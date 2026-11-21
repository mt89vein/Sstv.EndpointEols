using System.Net;
using System.Text.Json;

namespace Sstv.EndpointEol.IntegrationTests;

/// <summary>
/// Verifies that the Swashbuckle integration from <c>Sstv.EndpointEol.Swashbuckle</c>
/// annotates operations of the sample application.
/// </summary>
[TestFixture]
public sealed class SwaggerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task SwaggerDocument_MarksControllerActionAsDeprecatedWithExtensions()
    {
        var root = await GetSwaggerAsync();
        var operation = root.GetProperty("paths").GetProperty("/api/v1/books/{id}").GetProperty("delete");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(operation.GetProperty("deprecated").GetBoolean(), Is.True);
            Assert.That(operation.GetProperty("x-sunset").GetString(), Is.EqualTo("2027-01-01"));
            Assert.That(operation.GetProperty("x-eol-warn-after").GetString(), Is.EqualTo("2026-01-01"));
            Assert.That(operation.GetProperty("x-eol-replaced-by").GetString(), Is.EqualTo("/api/v2/books/{id}"));
            Assert.That(operation.GetProperty("description").GetString(), Does.Contain("Deprecated"));
        }
    }

    [Test]
    public async Task SwaggerDocument_LinksReplacementOperationAsSwaggerUiDeepLink()
    {
        var root = await GetSwaggerAsync();
        var description = root.GetProperty("paths").GetProperty("/api/v1/books/{id}")
            .GetProperty("delete").GetProperty("description").GetString();
        var replacement = root.GetProperty("paths").GetProperty("/api/v2/books/{id}").GetProperty("delete");

        using (Assert.EnterMultipleScope())
        {
            // The link jumps to the v2 operation inside Swagger UI.
            Assert.That(description, Does.Contain("[`/api/v2/books/{id}`](#/Books/delete_api_v2_books__id_)"));
            // The deep link needs an operationId, so one is generated for the successor.
            Assert.That(replacement.GetProperty("operationId").GetString(),
                Is.EqualTo("delete_api_v2_books__id_"));
        }
    }

    [Test]
    public async Task SwaggerDocument_LinksExternalReplacementAsUrl()
    {
        var root = await GetSwaggerAsync();
        var description = root.GetProperty("paths").GetProperty("/api/v1/legacy/status")
            .GetProperty("get").GetProperty("description").GetString();

        Assert.That(description, Does.Contain("](https://example.com/migrate-legacy)"));
    }

    [Test]
    public async Task SwaggerDocument_MarksMinimalApiEndpointAsDeprecated()
    {
        var root = await GetSwaggerAsync();
        var operation = root.GetProperty("paths").GetProperty("/api/v1/legacy/ping").GetProperty("get");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(operation.GetProperty("deprecated").GetBoolean(), Is.True);
            Assert.That(operation.GetProperty("x-sunset").GetString(), Is.EqualTo("2026-01-01"));
        }
    }

    [Test]
    public async Task SwaggerDocument_PutsDeprecatedTagFirst()
    {
        var root = await GetSwaggerAsync();
        var tags = root.GetProperty("tags").EnumerateArray().ToArray();

        Assert.That(tags[0].GetProperty("name").GetString(), Is.EqualTo("Deprecated / EOL"));
    }

    private static async Task<JsonElement> GetSwaggerAsync()
    {
        using var factory = new SampleAppFactory(Now);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return json.RootElement.Clone();
    }
}
