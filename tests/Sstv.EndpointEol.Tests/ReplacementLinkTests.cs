using Microsoft.OpenApi;
using Sstv.EndpointEol.Swashbuckle;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Text.Json.Nodes;

namespace Sstv.EndpointEol.Tests;

/// <summary>
/// Tests for the replacement links added by <see cref="EndOfLifeDocumentFilter"/>: a Swagger UI
/// deep link when the successor operation is part of the document, an external link for absolute
/// URLs, and plain code when nothing can be resolved.
/// </summary>
[TestFixture]
public sealed class ReplacementLinkTests
{
    [Test]
    public void Apply_WhenReplacementExists_AddsDeepLinkAndGeneratesOperationId()
    {
        var document = new OpenApiDocument { Paths = new OpenApiPaths() };
        var deprecated = AddOperation(
            document, "/api/v1/books/{id}", HttpMethod.Delete,
            description: Note("/api/v2/books/{id}"),
            tags: ["Books"],
            marked: true,
            replacedBy: "/api/v2/books/{id}"
        );
        var replacement = AddOperation(document, "/api/v2/books/{id}", HttpMethod.Delete, tags: ["Books"]);

        Apply(document);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deprecated.Description, Does.Contain("[`/api/v2/books/{id}`](#/Books/delete_api_v2_books__id_)"));
            Assert.That(replacement.OperationId, Is.EqualTo("delete_api_v2_books__id_"));
        }
    }

    [Test]
    public void Apply_WhenReplacementHasOperationId_KeepsIt()
    {
        var document = new OpenApiDocument { Paths = new OpenApiPaths() };
        var deprecated = AddOperation(
            document, "/api/v1/books/{id}", HttpMethod.Delete,
            description: Note("/api/v2/books/{id}"),
            tags: ["Books"],
            marked: true,
            replacedBy: "/api/v2/books/{id}"
        );
        var replacement = AddOperation(
            document, "/api/v2/books/{id}", HttpMethod.Delete,
            tags: ["Books"],
            operationId: "deleteBookV2"
        );

        Apply(document);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deprecated.Description, Does.Contain("](#/Books/deleteBookV2)"));
            Assert.That(replacement.OperationId, Is.EqualTo("deleteBookV2"));
        }
    }

    [Test]
    public void Apply_WhenReplacementPathHasConstraint_MatchesDocumentPath()
    {
        var document = new OpenApiDocument { Paths = new OpenApiPaths() };
        var deprecated = AddOperation(
            document, "/api/v1/books/{id}", HttpMethod.Delete,
            description: Note("/api/v2/books/{id:guid}"),
            tags: ["Books"],
            marked: true,
            replacedBy: "/api/v2/books/{id:guid}"
        );
        AddOperation(document, "/api/v2/books/{id}", HttpMethod.Delete, tags: ["Books"]);

        Apply(document);

        Assert.That(deprecated.Description, Does.Contain("](#/Books/delete_api_v2_books__id_)"));
    }

    [Test]
    public void Apply_WhenReplacementSpecifiesMethod_SelectsThatOperation()
    {
        var document = new OpenApiDocument { Paths = new OpenApiPaths() };
        var deprecated = AddOperation(
            document, "/api/v1/books/{id}", HttpMethod.Delete,
            description: Note("DELETE /api/v2/books/{id}"),
            tags: ["Books"],
            marked: true,
            replacedBy: "DELETE /api/v2/books/{id}"
        );
        AddOperation(
            document, "/api/v2/books/{id}", HttpMethod.Get,
            tags: ["Books"],
            operationId: "getBookV2"
        );
        AddOperation(
            document, "/api/v2/books/{id}", HttpMethod.Delete,
            tags: ["Books"],
            operationId: "deleteBookV2"
        );

        Apply(document);

        Assert.That(deprecated.Description, Does.Contain("](#/Books/deleteBookV2)"));
    }

    [Test]
    public void Apply_WhenPathOnlyReplacementMatchesCurrentMethod_SelectsIt()
    {
        var document = new OpenApiDocument { Paths = new OpenApiPaths() };
        var deprecated = AddOperation(
            document, "/api/v1/books/{id}", HttpMethod.Delete,
            description: Note("/api/v2/books/{id}"),
            tags: ["Books"],
            marked: true,
            replacedBy: "/api/v2/books/{id}"
        );
        AddOperation(document, "/api/v2/books/{id}", HttpMethod.Get, tags: ["Books"], operationId: "getBookV2");
        AddOperation(document, "/api/v2/books/{id}", HttpMethod.Delete, tags: ["Books"], operationId: "deleteBookV2");

        Apply(document);

        Assert.That(deprecated.Description, Does.Contain("](#/Books/deleteBookV2)"));
    }

    [Test]
    public void Apply_WhenPathOnlyReplacementIsAmbiguous_LeavesPlainText()
    {
        var document = new OpenApiDocument { Paths = new OpenApiPaths() };
        var deprecated = AddOperation(
            document, "/api/v1/books", HttpMethod.Delete,
            description: Note("/api/v2/books"),
            tags: ["Books"],
            marked: true,
            replacedBy: "/api/v2/books"
        );
        var get = AddOperation(document, "/api/v2/books", HttpMethod.Get, tags: ["Books"]);
        var post = AddOperation(document, "/api/v2/books", HttpMethod.Post, tags: ["Books"]);

        Apply(document);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deprecated.Description, Does.Contain("Replacement: `/api/v2/books`."));
            Assert.That(deprecated.Description, Does.Not.Contain("]("));
            Assert.That(get.OperationId, Is.Null);
            Assert.That(post.OperationId, Is.Null);
        }
    }

    [Test]
    public void Apply_WhenReplacementIsAbsoluteUrl_LinksExternally()
    {
        var document = new OpenApiDocument { Paths = new OpenApiPaths() };
        var deprecated = AddOperation(
            document, "/api/v1/legacy", HttpMethod.Get,
            description: Note("https://example.com/migrate-legacy"),
            tags: ["Legacy"],
            marked: true,
            replacedBy: "https://example.com/migrate-legacy"
        );

        Apply(document);

        Assert.That(deprecated.Description,
            Does.Contain("[`https://example.com/migrate-legacy`](https://example.com/migrate-legacy)"));
    }

    [Test]
    public void Apply_WhenReplacementPathIsMissing_LeavesPlainText()
    {
        var document = new OpenApiDocument { Paths = new OpenApiPaths() };
        var deprecated = AddOperation(
            document, "/api/v1/legacy", HttpMethod.Get,
            description: Note("/api/v9/missing"),
            tags: ["Legacy"],
            marked: true,
            replacedBy: "/api/v9/missing"
        );

        Apply(document);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deprecated.Description, Does.Contain("Replacement: `/api/v9/missing`."));
            Assert.That(deprecated.Description, Does.Not.Contain("]("));
        }
    }

    [Test]
    public void Apply_WhenReplacementHasNoTags_UsesDefaultTag()
    {
        var document = new OpenApiDocument { Paths = new OpenApiPaths() };
        var deprecated = AddOperation(
            document, "/api/v1/legacy", HttpMethod.Get,
            description: Note("/api/v2/legacy"),
            tags: ["Legacy"],
            marked: true,
            replacedBy: "/api/v2/legacy"
        );
        AddOperation(document, "/api/v2/legacy", HttpMethod.Get, tags: null);

        Apply(document);

        Assert.That(deprecated.Description, Does.Contain("](#/default/get_api_v2_legacy)"));
    }

    [Test]
    public void Apply_WhenReplacementHasSlashFreeTag_PrefersItForTheAnchor()
    {
        var document = new OpenApiDocument { Paths = new OpenApiPaths() };
        var deprecated = AddOperation(
            document, "/api/v1/legacy", HttpMethod.Get,
            description: Note("/api/v2/legacy"),
            tags: ["Legacy"],
            marked: true,
            replacedBy: "/api/v2/legacy"
        );
        AddOperation(
            document, "/api/v2/legacy", HttpMethod.Get,
            tags: ["Deprecated / EOL", "Legacy"],
            operationId: "getLegacyV2"
        );

        Apply(document);

        Assert.That(deprecated.Description, Does.Contain("](#/Legacy/getLegacyV2)"));
    }

    [Test]
    public void Apply_WhenCalledTwice_DoesNotDuplicateLink()
    {
        var document = new OpenApiDocument { Paths = new OpenApiPaths() };
        var deprecated = AddOperation(
            document, "/api/v1/books/{id}", HttpMethod.Delete,
            description: Note("/api/v2/books/{id}"),
            tags: ["Books"],
            marked: true,
            replacedBy: "/api/v2/books/{id}"
        );
        AddOperation(document, "/api/v2/books/{id}", HttpMethod.Delete, tags: ["Books"]);

        Apply(document);
        Apply(document);

        var occurrences = deprecated.Description!.Split("#/Books/delete_api_v2_books__id_").Length - 1;

        Assert.That(occurrences, Is.EqualTo(1));
    }

    private static void Apply(OpenApiDocument document) =>
        new EndOfLifeDocumentFilter().Apply(document, CreateContext());

    private static string Note(string replacedBy) =>
        $"**Deprecated.** Planned removal (sunset): **2027-01-01**. Replacement: `{replacedBy}`.";

    private static OpenApiOperation AddOperation(
        OpenApiDocument document,
        string path,
        HttpMethod method,
        string? description = null,
        string[]? tags = null,
        bool marked = false,
        string? replacedBy = null,
        string? operationId = null
    )
    {
        if (!document.Paths.TryGetValue(path, out var pathItem))
        {
            pathItem = new OpenApiPathItem
            {
                Operations = new Dictionary<HttpMethod, OpenApiOperation>()
            };
            document.Paths[path] = pathItem;
        }

        var operation = new OpenApiOperation
        {
            Description = description,
            Tags = tags is null
                ? null
                : new HashSet<OpenApiTagReference>(tags.Select(name => new OpenApiTagReference(name, document))),
            OperationId = operationId,
            Extensions = new Dictionary<string, IOpenApiExtension>()
        };

        if (marked)
        {
            // What EndOfLifeOperationFilter leaves behind for a marked operation.
            operation.Extensions["x-sunset"] = new JsonNodeExtension(JsonValue.Create("2027-01-01"));
        }

        if (replacedBy is not null)
        {
            operation.Extensions["x-eol-replaced-by"] = new JsonNodeExtension(JsonValue.Create(replacedBy));
        }

        pathItem.Operations![method] = operation;

        return operation;
    }

    /// <summary>
    /// The filter never reads the schema generator or descriptions, so empty values are enough.
    /// </summary>
    private static DocumentFilterContext CreateContext() =>
        new([], null, new SchemaRepository());
}
