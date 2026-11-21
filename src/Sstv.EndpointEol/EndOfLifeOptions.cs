using Microsoft.AspNetCore.Http;

namespace Sstv.EndpointEol;

/// <summary>
/// Configuration of the end-of-life feature.
/// </summary>
public sealed class EndOfLifeOptions
{
    /// <summary>
    /// Global enforcement mode. Default is <see cref="EnforcementMode.WarnOnly"/> —
    /// safe to enable everywhere, nothing gets blocked until explicitly switched to Enforce.
    /// </summary>
    public EnforcementMode EnforcementMode { get; set; } = EnforcementMode.WarnOnly;

    /// <summary>
    /// OpenTelemetry meter name. Must be subscribed via AddMeter to be exported.
    /// </summary>
    public string MeterName { get; set; } = "Sstv.EndpointEol";

    /// <summary>
    /// Minimum interval in minutes between Warning log records for the same route+phase.
    /// Zero disables throttling (log every request). Default: 1 minute.
    /// </summary>
    public int LogThrottleMinutes { get; set; } = 1;

    /// <summary>
    /// Absolute URL of a page describing the sunset process.
    /// When set, announced via Link header rel="sunset".
    /// </summary>
    public Uri? SunsetDocsUrl { get; set; }

    /// <summary>
    /// Claim type carrying the consumer identity, reported in warning logs to identify
    /// who still calls a deprecated endpoint. Default: "sub".
    /// When the claim is missing, falls back to <see cref="System.Security.Claims.ClaimTypes.NameIdentifier"/>,
    /// which covers JWT handlers that map inbound claims
    /// (<c>JwtBearerOptions.MapInboundClaims</c>).
    /// </summary>
    public string? UserIdClaimType { get; set; } = "sub";

    /// <summary>
    /// Tracks distinct consumers per route and exposes them as <c>eol.consumers{route}</c>.
    /// This is the answer to "who is still calling the legacy endpoint".
    /// Default: true. In-memory state grows with the number of distinct consumers per route.
    /// </summary>
    public bool CollectConsumers { get; set; } = true;

    /// <summary>
    /// Builds the body of the <c>410 Gone</c> response.
    /// Set it to align the payload with an application-specific error contract
    /// (for example, to add an error code or a localized message).
    /// The response is always sent with <c>Cache-Control: no-store</c>.
    /// </summary>
    public Func<HttpContext, EndOfLifePolicy, GoneProblemDetails>? GoneProblemDetailsFactory { get; set; }
}
