namespace Sstv.EndpointEol;

/// <summary>
/// Marks a controller or an action as scheduled for retirement (sunsetting).
/// Attribute arguments must be compile-time constants, therefore dates are passed
/// as ISO 8601 strings ("yyyy-MM-dd") and validated once at startup.
/// Method-level attribute overrides controller-level attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
public sealed class EndOfLifeAttribute : Attribute
{
    /// <summary>
    /// Creates new instance of <see cref="EndOfLifeAttribute"/>.
    /// </summary>
    /// <param name="blockAfter">
    /// ISO date (yyyy-MM-dd) after which requests are rejected with 410 Gone.
    /// </param>
    public EndOfLifeAttribute(string blockAfter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blockAfter);

        BlockAfter = blockAfter;
    }

    /// <summary>
    /// ISO date (yyyy-MM-dd) after which the endpoint is permanently blocked.
    /// </summary>
    public string BlockAfter { get; }

    /// <summary>
    /// ISO date (yyyy-MM-dd) when warning phase begins.
    /// Until then the endpoint only announces its sunset date via headers.
    /// </summary>
    public string? WarnAfter { get; set; }

    /// <summary>
    /// Replacement hint: path of the successor endpoint or migration guide reference.
    /// Values starting with "/" are additionally announced via Link header rel="successor-version".
    /// </summary>
    public string? ReplacedBy { get; set; }
}
