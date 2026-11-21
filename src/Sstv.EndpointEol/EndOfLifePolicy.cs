using System.Globalization;

namespace Sstv.EndpointEol;

/// <summary>
/// Parsed, validated end-of-life schedule of an endpoint.
/// </summary>
/// <param name="BlockAfter">Date after which the endpoint is blocked.</param>
/// <param name="WarnAfter">Date when warning phase begins.</param>
/// <param name="ReplacedBy">Replacement hint.</param>
public sealed record EndOfLifePolicy(
    DateOnly BlockAfter,
    DateOnly? WarnAfter,
    string? ReplacedBy
)
{
    private const string DATE_FORMAT = "yyyy-MM-dd";

    /// <summary>
    /// Warning window applied when an attribute does not specify <c>WarnAfter</c> explicitly.
    /// Without it an endpoint would jump straight from "active" to "blocked",
    /// leaving no period in which remaining consumers could be observed.
    /// </summary>
    private const int DEFAULT_WARNING_WINDOW_DAYS = 90;

    /// <summary>
    /// Calculates lifecycle phase of the endpoint for the given date.
    /// </summary>
    /// <param name="today">Current date (UTC).</param>
    /// <returns>Lifecycle phase.</returns>
    public EndOfLifePhase GetPhase(DateOnly today)
    {
        if (today >= BlockAfter)
        {
            return EndOfLifePhase.Blocked;
        }

        return WarnAfter is { } warnAfter && today >= warnAfter
            ? EndOfLifePhase.Warning
            : EndOfLifePhase.Active;
    }

    /// <summary>
    /// Parses and validates <see cref="EndOfLifeAttribute"/> into <see cref="EndOfLifePolicy"/>.
    /// When the attribute does not set <c>WarnAfter</c>, a 90-day warning window before
    /// <c>BlockAfter</c> is assumed, so an endpoint always spends time in the observable
    /// warning phase.
    /// </summary>
    /// <param name="attribute">Attribute to parse.</param>
    /// <param name="context">Optional endpoint description used in error messages.</param>
    /// <returns>Parsed policy.</returns>
    /// <exception cref="InvalidOperationException">
    /// Dates have invalid format or WarnAfter is not earlier than BlockAfter.
    /// </exception>
    public static EndOfLifePolicy From(EndOfLifeAttribute attribute, string? context = null)
    {
        ArgumentNullException.ThrowIfNull(attribute);

        var blockAfter = ParseDate(attribute.BlockAfter, nameof(attribute.BlockAfter), context);
        var warnAfter = attribute.WarnAfter is null
            ? blockAfter.AddDays(-DEFAULT_WARNING_WINDOW_DAYS)
            : ParseDate(attribute.WarnAfter, nameof(attribute.WarnAfter), context);

        if (warnAfter >= blockAfter)
        {
            throw new InvalidOperationException(
                $"[{context}] EndOfLife: {nameof(attribute.WarnAfter)} ('{attribute.WarnAfter}') "
                + $"must be earlier than {nameof(attribute.BlockAfter)} ('{attribute.BlockAfter}')."
            );
        }

        return new EndOfLifePolicy(blockAfter, warnAfter, attribute.ReplacedBy);
    }

    private static DateOnly ParseDate(string value, string propertyName, string? context)
    {
        if (!DateOnly.TryParseExact(value, DATE_FORMAT, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
        {
            throw new InvalidOperationException(
                $"[{context}] EndOfLife: '{value}' is not a valid {propertyName} date. Expected format: '{DATE_FORMAT}'."
            );
        }

        return result;
    }
}
