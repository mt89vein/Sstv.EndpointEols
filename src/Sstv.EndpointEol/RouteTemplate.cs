namespace Sstv.EndpointEol;

/// <summary>
/// Normalizes route patterns so metrics, diagnostics and logs use the same representation.
/// MVC route patterns have no leading slash (<c>api/v1/books/{id:guid}</c>), while route patterns
/// built by Minimal APIs usually start with one; the public contract always starts with <c>/</c>.
/// </summary>
internal static class RouteTemplate
{
    /// <summary>
    /// Prefixes a route pattern with <c>/</c> when needed.
    /// </summary>
    /// <param name="routePattern">Raw route pattern.</param>
    /// <returns>Normalized route template.</returns>
    public static string Normalize(string? routePattern)
    {
        if (string.IsNullOrEmpty(routePattern))
        {
            return "/";
        }

        return routePattern.StartsWith('/') ? routePattern : $"/{routePattern}";
    }
}
