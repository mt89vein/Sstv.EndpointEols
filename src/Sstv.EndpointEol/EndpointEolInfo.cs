namespace Sstv.EndpointEol;

/// <summary>
/// Description of a single endpoint marked with <see cref="EndOfLifeAttribute"/>.
/// </summary>
public sealed record EndpointEolInfo
{
    /// <summary>
    /// Comma-separated HTTP methods of the endpoint.
    /// </summary>
    public required string Method { get; init; }

    /// <summary>
    /// Route template of the endpoint.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Endpoint display name (usually controller action full name).
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Date when warning phase begins.
    /// </summary>
    public DateOnly? WarnAfter { get; init; }

    /// <summary>
    /// Date after which the endpoint is blocked.
    /// </summary>
    public required DateOnly BlockAfter { get; init; }

    /// <summary>
    /// Replacement hint.
    /// </summary>
    public string? ReplacedBy { get; init; }

    /// <summary>
    /// Current lifecycle phase.
    /// </summary>
    public required EndOfLifePhase Phase { get; init; }

    /// <summary>
    /// Days left until the block date. Negative when already blocked.
    /// </summary>
    public required int DaysToBlock { get; init; }
}

/// <summary>
/// Full report about all endpoints marked with <see cref="EndOfLifeAttribute"/>,
/// sorted by block date ascending (soonest to die first).
/// </summary>
public sealed record EndpointEolReport
{
    /// <summary>
    /// Server time (UTC) the report was generated at.
    /// </summary>
    public required DateTimeOffset ServerTime { get; init; }

    /// <summary>
    /// Enforcement mode in effect at the moment the report was generated.
    /// Phase and days to block describe the schedule; this field tells whether the schedule
    /// is actually enforced, so a report showing <c>blocked</c> endpoints in
    /// <c>warnOnly</c> mode is unambiguous.
    /// </summary>
    public required EnforcementMode EnforcementMode { get; init; }

    /// <summary>
    /// All tracked endpoints.
    /// </summary>
    public required IReadOnlyList<EndpointEolInfo> Items { get; init; }
}
