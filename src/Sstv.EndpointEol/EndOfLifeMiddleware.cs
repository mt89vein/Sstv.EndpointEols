using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net.Mime;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sstv.EndpointEol;

/// <summary>
/// Intercepts requests to endpoints marked with <see cref="EndOfLifeAttribute"/>.
/// Always announces sunset date via standard HTTP headers, logs and counts requests during the
/// warning phase, rejects requests after the block date.
/// Must be placed after <c>UseRouting</c>.
/// </summary>
internal sealed class EndOfLifeMiddleware
{
    private const string SUNSET_HEADER = "Sunset";
    private const string DEPRECATION_HEADER = "Deprecation";
    private const string LINK_HEADER = "Link";
    private const string LOCATION_HEADER = "Location";
    private const string CACHE_CONTROL_HEADER = "Cache-Control";
    private const string NO_STORE = "no-store";
    private const string PROBLEM_JSON = MediaTypeNames.Application.ProblemJson;
    private const string GONE_TYPE = "https://developer.mozilla.org/docs/Web/HTTP/Status/410";
    private const string GONE_TITLE = "Endpoint has reached end of life";
    private const string THROTTLE_KEY_PREFIX = "eol:";
    private const string ANONYMOUS_CONSUMER = "anonymous";

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly RequestDelegate _next;
    private readonly TimeProvider _timeProvider;
    private readonly IOptionsMonitor<EndOfLifeOptions> _options;
    private readonly EndOfLifeMetrics _metrics;
    private readonly ILogger<EndOfLifeMiddleware> _logger;
    private readonly IMemoryCache _throttleCache;

    /// <summary>
    /// Per-endpoint cache of the parsed policy and preformatted header values.
    /// Keyed by <see cref="Endpoint"/>: entries live exactly as long as the endpoint itself,
    /// so rebuilt routing tables cannot leave stale policies behind.
    /// </summary>
    private readonly ConditionalWeakTable<Endpoint, EndpointPolicy> _policies = new();

    /// <summary>
    /// Creates new instance of <see cref="EndOfLifeMiddleware"/>.
    /// </summary>
    /// <param name="next">Next middleware in pipeline.</param>
    /// <param name="timeProvider">Time provider.</param>
    /// <param name="options">Options monitor, so the enforcement kill-switch can be flipped without a restart.</param>
    /// <param name="metrics">Metrics collector.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="throttleCache">Cache backing the log throttling windows.</param>
    public EndOfLifeMiddleware(
        RequestDelegate next,
        TimeProvider timeProvider,
        IOptionsMonitor<EndOfLifeOptions> options,
        EndOfLifeMetrics metrics,
        ILogger<EndOfLifeMiddleware> logger,
        IMemoryCache throttleCache
    )
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(throttleCache);

