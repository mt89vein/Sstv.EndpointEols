using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.Time.Testing;

namespace Sstv.EndpointEol.Tests;

/// <summary>
/// Tests for <see cref="EndpointEolRegistry"/> snapshot building and ordering.
/// </summary>
[TestFixture]
public sealed class EndpointEolRegistryTests
{
    private static readonly string[] PostMethod = ["POST"];
    private static readonly string[] GetMethod = ["GET"];

    [Test]
    public void GetSnapshot_ReturnsOnlyMarkedEndpoints_SortedByBlockDateAscending()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.Zero));
        var first = CreateEndpoint("/api/a", new EndOfLifeAttribute("2027-03-01") { WarnAfter = "2026-12-01" });
        var second = CreateEndpoint("/api/b", new EndOfLifeAttribute("2027-01-01"));
        var third = CreateEndpoint("/api/c", new EndOfLifeAttribute("2027-02-01"));
        var unmarked = CreateEndpoint("/api/d", metadata: null);
        var registry = new EndpointEolRegistry(
            [new FakeEndpointDataSource(first, second, third, unmarked)],
            timeProvider
        );

        var snapshot = registry.GetSnapshot();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot, Has.Count.EqualTo(3));
            Assert.That(snapshot.Select(x => x.Path), Is.EqualTo(["/api/b", "/api/c", "/api/a"]));
        }
    }

    [Test]
    public void GetSnapshot_FillsAllFieldsFromPolicy()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.Zero));
        var endpoint = CreateEndpoint(
            "/api/books/{id}",
            new EndOfLifeAttribute("2027-01-01") { WarnAfter = "2026-10-23", ReplacedBy = "DELETE /api/v2/books" },
            new HttpMethodMetadata(PostMethod)
        );
        var registry = new EndpointEolRegistry([new FakeEndpointDataSource(endpoint)], timeProvider);

        var info = registry.GetSnapshot().Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(info.Method, Is.EqualTo("POST"));
            Assert.That(info.Path, Is.EqualTo("/api/books/{id}"));
            Assert.That(info.WarnAfter, Is.EqualTo(new DateOnly(2026, 10, 23)));
            Assert.That(info.BlockAfter, Is.EqualTo(new DateOnly(2027, 1, 1)));
            Assert.That(info.ReplacedBy, Is.EqualTo("DELETE /api/v2/books"));
            Assert.That(info.Phase, Is.EqualTo(EndOfLifePhase.Active));
            Assert.That(info.DaysToBlock, Is.EqualTo(132));
        }
    }

    [Test]
    public void GetSnapshot_WhenBlockDateReached_ReportsBlockedPhaseWithNegativeDays()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2027, 2, 10, 0, 0, 0, TimeSpan.Zero));
        var endpoint = CreateEndpoint("/api/old", new EndOfLifeAttribute("2027-01-01"), new HttpMethodMetadata(GetMethod));
        var registry = new EndpointEolRegistry([new FakeEndpointDataSource(endpoint)], timeProvider);

        var info = registry.GetSnapshot().Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(info.Phase, Is.EqualTo(EndOfLifePhase.Blocked));
            Assert.That(info.DaysToBlock, Is.LessThan(0));
        }
    }

    [Test]
    public void GetSnapshot_WhenCachedRecomputesPhaseAndDaysAgainstCurrentTime()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 12, 20, 0, 0, 0, TimeSpan.Zero));
        var endpoint = CreateEndpoint(
            "/api/old",
            new EndOfLifeAttribute("2027-01-01") { WarnAfter = "2026-10-23" },
            new HttpMethodMetadata(GetMethod)
        );
        var registry = new EndpointEolRegistry([new FakeEndpointDataSource(endpoint)], timeProvider);

        var before = registry.GetSnapshot().Single();

        // Endpoint set does not change, so the cached list stays in place.
        timeProvider.Advance(TimeSpan.FromDays(13));

        var after = registry.GetSnapshot().Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(before.Phase, Is.EqualTo(EndOfLifePhase.Warning));
            Assert.That(before.DaysToBlock, Is.EqualTo(12));
            Assert.That(after.Phase, Is.EqualTo(EndOfLifePhase.Blocked));
            Assert.That(after.DaysToBlock, Is.EqualTo(-1));
            Assert.That(after.BlockAfter, Is.EqualTo(before.BlockAfter));
            Assert.That(after.Path, Is.EqualTo(before.Path));
        }
    }

    [Test]
    public void GetSnapshot_WhenNothingChanged_ReturnsEquivalentItems()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.Zero));
        var endpoint = CreateEndpoint("/api/a", new EndOfLifeAttribute("2027-01-01"));
        var registry = new EndpointEolRegistry([new FakeEndpointDataSource(endpoint)], timeProvider);

        var first = registry.GetSnapshot().Single();
        var second = registry.GetSnapshot().Single();

        Assert.That(second, Is.EqualTo(first));
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
