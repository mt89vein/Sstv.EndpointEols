using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Sstv.EndpointEol.Swashbuckle;

/// <summary>
/// Collects all operations marked with <see cref="EndOfLifeAttribute"/> under a single
/// <c>Deprecated / EOL</c> tag, so the whole list of sunsetted APIs is visible in Swagger UI
/// without filtering. Original tags are preserved, therefore each operation still shows up
/// under its resource group as well.
/// <para>
/// Also turns the replacement note into a clickable link: a Swagger UI deep link when the
/// successor operation is part of the document, an external link for absolute URLs.
/// </para>
/// </summary>
public sealed class EndOfLifeDocumentFilter : IDocumentFilter
{
    /// <summary>
    /// Tag the end-of-life operations are grouped under.
    /// </summary>
    public const string TAG_NAME = "Deprecated / EOL";

    /// <summary>
    /// Vendor extension set by <see cref="EndOfLifeOperationFilter"/> on every marked operation.
    /// Document filters run after operation filters, so its presence is the reliable signal.
    /// </summary>
    private const string SUNSET_EXTENSION = "x-sunset";

    private const string TAG_DESCRIPTION =
        "Endpoints scheduled for removal. Sunset date and replacement are listed per operation.";

    /// <inheritdoc />
    public void Apply(OpenApiDocument document, DocumentFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);

        if (document.Paths is not { Count: > 0 })
        {
            return;
        }

        var marked = new List<(HttpMethod Method, OpenApiOperation Operation)>();

        foreach (var path in document.Paths)
        {
            if (path.Value?.Operations is not { } operations)
            {
                continue;
            }

            foreach (var (method, operation) in operations)
            {
                if (operation.Extensions?.ContainsKey(SUNSET_EXTENSION) is true)
                {
                    marked.Add((method, operation));
                }
            }
        }

        if (marked.Count == 0)
        {
            return;
        }

        EnsureTag(document);

        foreach (var (method, operation) in marked)
        {
            // Swashbuckle serializes operation tags as references, so the tag must already be
            // present at the document level (microsoft/OpenAPI.NET#2319).
            if (operation.Tags is null)
            {
                operation.Tags = new HashSet<OpenApiTagReference> { new(TAG_NAME, document) };
            }
            else if (!operation.Tags.Any(static x => x.Name == TAG_NAME))
            {
                operation.Tags.Add(new OpenApiTagReference(TAG_NAME, document));
            }

            ReplacementLinkResolver.Apply(document, operation, method);
        }
    }

    /// <summary>
    /// Ensures the document contains the end-of-life tag and places it first, so it is the group
    /// consumers see when opening the document. The existing instance is reused to keep already
    /// created operation references valid.
    /// </summary>
    private static void EnsureTag(OpenApiDocument document)
    {
        document.Tags ??= new HashSet<OpenApiTag>();

        var tag = document.Tags.FirstOrDefault(static x => x.Name == TAG_NAME)
            ?? new OpenApiTag { Name = TAG_NAME };

        tag.Description = TAG_DESCRIPTION;

        // A single instance is shared between the document and its operations, the same way
        // Swashbuckle shares the tags it generates.
        document.Tags = new HashSet<OpenApiTag>([tag, .. document.Tags.Where(static x => x.Name != TAG_NAME)]);
    }
}
