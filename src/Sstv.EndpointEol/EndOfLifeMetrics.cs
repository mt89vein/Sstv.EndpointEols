using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Sstv.EndpointEol;

/// <summary>
/// Collects OpenTelemetry metrics about requests to endpoints marked with <see cref="EndOfLifeAttribute"/>.
/// </summary>
internal sealed class EndOfLifeMetrics : IDisposable
{
    private const string ANY_METHOD = "any";

    /// <summary>
    /// HTTP verbs are a small, finite set, so their lowercase forms (the OpenTelemetry
    /// convention) are cached instead of being allocated on every request.
    /// </summary>
    private static readonly ConcurrentDictionary<string, string> _methodNames
        = new(StringComparer.OrdinalIgnoreCase);

    private readonly Meter _meter;
    private readonly Counter<long> _requests;
    private readonly UpDownCounter<long> _consumers;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _knownConsumers
        = new(StringComparer.Ordinal);
    private readonly EndOfLifeOptions _options;

    /// <summary>
    /// Creates new instance of <see cref="EndOfLifeMetrics"/>.
    /// </summary>
    /// <param name="meterFactory">Meter factory.</param>
    /// <param name="options">Options.</param>
    public EndOfLifeMetrics(IMeterFactory meterFactory, IOptions<EndOfLifeOptions> options)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;

        _meter = meterFactory.Create(_options.MeterName);
        _requests = _meter.CreateCounter<long>(
            name: "eol.requests",
            unit: "{request}",
            description: "Requests to endpoints marked with [EndOfLife]."
        );
        _consumers = _meter.CreateUpDownCounter<long>(
            name: "eol.consumers",
            unit: "{consumer}",
            description: "Distinct consumers observed on endpoints marked with [EndOfLife], since process start."
        );
    }

    /// <summary>
    /// Counts one request to a sunsetted endpoint.
    /// Tags use route template (not raw path) to keep metric cardinality low.
    /// One measurement is recorded per HTTP method, so <c>by (method)</c> stays meaningful
    /// for endpoints accepting several verbs.
    /// </summary>
    /// <param name="route">Route template.</param>
    /// <param name="httpMethods">HTTP methods of the endpoint, or <c>null</c>/empty when unknown.</param>
    /// <param name="phase">Lifecycle phase at the moment of the request.</param>
    public void RecordRequest(string route, IReadOnlyList<string>? httpMethods, EndOfLifePhase phase)
    {
        if (!_requests.Enabled)
        {
            return;
        }

        var phaseName = GetPhaseName(phase);

        if (httpMethods is not { Count: > 0 })
        {
            _requests.Add(1, new TagList
            {
                { "route", route },
                { "method", ANY_METHOD },
                { "phase", phaseName }
            });

            return;
        }

        foreach (var httpMethod in httpMethods)
        {
            _requests.Add(1, new TagList
            {
                { "route", route },
                { "method", GetMethodName(httpMethod) },
                { "phase", phaseName }
            });
        }
    }

    /// <summary>
    /// Registers the consumer behind a request. Repeated calls for the same route and consumer
    /// are counted only once, so the instrument value equals the number of distinct consumers
    /// seen since the process started.
    /// </summary>
    /// <param name="route">Route template.</param>
    /// <param name="userId">Consumer identity, or <c>null</c> when it cannot be resolved.</param>
    public void RecordConsumer(string route, string? userId)
    {
        if (!_consumers.Enabled || !_options.CollectConsumers || string.IsNullOrEmpty(userId))
        {
            return;
        }

        var consumers = _knownConsumers.GetOrAdd(
            route,
            static _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal)
        );

        if (consumers.TryAdd(userId, 0))
        {
            _consumers.Add(1, new TagList { { "route", route } });
        }
    }

    private static string GetPhaseName(EndOfLifePhase phase) => phase switch
    {
        EndOfLifePhase.Active => "active",
        EndOfLifePhase.Warning => "warning",
        EndOfLifePhase.Blocked => "blocked",
        _ => "unknown"
    };

    /// <summary>
    /// OrdinalIgnoreCase keeps case variants of the same verb on a single cache entry, so even
    /// a custom <c>MapMethods("PURGE")</c> verb resolves without allocating after the first request.
    /// </summary>
    private static string GetMethodName(string httpMethod) =>
        _methodNames.GetOrAdd(httpMethod, static method => method.ToLowerInvariant());

    /// <inheritdoc />
    public void Dispose()
    {
        _meter.Dispose();
    }
}