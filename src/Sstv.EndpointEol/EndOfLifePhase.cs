namespace Sstv.EndpointEol;

/// <summary>
/// Lifecycle phase of an endpoint marked with <see cref="EndOfLifeAttribute"/>,
/// calculated against the current date.
/// </summary>
public enum EndOfLifePhase
{
    /// <summary>
    /// Endpoint works as usual, but already announces its sunset date.
    /// </summary>
    Active = 0,

    /// <summary>
    /// Warn date reached: every call is logged with Warning level and counted in metrics.
    /// </summary>
    Warning = 1,

    /// <summary>
    /// Block date reached: requests are rejected (depending on the enforcement mode).
    /// </summary>
    Blocked = 2
}
