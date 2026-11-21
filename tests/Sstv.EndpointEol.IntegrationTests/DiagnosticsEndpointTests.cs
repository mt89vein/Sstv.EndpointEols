using System.Net;
using System.Text.Json;

namespace Sstv.EndpointEol.IntegrationTests;

/// <summary>
/// Checks the diagnostics endpoint served by the sample application.
/// </summary>
[TestFixture]
public sealed class DiagnosticsEndpointTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task GetDiagnostics_ReturnsAllMarkedEndpointsSortedByBlockDate()
    {
        using var factory = new SampleAppFactory(Now, EnforcementMode.Enforce);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/diagnostics/end-of-life");
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        var items = root.GetProperty("items").EnumerateArray().ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(root.GetProperty("enforcementMode").GetString(), Is.EqualTo("enforce"));
            Assert.That(root.GetProperty("serverTime").GetDateTimeOffset(), Is.EqualTo(Now));
            Assert.That(items, Has.Length.EqualTo(4));
            Assert.That(items.Select(static x => x.GetProperty("blockAfter").GetString()),
                Is.EqualTo(["2026-01-01", "2026-01-01", "2027-01-01", "2099-01-01"]));
        }
    }

    [Test]
    public async Task GetDiagnostics_ReportsCurrentPhaseAndDaysToBlock()
    {
        using var factory = new SampleAppFactory(Now, EnforcementMode.Enforce);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/diagnostics/end-of-life");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToArray();

        var books = items.Single(static x => x.GetProperty("path").GetString() == "/api/v1/books/{id:guid}");
        var legacy = items.First(static x => x.GetProperty("path").GetString() == "/api/v1/legacy/status");
        var reports = items.Single(static x => x.GetProperty("path").GetString() == "/api/v1/reports/export");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(books.GetProperty("phase").GetString(), Is.EqualTo("warning"));
            Assert.That(books.GetProperty("daysToBlock").GetInt32(), Is.EqualTo(214));
            Assert.That(books.GetProperty("replacedBy").GetString(), Is.EqualTo("/api/v2/books/{id}"));
            Assert.That(legacy.GetProperty("phase").GetString(), Is.EqualTo("blocked"));
            Assert.That(reports.GetProperty("phase").GetString(), Is.EqualTo("active"));
        }
    }
}
