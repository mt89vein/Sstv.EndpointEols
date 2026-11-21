namespace Sstv.EndpointEol;

/// <summary>
/// Global enforcement mode of the end-of-life feature (kill switch).
/// </summary>
public enum EnforcementMode
{
    /// <summary>
    /// Feature is completely disabled, metrics included.
    /// </summary>
    Off = 0,

    /// <summary>
    /// Headers, logs and metrics are emitted, but nothing is ever blocked.
    /// Recommended default until usage metrics prove it is safe to enforce.
    /// </summary>
    WarnOnly = 1,

    /// <summary>
    /// Requests to endpoints past their block date are rejected with 410 Gone.
    /// </summary>
    Enforce = 2,

    /// <summary>
    /// Silent observation: metrics (including the distinct consumer count) are emitted,
    /// but no header is added, nothing is logged and nothing is blocked.
    /// Use it when clients must not see any change in the response, e.g. to run a trial
    /// measurement or to introduce the feature in an environment that breaks on unexpected headers.
    /// </summary>
    MetricsOnly = 3
}
