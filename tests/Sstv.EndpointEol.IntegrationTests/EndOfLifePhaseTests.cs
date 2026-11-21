using System.Net;
using System.Text.Json;

namespace Sstv.EndpointEol.IntegrationTests;

/// <summary>
/// End-to-end checks of the three lifecycle phases through the sample application:
/// headers, deprecation announcement and <c>410 Gone</c> blocking.
/// </summary>
[TestFixture]
public sealed class EndOfLifePhaseTests
{
    private static readonly Guid BookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset ActiveDay = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WarningDay = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset BlockedDay = new(2027, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task UnmarkedEndpoint_ReceivesNoEndOfLifeHeaders()
    {
        using var factory = new SampleAppFactory(WarningDay);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/books");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Headers.Contains("Sunset"), Is.False);
            Assert.That(response.Headers.Contains("Deprecation"), Is.False);
            Assert.That(response.Headers.Contains("Link"), Is.False);
        }
    }

    [Test]
    public async Task MarkedEndpoint_DuringActivePhase_AnnouncesSunsetOnly()
    {
        using var factory = new SampleAppFactory(ActiveDay, EnforcementMode.Enforce);
        using var client = factory.CreateClient();

        using var response = await client.DeleteAsync($"/api/v1/books/{BookId}");
        var links = string.Join(", ", response.Headers.GetValues("Link"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(response.Headers.GetValues("Sunset").Single(), Is.EqualTo("Fri, 01 Jan 2027 00:00:00 GMT"));
            Assert.That(response.Headers.Contains("Deprecation"), Is.False);
            Assert.That(links, Does.Contain("rel=\"sunset\""));
            Assert.That(links, Does.Contain("rel=\"successor-version\""));
            Assert.That(links, Does.Contain("</api/v2/books/{id}>"));
        }
    }

    [Test]
    public async Task MarkedEndpoint_DuringWarningPhase_AnnouncesDeprecation()
    {
        using var factory = new SampleAppFactory(WarningDay, EnforcementMode.Enforce);
        using var client = factory.CreateClient();

        using var response = await client.DeleteAsync($"/api/v1/books/{BookId}");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(response.Headers.GetValues("Sunset").Single(), Is.EqualTo("Fri, 01 Jan 2027 00:00:00 GMT"));
            Assert.That(response.Headers.GetValues("Deprecation").Single(), Is.EqualTo("@1767225600"));
        }
    }

    [Test]
    public async Task MarkedEndpoint_PastBlockDateInEnforceMode_ReturnsGoneProblemDetails()
    {
        using var factory = new SampleAppFactory(BlockedDay, EnforcementMode.Enforce);
        using var client = factory.CreateClient();

        using var response = await client.DeleteAsync($"/api/v1/books/{BookId}");
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Gone));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
            Assert.That(response.Headers.GetValues("Cache-Control").Single(), Is.EqualTo("no-store"));
            // HttpClient normalizes Location, so the route placeholder arrives percent-encoded.
            Assert.That(response.Headers.GetValues("Location").Single(),
                Is.EqualTo("/api/v2/books/%7Bid%7D"));
            Assert.That(root.GetProperty("status").GetInt32(), Is.EqualTo(410));
            Assert.That(root.GetProperty("sunset").GetString(), Is.EqualTo("2027-01-01"));
            Assert.That(root.GetProperty("replacement").GetString(), Is.EqualTo("/api/v2/books/{id}"));
            Assert.That(root.GetProperty("detail").GetString(), Does.Contain("2027-01-01"));
        }
    }

    [Test]
    public async Task MarkedEndpoint_PastBlockDateInWarnOnlyMode_IsStillServed()
    {
        using var factory = new SampleAppFactory(BlockedDay, EnforcementMode.WarnOnly);
        using var client = factory.CreateClient();

        using var response = await client.DeleteAsync($"/api/v1/books/{BookId}");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(response.Headers.GetValues("Deprecation").Single(), Is.EqualTo("@1767225600"));
        }
    }

    [Test]
    public async Task ControllerLevelAttribute_BlocksEveryActionOfTheController()
    {
        using var factory = new SampleAppFactory(WarningDay, EnforcementMode.Enforce);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/legacy/status");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Gone));
    }

    [Test]
    public async Task MinimalApiEndpoint_PastBlockDate_ReturnsGone()
    {
        using var factory = new SampleAppFactory(BlockedDay, EnforcementMode.Enforce);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/legacy/ping");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Gone));
    }

    [Test]
    public async Task ActiveEndpoint_FarFromBlockDate_HasNoDeprecationHeader()
    {
        using var factory = new SampleAppFactory(WarningDay, EnforcementMode.Enforce);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/v1/reports/export", content: null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
            Assert.That(response.Headers.GetValues("Sunset").Single(), Is.EqualTo("Thu, 01 Jan 2099 00:00:00 GMT"));
            Assert.That(response.Headers.Contains("Deprecation"), Is.False);
        }
    }
}
