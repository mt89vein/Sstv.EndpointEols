using Microsoft.AspNetCore.Builder;

namespace Sstv.EndpointEol;

/// <summary>
/// Extension methods for <see cref="RouteHandlerBuilder"/> that add <see cref="EndOfLifeAttribute"/>
/// metadata to Minimal API endpoints.
/// </summary>
public static class MinimalApiExtensions
{
    /// <summary>
    /// Marks a Minimal API endpoint with end-of-life metadata.
    /// </summary>
    /// <param name="builder">Route handler builder.</param>
    /// <param name="blockAfter">
    /// ISO date (yyyy-MM-dd) after which requests are rejected with 410 Gone.
    /// </param>
    /// <param name="warnAfter">
    /// ISO date (yyyy-MM-dd) when warning phase begins. Until then the endpoint only announces its sunset date via headers.
    /// </param>
    /// <param name="replacedBy">
    /// Replacement hint: path of the successor endpoint or migration guide reference.
    /// </param>
    /// <returns>Route handler builder for chaining.</returns>
    public static RouteHandlerBuilder WithEndOfLife(
        this RouteHandlerBuilder builder,
        string blockAfter,
        string? warnAfter = null,
        string? replacedBy = null
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(blockAfter);

        var attribute = new EndOfLifeAttribute(blockAfter)
        {
            WarnAfter = warnAfter,
            ReplacedBy = replacedBy
        };

        return builder.WithMetadata(attribute);
    }
}