        _next = next;
        _timeProvider = timeProvider;
        _options = options;
        _metrics = metrics;
        _logger = logger;
        _throttleCache = throttleCache;
    }

    /// <summary>
    /// Processes request.
    /// </summary>
    /// <param name="context">Http context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        // Options are re-read per request on purpose: flipping EnforcementMode through
        // configuration must take effect without restarting the application.
        var options = _options.CurrentValue;

        if (options.EnforcementMode == EnforcementMode.Off)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var endpoint = context.GetEndpoint();
        var attributes = endpoint?.Metadata.GetOrderedMetadata<EndOfLifeAttribute>();
        if (endpoint is null || attributes is not { Count: > 0 })
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Action-level attribute overrides controller-level one. The cache hit path allocates nothing.
        if (!_policies.TryGetValue(endpoint, out var policy))
        {
            policy = _policies.GetValue(endpoint, e => EndpointPolicy.Create(e, attributes[^1]));
        }

        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var phase = policy.Policy.GetPhase(today);
        var route = GetRouteTemplate(endpoint);

        // MetricsOnly observes silently: the response must look exactly as if the feature was off.
        var visible = options.EnforcementMode != EnforcementMode.MetricsOnly;

        if (visible)
        {
            policy.ApplyHeaders(context.Response, options, phase);
        }

        // Counted in every phase: the active phase is the baseline that tells apart
        // "traffic dropped to zero" from "nothing was ever measured".
        _metrics.RecordRequest(route, GetHttpMethods(endpoint), phase);

        if (phase != EndOfLifePhase.Active)
        {
            var userId = GetUserId(context, options);
            _metrics.RecordConsumer(route, userId);

            if (visible)
            {
                LogThrottled(route, phase, policy, policy.Policy.BlockAfter.DayNumber - today.DayNumber, userId, options);
            }
        }

        if (phase == EndOfLifePhase.Blocked && options.EnforcementMode == EnforcementMode.Enforce)
        {
            await WriteGoneResponseAsync(context, policy.Policy, options).ConfigureAwait(false);
            return;
        }

        await _next(context).ConfigureAwait(false);
    }

    private void LogThrottled(
        string route,
        EndOfLifePhase phase,
        EndpointPolicy policy,
        int daysLeft,
        string? userId,
        EndOfLifeOptions options
    )
    {
        if (options.LogThrottleMinutes > 0
            && !TryClaimThrottleWindow(ThrottleKey(route, phase, userId), TimeSpan.FromMinutes(options.LogThrottleMinutes)))
        {
            return;
        }

        if (phase != EndOfLifePhase.Blocked)
        {
            _logger.LogWarning(
                "Deprecated API called. Route: {Route}. Phase: {Phase}. Days until shutdown: {DaysLeft}. BlockAfter: {BlockAfter}. Replacement: {Replacement}. User: {UserId}",
                route, phase, daysLeft, policy.BlockAfterDate, policy.Policy.ReplacedBy, userId
            );

            return;
        }

        if (options.EnforcementMode == EnforcementMode.Enforce)
        {
            _logger.LogWarning(
                "Deprecated API call rejected. Route: {Route}. BlockAfter: {BlockAfter}. Replacement: {Replacement}. User: {UserId}",
                route, policy.BlockAfterDate, policy.Policy.ReplacedBy, userId
            );

            return;
        }

        _logger.LogWarning(
            "Deprecated API called after its block date, still served because enforcement mode is {EnforcementMode}. Route: {Route}. BlockAfter: {BlockAfter}. Replacement: {Replacement}. User: {UserId}",
            route, options.EnforcementMode, policy.BlockAfterDate, policy.Policy.ReplacedBy, userId
        );
    }

    /// <summary>
    /// Atomically claims a throttle window. The cache entry is evicted by
    /// <see cref="IMemoryCache"/> once the window elapses, so keys cannot pile up.
    /// </summary>
    private bool TryClaimThrottleWindow(string key, TimeSpan window)
    {
        var slot = _throttleCache.GetOrCreate(key, static _ => new ThrottleSlot())!;

        return slot.TryClaim(_timeProvider.GetUtcNow(), window);
    }

    /// <summary>
    /// Throttle key includes the consumer: the point of the warning phase is to see who still
    /// calls the endpoint, and a per-route window would hide all but one user per interval.
    /// </summary>
    private static string ThrottleKey(string route, EndOfLifePhase phase, string? userId) =>
        $"{THROTTLE_KEY_PREFIX}{route}|{phase}|{userId ?? ANONYMOUS_CONSUMER}";

    private static async Task WriteGoneResponseAsync(
        HttpContext context,
        EndOfLifePolicy policy,
        EndOfLifeOptions options
    )
    {
        var body = options.GoneProblemDetailsFactory?.Invoke(context, policy)
            ?? CreateGoneProblemDetails(context, policy);

        var json = JsonSerializer.SerializeToUtf8Bytes(body, _jsonOptions);

        var response = context.Response;
        response.StatusCode = StatusCodes.Status410Gone;
        response.ContentType = PROBLEM_JSON;
        response.ContentLength = json.Length;
        response.Headers[CACHE_CONTROL_HEADER] = NO_STORE;

        // Not standard for 410, but widely understood as "the successor lives here" and cheap
        // for clients that only look at Location. Only set when the hint is a real path;
        // the standards-compliant way to announce a successor is Link rel="successor-version".
        if (policy.ReplacedBy is { } successor && successor.StartsWith('/'))
        {
            response.Headers[LOCATION_HEADER] = successor;
        }

        // A 410 must never be served from a cache, and a HEAD response carries no body.
        if (HttpMethods.IsHead(context.Request.Method))
        {
            return;
        }

        await response.Body.WriteAsync(json.AsMemory()).ConfigureAwait(false);
    }

    private static GoneProblemDetails CreateGoneProblemDetails(HttpContext context, EndOfLifePolicy policy) =>
        new()
        {
            Type = GONE_TYPE,
            Title = GONE_TITLE,
            Status = StatusCodes.Status410Gone,
            Detail = $"This endpoint was permanently shut down on {policy.BlockAfter:yyyy-MM-dd}."
                + (policy.ReplacedBy is null ? null : $" Migrate to: {policy.ReplacedBy}."),
            Sunset = policy.BlockAfter.ToString("yyyy-MM-dd"),
            Replacement = policy.ReplacedBy,
            TraceId = Activity.Current?.Id ?? context.TraceIdentifier
        };

    private static string GetRouteTemplate(Endpoint endpoint)
    {
        if (endpoint is RouteEndpoint { RoutePattern.RawText: { } routePattern })
        {
            return RouteTemplate.Normalize(routePattern);
        }

        return endpoint.DisplayName ?? "unknown";
    }

    private static IReadOnlyList<string>? GetHttpMethods(Endpoint endpoint) =>
        endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;

    private static string? GetUserId(HttpContext context, EndOfLifeOptions options)
    {
        if (options.UserIdClaimType is { Length: > 0 } claimType
            && context.User.FindFirstValue(claimType) is { Length: > 0 } primary)
        {
            return primary;
        }

        // Covers JWT handlers with MapInboundClaims enabled, where "sub" is mapped to a long claim URI.
        return context.User.FindFirstValue(ClaimTypes.NameIdentifier);
    }

    /// <summary>
    /// Everything derived from an endpoint's schedule: parsed dates plus header values that are
    /// identical for every request, so they are formatted once instead of per request.
    /// </summary>
    private sealed record EndpointPolicy(
        EndOfLifePolicy Policy,
        string BlockAfterDate,
        string SunsetHeaderValue,
        string? DeprecationHeaderValue,
        string? SuccessorLinkValue
    )
    {
        public static EndpointPolicy Create(Endpoint endpoint, EndOfLifeAttribute attribute)
        {
            var policy = EndOfLifePolicy.From(attribute, endpoint.DisplayName);
            var midnight = TimeOnly.MinValue;

            return new EndpointPolicy(
                policy,
                policy.BlockAfter.ToString("yyyy-MM-dd"),
                policy.BlockAfter.ToDateTime(midnight, DateTimeKind.Utc).ToString("R"),
                policy.WarnAfter is { } warnAfter
                    ? $"@{new DateTimeOffset(warnAfter.ToDateTime(midnight, DateTimeKind.Utc)).ToUnixTimeSeconds()}"
                    : null,
                policy.ReplacedBy is { } replacedBy && replacedBy.StartsWith('/')
                    ? $"<{replacedBy}>; rel=\"successor-version\""
                    : null
            );
        }

        public void ApplyHeaders(HttpResponse response, EndOfLifeOptions options, EndOfLifePhase phase)
        {
            response.Headers[SUNSET_HEADER] = SunsetHeaderValue;

            // Announced only once the warning phase is reached: before that the endpoint is
            // not deprecated yet, it merely carries a future sunset date.
            if (phase != EndOfLifePhase.Active && DeprecationHeaderValue is { } deprecation)
            {
                response.Headers[DEPRECATION_HEADER] = deprecation;
            }

            if (options.SunsetDocsUrl is { } docsUrl)
            {
                // Not cached: options can be reloaded at any time.
                response.Headers.Append(LINK_HEADER, $"<{docsUrl}>; rel=\"sunset\"");
            }

            if (SuccessorLinkValue is { } successorLink)
            {
                response.Headers.Append(LINK_HEADER, successorLink);
            }
        }
    }

    /// <summary>
    /// Throttle window claim shared by all requests hitting the same key at the same moment.
    /// </summary>
    private sealed class ThrottleSlot
    {
        private long _claimedAtUtcTicks;

        public bool TryClaim(DateTimeOffset now, TimeSpan window)
        {
            var nowTicks = now.UtcTicks;

            while (true)
            {
                var claimedAt = Interlocked.Read(ref _claimedAtUtcTicks);

                if (claimedAt != 0 && nowTicks - claimedAt < window.Ticks)
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref _claimedAtUtcTicks, nowTicks, claimedAt) == claimedAt)
                {
                    return true;
                }
            }
        }
    }
}
