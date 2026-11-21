using Microsoft.OpenApi;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Sstv.EndpointEol.Swashbuckle;

/// <summary>
/// Turns the "Replacement" note of a deprecated operation into a clickable link.
/// <para>
/// When <c>ReplacedBy</c> points to an operation that exists in the same document, the link is a
/// Swagger UI deep link (<c>#/{tag}/{operationId}</c>) that expands and scrolls to the successor
/// operation. Absolute <c>http</c>/<c>https</c> URLs are linked as external addresses. Anything that
/// cannot be resolved is left as plain code, so the document never contains a dead link.
/// </para>
/// </summary>
internal static class ReplacementLinkResolver
{
    private const string REPLACED_BY_EXTENSION = "x-eol-replaced-by";
    private const string REPLACEMENT_PREFIX = "Replacement: ";
    private const string DEFAULT_TAG = "default";

    private static readonly Regex _routeConstraint = new(
        @"\{\*{0,2}([^}:]+)(?::[^}]+)?\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    private static readonly Regex _nonWord = new(
        @"\W",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    /// <summary>
    /// Rewrites the replacement note of <paramref name="operation"/> into a Markdown link when the
    /// replacement target can be resolved. The operation is left untouched otherwise.
    /// </summary>
    /// <param name="document">Document being generated, used to look up the successor operation.</param>
    /// <param name="operation">Deprecated operation whose description should be linked.</param>
    /// <param name="method">HTTP method of the deprecated operation.</param>
    public static void Apply(OpenApiDocument document, OpenApiOperation operation, HttpMethod method)
    {
        if (operation.Description is null ||
            operation.Extensions is null ||
            !operation.Extensions.TryGetValue(REPLACED_BY_EXTENSION, out var extension) ||
            extension is not JsonNodeExtension { Node: JsonValue node } ||
            !node.TryGetValue<string>(out var replacedBy) ||
            string.IsNullOrEmpty(replacedBy))
        {
            return;
        }

        var plain = REPLACEMENT_PREFIX + "`" + replacedBy + "`";
        if (!operation.Description.Contains(plain, StringComparison.Ordinal))
        {
            return;
        }

        var href = ResolveHref(document, replacedBy, method);
        if (href is null)
        {
            return;
        }

        var linked = REPLACEMENT_PREFIX + "[`" + replacedBy + "`](" + href + ")";

        operation.Description = operation.Description.Replace(plain, linked, StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds the link target: an external URL, a Swagger UI deep link, or <c>null</c> when the
    /// replacement cannot be resolved unambiguously.
    /// </summary>
    private static string? ResolveHref(OpenApiDocument document, string replacedBy, HttpMethod currentMethod)
    {
        var value = replacedBy.Trim();

        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return value;
        }

        var (requestedMethod, path) = Parse(value);
        var target = FindOperation(document, path, requestedMethod, currentMethod);
        if (target is null)
        {
            return null;
        }

        var operationId = string.IsNullOrEmpty(target.Value.Operation.OperationId)
            ? CreateOperationId(target.Value.Method, target.Value.Path)
            : target.Value.Operation.OperationId;

        // Swagger UI builds its anchors from the operationId, so a generated one must be persisted.
        target.Value.Operation.OperationId = operationId;

        return "#/" + Uri.EscapeDataString(SelectTag(target.Value.Operation)) + "/" + Uri.EscapeDataString(operationId);
    }

    /// <summary>
    /// Splits an optional <c>METHOD </c> prefix from the replacement path.
    /// </summary>
    private static (HttpMethod? Method, string Path) Parse(string value)
    {
        var separator = value.IndexOf(' ', StringComparison.Ordinal);
        if (separator <= 0)
        {
            return (null, value);
        }

        var method = value[..separator].ToUpperInvariant() switch
        {
            "GET" => HttpMethod.Get,
            "PUT" => HttpMethod.Put,
            "POST" => HttpMethod.Post,
            "DELETE" => HttpMethod.Delete,
            "OPTIONS" => HttpMethod.Options,
            "HEAD" => HttpMethod.Head,
            "PATCH" => HttpMethod.Patch,
            "TRACE" => HttpMethod.Trace,
            _ => null
        };

        return method is null ? (null, value) : (method, value[(separator + 1)..].Trim());
    }

    /// <summary>
    /// Finds the operation the replacement points at. A path-only replacement is resolved only when
    /// it is unambiguous: the same HTTP method wins, a single-operation path is accepted, and
    /// anything else is treated as unresolved rather than linking to a random operation.
    /// </summary>
    private static TargetOperation? FindOperation(
        OpenApiDocument document,
        string path,
        HttpMethod? requestedMethod,
        HttpMethod currentMethod
    )
    {
        if (document.Paths is not { Count: > 0 } paths)
        {
            return null;
        }

        var normalizedPath = NormalizePath(path);

        foreach (var candidate in paths)
        {
            if (candidate.Value?.Operations is not { Count: > 0 } operations ||
                !string.Equals(NormalizePath(candidate.Key), normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (requestedMethod is { } exactMethod)
            {
                return operations.TryGetValue(exactMethod, out var exact)
                    ? new TargetOperation(exact, exactMethod, candidate.Key)
                    : null;
            }

            if (operations.TryGetValue(currentMethod, out var sameMethod))
            {
                return new TargetOperation(sameMethod, currentMethod, candidate.Key);
            }

            if (operations.Count == 1)
            {
                var only = operations.First();
                return new TargetOperation(only.Value, only.Key, candidate.Key);
            }

            return null;
        }

        return null;
    }

    /// <summary>
    /// Normalizes a route template so attribute values and OpenAPI path keys can be compared:
    /// leading slash, no trailing slash, no query/fragment and no route constraints
    /// (<c>{id:guid}</c> becomes <c>{id}</c>).
    /// </summary>
    private static string NormalizePath(string path)
    {
        var value = path.Trim();

        var suffix = value.IndexOfAny(['?', '#']);
        if (suffix >= 0)
        {
            value = value[..suffix];
        }

        value = _routeConstraint.Replace(value, "{$1}");

        if (!value.StartsWith('/'))
        {
            value = "/" + value;
        }

        return value.Length > 1 ? value.TrimEnd('/') : value;
    }

    /// <summary>
    /// Selects the tag Swagger UI groups the operation under. Tags containing a slash are skipped
    /// when possible: Swagger UI decodes the hash before splitting it, so such a tag cannot be
    /// deep-linked.
    /// </summary>
    private static string SelectTag(OpenApiOperation operation)
    {
        var names = operation.Tags?
            .Select(static x => x.Name)
            .OfType<string>()
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        if (names is not { Length: > 0 })
        {
            return DEFAULT_TAG;
        }

        return names.FirstOrDefault(static x => !x.Contains('/')) ?? names[0];
    }

    /// <summary>
    /// Generates a stable operationId from the HTTP method and route template. The format matches
    /// the fallback Swagger UI itself uses when an operation has no operationId.
    /// </summary>
    private static string CreateOperationId(HttpMethod method, string path) =>
        method.Method.ToLowerInvariant() + _nonWord.Replace(path, "_");

    private readonly record struct TargetOperation(OpenApiOperation Operation, HttpMethod Method, string Path);
}
