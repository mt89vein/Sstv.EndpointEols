using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sstv.EndpointEol;

/// <summary>
/// Extension methods for <see cref="IEndpointRouteBuilder"/>.
/// </summary>
public static class DiagnosticsEndpointExtensions
{
    /// <summary>
    /// Serializer settings owned by the library, so the diagnostics contract does not depend on
    /// how the host application configured its JSON options: enums as camelCase strings,
    /// camelCase property names.
    /// </summary>
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// Maps diagnostic GET endpoint returning all endpoints marked with <see cref="EndOfLifeAttribute"/>,
    /// sorted by block date ascending (soonest to die first).
    /// </summary>
    /// <param name="endpoints">Endpoint route builder.</param>
    /// <param name="pattern">Endpoint route pattern.</param>
    /// <returns>Convention builder for further configuration (e.g. RequireAuthorization).</returns>
    public static IEndpointConventionBuilder MapEndOfLifeDiagnostics(
        this IEndpointRouteBuilder endpoints,
        string pattern = "_meta/end-of-life"
    )
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        return endpoints.MapGet(
            pattern,
            static (IEndpointEolRegistry registry, TimeProvider timeProvider, IOptionsMonitor<EndOfLifeOptions> options) =>
                TypedResults.Json(CreateReport(registry, timeProvider, options.CurrentValue), _jsonOptions)
        );
    }

    /// <summary>
    /// Builds the diagnostics report for the given options.
    /// </summary>
    /// <param name="registry">Endpoint eol registry.</param>
    /// <param name="timeProvider">Time provider.</param>
    /// <param name="options">Current options.</param>
    /// <returns>Report snapshot.</returns>
    internal static EndpointEolReport CreateReport(
        IEndpointEolRegistry registry,
        TimeProvider timeProvider,
        EndOfLifeOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);

        return new EndpointEolReport
        {
            ServerTime = timeProvider.GetUtcNow(),
            EnforcementMode = options.EnforcementMode,
            Items = registry.GetSnapshot()
        };
    }
}