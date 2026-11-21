using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.Time.Testing;

namespace Sstv.EndpointEol.Tests;

/// <summary>
/// Tests for the diagnostics report built by <see cref="DiagnosticsEndpointExtensions"/>.
/// </summary>
[TestFixture]
public sealed class DiagnosticsReportTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 0, 0, 0, TimeSpan.Zero);

    [TestCase(EnforcementMode.Off, ExpectedResult = "off")]
    [TestCase(EnforcementMode.WarnOnly, ExpectedResult = "warnOnly")]
    [TestCase(EnforcementMode.Enforce, ExpectedResult = "enforce")]
    [TestCase(EnforcementMode.MetricsOnly, ExpectedResult = "metricsOnly")]
    public string CreateReport_SerializesEnforcementModeAsCamelCaseString(EnforcementMode mode)
    {
        var report = CreateReport(new EndOfLifeOptions { EnforcementMode = mode }, endpoint: null);

        var json = System.Text.Json.JsonSerializer.Serialize(report, ReportJsonOptions);

        return System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("enforcementMode").GetString()!;
    }

    [Test]
    public void CreateReport_IncludesServerTimeAndItems()
    {
        var endpoint = CreateEndpoint("/api/v1/books/{id}", new EndOfLifeAttribute("2027-01-01") { WarnAfter = "2026-10-23" });

        var report = CreateReport(new EndOfLifeOptions(), endpoint);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.ServerTime, Is.EqualTo(Now));
            Assert.That(report.EnforcementMode, Is.EqualTo(EnforcementMode.WarnOnly));
            Assert.That(report.Items, Has.Count.EqualTo(1));
            Assert.That(report.Items[0].Path, Is.EqualTo("/api/v1/books/{id}"));
            Assert.That(report.Items[0].Phase, Is.EqualTo(EndOfLifePhase.Active));
            Assert.That(report.Items[0].DaysToBlock, Is.EqualTo(132));
        }
    }

    [Test]
    public void CreateReport_SerializesPhaseAsCamelCaseString()
    {
        var endpoint = CreateEndpoint("/api/v1/old", new EndOfLifeAttribute("2027-01-01"));

        var report = CreateReport(new EndOfLifeOptions(), endpoint);

        var json = System.Text.Json.JsonSerializer.Serialize(report, ReportJsonOptions);

        Assert.That(json, Does.Contain("\"phase\":\"active\""));
    }

    /// <summary>
    /// Mirrors the serializer settings owned by the diagnostics endpoint.
    /// </summary>
    private static readonly System.Text.Json.JsonSerializerOptions ReportJsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase) }
    };

    private static EndpointEolReport CreateReport(EndOfLifeOptions options, RouteEndpoint? endpoint)
    {
        var dataSources = endpoint is null
            ? Array.Empty<EndpointDataSource>()
            : [new FakeEndpointDataSource(endpoint)];

        var registry = new EndpointEolRegistry(dataSources, new FakeTimeProvider(Now));

        return DiagnosticsEndpointExtensions.CreateReport(registry, new FakeTimeProvider(Now), options);
    }

    private static RouteEndpoint CreateEndpoint(string pattern, params object[]? metadata) =>
        new(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(pattern),
            order: 0,
            new EndpointMetadataCollection(metadata ?? []),
            displayName: $"test:{pattern}"
        );

    private sealed class FakeEndpointDataSource(params Endpoint[] endpoints) : EndpointDataSource
    {
        public override IReadOnlyList<Endpoint> Endpoints => endpoints;

        public override IChangeToken GetChangeToken() => new CancellationChangeToken(CancellationToken.None);
    }
}