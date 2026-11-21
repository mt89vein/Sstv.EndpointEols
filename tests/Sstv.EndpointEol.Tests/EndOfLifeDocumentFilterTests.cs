using Microsoft.OpenApi;
using Sstv.EndpointEol.Swashbuckle;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Text.Json.Nodes;

namespace Sstv.EndpointEol.Tests;

/// <summary>
/// Tests for <see cref="EndOfLifeDocumentFilter"/>: every marked operation ends up in a single
/// dedicated tag placed first in the document.
/// </summary>
[TestFixture]
public sealed class EndOfLifeDocumentFilterTests
{
    [Test]
    public void Apply_WhenOperationIsMarked_AddsEndOfLifeTagFirst()
    {
        var document = CreateDocument(
            markedOperations: 1,
            existingTags: [new OpenApiTag { Name = "Account" }]
        );

        new EndOfLifeDocumentFilter().Apply(document, CreateContext());

        var operation = MarkedOperation(document);
        var names = operation.Tags!.Select(static x => x.Name).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(names, Does.Contain(EndOfLifeDocumentFilter.TAG_NAME));
            // Original grouping must survive, the EOL tag is additive.
            Assert.That(names, Does.Contain("Account"));
            Assert.That(document.Tags!.Select(static x => x.Name).First(),
                Is.EqualTo(EndOfLifeDocumentFilter.TAG_NAME));
        }
    }

    [Test]
    public void Apply_WhenDocumentTagAlreadyExists_MovesItFirstWithoutDuplicating()
    {
        var document = CreateDocument(markedOperations: 1, existingTags: []);
        document.Tags = new HashSet<OpenApiTag>
        {
            new() { Name = "Account" },
            new() { Name = EndOfLifeDocumentFilter.TAG_NAME, Description = "stale" }
        };

        new EndOfLifeDocumentFilter().Apply(document, CreateContext());

        var tags = document.Tags!.Select(static x => x.Name).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(tags, Is.EqualTo([EndOfLifeDocumentFilter.TAG_NAME, "Account"]));
            Assert.That(tags.Count(static x => x == EndOfLifeDocumentFilter.TAG_NAME), Is.EqualTo(1));
            Assert.That(document.Tags!.First().Description, Is.Not.EqualTo("stale"));
        }
    }

    [Test]
    public void Apply_WhenCalledTwice_DoesNotDuplicateTagOnOperation()
    {
        var document = CreateDocument(markedOperations: 1, existingTags: []);
        var filter = new EndOfLifeDocumentFilter();

        filter.Apply(document, CreateContext());
        filter.Apply(document, CreateContext());

        var names = MarkedOperation(document).Tags!.Select(static x => x.Name).ToArray();

        Assert.That(names.Count(static x => x == EndOfLifeDocumentFilter.TAG_NAME), Is.EqualTo(1));
    }

    [Test]
    public void Apply_WhenNothingIsMarked_LeavesDocumentUntouched()
    {
        var document = CreateDocument(markedOperations: 0, existingTags: [new OpenApiTag { Name = "Account" }]);

        new EndOfLifeDocumentFilter().Apply(document, CreateContext());

        var plainTags = document.Paths["/api/v1/plain"]!.Operations![HttpMethod.Get].Tags!
            .Select(static x => x.Name);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(document.Tags!.Select(static x => x.Name), Is.EqualTo(["Account"]));
            Assert.That(plainTags, Is.EqualTo(new[] { "Account" }));
        }
    }

    [Test]
    public void Apply_WhenOperationHasNoTags_StillGetsEndOfLifeTag()
    {
        var document = CreateDocument(markedOperations: 1, existingTags: [], withTags: false);

        new EndOfLifeDocumentFilter().Apply(document, CreateContext());

        Assert.That(MarkedOperation(document).Tags!.Select(static x => x.Name),
            Is.EqualTo([EndOfLifeDocumentFilter.TAG_NAME]));
    }

    private static OpenApiDocument CreateDocument(int markedOperations, IList<OpenApiTag> existingTags, bool withTags = true)
    {
        var document = new OpenApiDocument
        {
            Paths = new OpenApiPaths(),
            Tags = new HashSet<OpenApiTag>(existingTags)
        };

        for (var i = 0; i < markedOperations; i++)
        {
            document.Paths[$"/api/v1/marked-{i}"] = CreatePathItem(document, marked: true, withTags);
        }

        document.Paths["/api/v1/plain"] = CreatePathItem(document, marked: false, withTags);

        return document;
    }

    private static OpenApiPathItem CreatePathItem(OpenApiDocument document, bool marked, bool withTags) =>
        new()
        {
            Operations = new Dictionary<HttpMethod, OpenApiOperation>
            {
                [HttpMethod.Get] = CreateOperation(document, marked, withTags)
            }
        };

    private static OpenApiOperation CreateOperation(OpenApiDocument document, bool marked, bool withTags)
    {
        var operation = new OpenApiOperation
        {
            Tags = withTags
                ? new HashSet<OpenApiTagReference> { new("Account", document) }
                : null,
            Extensions = new Dictionary<string, IOpenApiExtension>()
        };

        if (marked)
        {
            // What EndOfLifeOperationFilter leaves behind for a marked operation.
            operation.Extensions["x-sunset"] = new JsonNodeExtension(JsonValue.Create("2027-01-01"));
        }

        return operation;
    }

    /// <summary>
    /// The operation of <c>/api/v1/marked-0</c>, i.e. one carrying the sunset extension.
    /// </summary>
    private static OpenApiOperation MarkedOperation(OpenApiDocument document) =>
        document.Paths["/api/v1/marked-0"]!.Operations![HttpMethod.Get];

    /// <summary>
    /// The filter never reads the schema generator or the descriptions, so empty values are enough.
    /// </summary>
    private static DocumentFilterContext CreateContext() =>
        new([], null, new SchemaRepository());
}
