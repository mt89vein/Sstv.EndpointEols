using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using System.Security.Claims;

namespace Sstv.EndpointEol.Tests;

/// <summary>
/// Tests for <see cref="EndOfLifeMiddleware"/> behavior: headers, phases, metrics and blocking.
/// </summary>
[TestFixture]
public sealed class EndOfLifeMiddlewareTests
{
    private static readonly DateTimeOffset WarnDay = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset BlockedDay = new(2027, 2, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Shared between tests so throttling windows do not leak, cleared before every test.
    /// </summary>
    private static readonly MemoryCache ThrottleCache = new(new MemoryCacheOptions());

    private Harness _harness = null!;

    [SetUp]
    public void SetUp() => ThrottleCache.Compact(1.0);

    [TearDown]
    public void TearDown()
    {
        _harness?.Dispose();
        _harness = null!;
    }

    [Test]
    public async Task InvokeAsync_WhenEndpointHasNoAttribute_JustCallsNext()
    {
        var context = CreateContext(CreateEndpoint(metadata: null));
        var harness = CreateMiddleware(
            new EndOfLifeOptions { EnforcementMode = EnforcementMode.Enforce },
            new FakeTimeProvider(WarnDay)
        );

        await harness.Middleware.InvokeAsync(context);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(harness.Next.Called, Is.True);
            Assert.That(context.Response.Headers.ContainsKey("Sunset"), Is.False);
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
            Assert.That(harness.Metrics.Measurements, Is.Empty);
        }
    }

    [Test]
    public async Task InvokeAsync_WhenModeIsOff_JustCallsNext()
    {
        var context = CreateContext(CreateEndpoint(new EndOfLifeAttribute("2027-01-01")));
        var harness = CreateMiddleware(
            new EndOfLifeOptions { EnforcementMode = EnforcementMode.Off },
            new FakeTimeProvider(BlockedDay)
        );

        await harness.Middleware.InvokeAsync(context);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(harness.Next.Called, Is.True);
            Assert.That(context.Response.Headers.ContainsKey("Sunset"), Is.False);
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
            Assert.That(harness.Metrics.Measurements, Is.Empty);
        }
    }

    [Test]
    public async Task InvokeAsync_DuringWarningPhase_SetsSunsetDeprecationAndLinkHeaders_AndCallsNext()
    {
        var context = CreateContext(CreateEndpoint(new EndOfLifeAttribute("2027-01-01")
        {
            WarnAfter = "2026-10-23",
            ReplacedBy = "/api/v2/test"
        }));
        var harness = CreateMiddleware(new EndOfLifeOptions(), new FakeTimeProvider(WarnDay));

        await harness.Middleware.InvokeAsync(context);

        var expectedSunset = ToHttpDate(new DateOnly(2027, 1, 1));
        var expectedDeprecation = "@" + new DateTimeOffset(
                new DateOnly(2026, 10, 23).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            ).ToUnixTimeSeconds();
        var links = context.Response.Headers.Link.ToString();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(harness.Next.Called, Is.True);
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
            Assert.That(context.Response.Headers["Sunset"].ToString(), Is.EqualTo(expectedSunset));
            Assert.That(context.Response.Headers["Deprecation"].ToString(), Is.EqualTo(expectedDeprecation));
            Assert.That(links, Does.Contain("/api/v2/test"));
            Assert.That(links, Does.Contain("successor-version"));
        }
    }

    [Test]
    public async Task InvokeAsync_WhenWarnAfterNotSet_AnnouncesDeprecationAfterDefaultWindow()
    {
        // BlockAfter minus the 90-day default window is 2026-10-03.
        var context = CreateContext(CreateEndpoint(new EndOfLifeAttribute("2027-01-01")));
        var harness = CreateMiddleware(new EndOfLifeOptions(), new FakeTimeProvider(WarnDay));

        await harness.Middleware.InvokeAsync(context);

        var expectedDeprecation = "@" + new DateTimeOffset(
                new DateOnly(2026, 10, 3).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            ).ToUnixTimeSeconds();

        Assert.That(context.Response.Headers["Deprecation"].ToString(), Is.EqualTo(expectedDeprecation));
    }

    [Test]
    public async Task InvokeAsync_DuringActivePhase_SetsOnlySunsetAndDocsLinkHeaders()
    {
        var context = CreateContext(CreateEndpoint(new EndOfLifeAttribute("2027-01-01") { WarnAfter = "2026-10-23" }));
        var harness = CreateMiddleware(
            new EndOfLifeOptions { SunsetDocsUrl = new Uri("https://example.com/eol") },
            new FakeTimeProvider(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero))
        );

        await harness.Middleware.InvokeAsync(context);

        var links = context.Response.Headers.Link.ToString();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(harness.Next.Called, Is.True);
            Assert.That(context.Response.Headers.ContainsKey("Deprecation"), Is.False);
            Assert.That(links, Does.Contain("https://example.com/eol"));
            Assert.That(links, Does.Contain("rel=\"sunset\""));
            Assert.That(links, Does.Not.Contain("successor-version"));
        }
    }

    [Test]
    public async Task InvokeAsync_WhenBlockDateReachedInEnforceMode_Returns410ProblemDetails_AndSkipsNext()
    {
        var context = CreateContext(CreateEndpoint(new EndOfLifeAttribute("2027-01-01") { ReplacedBy = "/api/v2/test" }));
        var harness = CreateMiddleware(
            new EndOfLifeOptions { EnforcementMode = EnforcementMode.Enforce },
            new FakeTimeProvider(BlockedDay)
        );

        await harness.Middleware.InvokeAsync(context);

        var body = ReadBody(context);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(harness.Next.Called, Is.False);
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status410Gone));
            Assert.That(context.Response.ContentType, Is.EqualTo("application/problem+json"));
            Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
            Assert.That(context.Response.ContentLength, Is.EqualTo(System.Text.Encoding.UTF8.GetByteCount(body)));
            Assert.That(body, Does.Contain("\"status\":410"));
            Assert.That(body, Does.Contain("\"sunset\":\"2027-01-01\""));
            Assert.That(body, Does.Contain("/api/v2/test"));
        }
    }

    [Test]
    public async Task InvokeAsync_WhenRequestIsHead_Returns410WithoutBody()
    {
        var context = CreateContext(CreateEndpoint(new EndOfLifeAttribute("2027-01-01")));
        context.Request.Method = HttpMethods.Head;
        var harness = CreateMiddleware(
            new EndOfLifeOptions { EnforcementMode = EnforcementMode.Enforce },
            new FakeTimeProvider(BlockedDay)
        );

        await harness.Middleware.InvokeAsync(context);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status410Gone));
            Assert.That(context.Response.ContentLength, Is.GreaterThan(0));
            Assert.That(ReadBody(context), Is.Empty);
        }
    }

    [Test]
    public async Task InvokeAsync_WhenGoneProblemDetailsFactoryConfigured_UsesIt()
    {
        var context = CreateContext(CreateEndpoint(new EndOfLifeAttribute("2027-01-01")));
        var harness = CreateMiddleware(
            new EndOfLifeOptions
            {
                EnforcementMode = EnforcementMode.Enforce,
                GoneProblemDetailsFactory = (_, policy) => new GoneProblemDetails
                {
                    Type = "https://errors.example.com/eol",
                    Title = "Endpoint retired",
                    Status = StatusCodes.Status410Gone,
                    Detail = $"Retired on {policy.BlockAfter:yyyy-MM-dd}",
                    Sunset = policy.BlockAfter.ToString("yyyy-MM-dd"),
                    Extensions = new Dictionary<string, object?> { ["code"] = "ENDPOINT_EOL" }
                }
            },
            new FakeTimeProvider(BlockedDay)
        );

        await harness.Middleware.InvokeAsync(context);

        var body = ReadBody(context);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(body, Does.Contain("https://errors.example.com/eol"));
            Assert.That(body, Does.Contain("\"code\":\"ENDPOINT_EOL\""));
        }
    }

    [Test]
    public async Task InvokeAsync_WhenBlockDateReachedInWarnOnlyMode_CallsNext()
    {
        var context = CreateContext(CreateEndpoint(new EndOfLifeAttribute("2027-01-01")));
        var harness = CreateMiddleware(
            new EndOfLifeOptions { EnforcementMode = EnforcementMode.WarnOnly },
            new FakeTimeProvider(BlockedDay)
        );

        await harness.Middleware.InvokeAsync(context);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(harness.Next.Called, Is.True);
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        }
    }

    [Test]
    public async Task InvokeAsync_MethodLevelAttributeOverridesControllerLevelOne()
    {
        var controllerAttribute = new EndOfLifeAttribute("2027-01-01") { WarnAfter = "2026-10-23" };
        var actionAttribute = new EndOfLifeAttribute("2027-03-01") { WarnAfter = "2027-02-01" };
        var context = CreateContext(CreateEndpoint(controllerAttribute, actionAttribute));
        var harness = CreateMiddleware(
            new EndOfLifeOptions { EnforcementMode = EnforcementMode.Enforce },
            new FakeTimeProvider(new DateTimeOffset(2027, 1, 15, 0, 0, 0, TimeSpan.Zero))
        );

        await harness.Middleware.InvokeAsync(context);

        // Controller dates would block the request on this date, action dates keep it active.
        using (Assert.EnterMultipleScope())
        {
            Assert.That(harness.Next.Called, Is.True);
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
            Assert.That(context.Response.Headers["Sunset"].ToString(), Is.EqualTo(ToHttpDate(new DateOnly(2027, 3, 1))));
            Assert.That(context.Response.Headers.ContainsKey("Deprecation"), Is.False);
        }
    }

    [Test]
    public async Task InvokeAsync_DuringActivePhase_RecordsMetricWithActivePhase()
    {
        var context = CreateContext(CreateEndpoint(
            new EndOfLifeAttribute("2027-01-01"),
            new HttpMethodMetadata(["GET"])
        ));
        var harness = CreateMiddleware(new EndOfLifeOptions(), new FakeTimeProvider(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)));

        await harness.Middleware.InvokeAsync(context);

        var measurement = harness.Metrics.MeasurementsOf(MetricsCollector.RequestsInstrument).Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Value, Is.EqualTo(1));
            Assert.That(measurement.Tag("phase"), Is.EqualTo("active"));
            Assert.That(measurement.Tag("route"), Is.EqualTo("/api/v1/test"));
            Assert.That(measurement.Tag("method"), Is.EqualTo("get"));
        }
    }

    [Test]
    public async Task InvokeAsync_DuringWarningPhase_RecordsMetricWithWarningPhase()
    {
        var context = CreateContext(CreateEndpoint(new EndOfLifeAttribute("2027-01-01") { WarnAfter = "2026-10-23" }));
        var harness = CreateMiddleware(new EndOfLifeOptions(), new FakeTimeProvider(WarnDay));

        await harness.Middleware.InvokeAsync(context);

        Assert.That(harness.Metrics.MeasurementsOf(MetricsCollector.RequestsInstrument).Single().Tag("phase"),
            Is.EqualTo("warning"));
    }

    [Test]
    public async Task InvokeAsync_WhenEndpointAcceptsSeveralMethods_RecordsOneMeasurementPerMethod()
    {
        var context = CreateContext(CreateEndpoint(
            new EndOfLifeAttribute("2027-01-01"),
            new HttpMethodMetadata(["GET", "POST"])
        ));
        var harness = CreateMiddleware(new EndOfLifeOptions(), new FakeTimeProvider(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)));

        await harness.Middleware.InvokeAsync(context);

        var methods = harness.Metrics.MeasurementsOf(MetricsCollector.RequestsInstrument)
            .Select(x => x.Tag("method"))
            .ToArray();

        Assert.That(methods, Is.EquivalentTo(["get", "post"]));
    }

    [Test]
    public async Task InvokeAsync_WhenMethodsAreUnknown_RecordsAnyMethod()
    {
        var context = CreateContext(CreateEndpoint(new EndOfLifeAttribute("2027-01-01")));
        var harness = CreateMiddleware(new EndOfLifeOptions(), new FakeTimeProvider(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)));

        await harness.Middleware.InvokeAsync(context);

        Assert.That(harness.Metrics.MeasurementsOf(MetricsCollector.RequestsInstrument).Single().Tag("method"),
            Is.EqualTo("any"));
    }

    [Test]
    public async Task InvokeAsync_WhenBlockDateReachedInWarnOnlyMode_RecordsMetricWithBlockedPhase()
    {
        var context = CreateContext(CreateEndpoint(new EndOfLifeAttribute("2027-01-01")));
        var harness = CreateMiddleware(
            new EndOfLifeOptions { EnforcementMode = EnforcementMode.WarnOnly },
            new FakeTimeProvider(BlockedDay)
        );

        await harness.Middleware.InvokeAsync(context);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(harness.Next.Called, Is.True);
            Assert.That(harness.Metrics.MeasurementsOf(MetricsCollector.RequestsInstrument).Single().Tag("phase"),
                Is.EqualTo("blocked"));
        }
    }

    [Test]
    public async Task InvokeAsync_WhenBlockDateReachedInEnforceMode_RecordsMetricWithBlockedPhase()
    {
        var context = CreateContext(CreateEndpoint(new EndOfLifeAttribute("2027-01-01")));
        var harness = CreateMiddleware(
            new EndOfLifeOptions { EnforcementMode = EnforcementMode.Enforce },
            new FakeTimeProvider(BlockedDay)
        );

        await harness.Middleware.InvokeAsync(context);

        Assert.That(harness.Metrics.MeasurementsOf(MetricsCollector.RequestsInstrument).Single().Tag("phase"),
            Is.EqualTo("blocked"));
    }

    [Test]
    public async Task InvokeAsync_WhenMetricsOnlyMode_RecordsMetricsButAddsNoHeadersAndNoLogs()
    {
        var context = CreateWarningContext("sub", "user-42");
        var harness = CreateMiddleware(
            new EndOfLifeOptions { EnforcementMode = EnforcementMode.MetricsOnly },
            new FakeTimeProvider(BlockedDay)
        );

        await harness.Middleware.InvokeAsync(context);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(harness.Next.Called, Is.True);
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
            Assert.That(context.Response.Headers.ContainsKey("Sunset"), Is.False);
            Assert.That(context.Response.Headers.ContainsKey("Deprecation"), Is.False);
            Assert.That(context.Response.Headers.ContainsKey("Link"), Is.False);
            Assert.That(harness.Logger.Messages, Is.Empty);
            Assert.That(harness.Metrics.MeasurementsOf(MetricsCollector.RequestsInstrument), Has.Length.EqualTo(1));
            Assert.That(harness.Metrics.MeasurementsOf(MetricsCollector.ConsumersInstrument), Has.Length.EqualTo(1));
        }
    }

    [Test]
    public async Task InvokeAsync_WhenMetricsOnlyMode_PastBlockDate_ServesRequestAndCountsBlockedPhase()
    {
        var context = CreateContext(CreateEndpoint(new EndOfLifeAttribute("2027-01-01")));
        var harness = CreateMiddleware(
            new EndOfLifeOptions { EnforcementMode = EnforcementMode.MetricsOnly },
            new FakeTimeProvider(BlockedDay)
        );

        await harness.Middleware.InvokeAsync(context);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(harness.Next.Called, Is.True);
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
            Assert.That(harness.Metrics.MeasurementsOf(MetricsCollector.RequestsInstrument).Single().Tag("phase"),
                Is.EqualTo("blocked"));
        }
    }

    [Test]
    public async Task InvokeAsync_WhenBlockedAndReplacementIsPath_SendsLocationHeader()
    {
        var context = CreateContext(CreateEndpoint(new EndOfLifeAttribute("2027-01-01") { ReplacedBy = "/api/v2/test" }));
        var harness = CreateMiddleware(
            new EndOfLifeOptions { EnforcementMode = EnforcementMode.Enforce },
            new FakeTimeProvider(BlockedDay)
        );

        await harness.Middleware.InvokeAsync(context);

        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo("/api/v2/test"));
    }

    [Test]
    public async Task InvokeAsync_WhenBlockedAndReplacementIsNotPath_OmitsLocationHeader()
    {
        var context = CreateContext(CreateEndpoint(new EndOfLifeAttribute("2027-01-01") { ReplacedBy = "Migration guide v2" }));
        var harness = CreateMiddleware(
            new EndOfLifeOptions { EnforcementMode = EnforcementMode.Enforce },
            new FakeTimeProvider(BlockedDay)
        );

        await harness.Middleware.InvokeAsync(context);

        Assert.That(context.Response.Headers.ContainsKey("Location"), Is.False);
    }

    [Test]
    public async Task InvokeAsync_WhenBlockedAndReplacementIsAbsoluteUrl_OmitsLocationHeader()
    {
        var context = CreateContext(CreateEndpoint(new EndOfLifeAttribute("2027-01-01")
        {
            ReplacedBy = "https://docs.example.com/migration"
        }));
        var harness = CreateMiddleware(
            new EndOfLifeOptions { EnforcementMode = EnforcementMode.Enforce },
            new FakeTimeProvider(BlockedDay)
        );

        await harness.Middleware.InvokeAsync(context);

        // Only a server-side path is a Location value; an external URL is a documentation link.
        Assert.That(context.Response.Headers.ContainsKey("Location"), Is.False);
    }

    [Test]
    public async Task InvokeAsync_WhenSameMethodRecordedRepeatedly_ReusesCachedMethodName()
    {
        var harness = CreateMiddleware(new EndOfLifeOptions(), new FakeTimeProvider(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)));

        await harness.Middleware.InvokeAsync(CreateContext(CreateEndpoint(
            new EndOfLifeAttribute("2027-01-01"),
            new HttpMethodMetadata(["GET"])
        )));
        await harness.Middleware.InvokeAsync(CreateContext(CreateEndpoint(
            new EndOfLifeAttribute("2027-01-01"),
            new HttpMethodMetadata(["GET"])
        )));

        var names = harness.Metrics.MeasurementsOf(MetricsCollector.RequestsInstrument)
            .Select(static x => x.Tag("method"))
            .ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(names, Is.EqualTo(["get", "get"]));
            // Same reference means the lowercase form was not re-allocated per request.
            Assert.That(names[0], Is.SameAs(names[1]));
        }
    }

    [Test]
    public async Task InvokeAsync_WhenEnforcementModeReloaded_AppliesWithoutRestart()
    {
        var context = CreateContext(CreateEndpoint(new EndOfLifeAttribute("2027-01-01")));
        var harness = CreateMiddleware(
            new EndOfLifeOptions { EnforcementMode = EnforcementMode.WarnOnly },
            new FakeTimeProvider(BlockedDay)
        );

        await harness.Middleware.InvokeAsync(context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));

        // Same middleware instance, new options instance: only an IOptionsMonitor can do this.
        harness.Options.Set(new EndOfLifeOptions { EnforcementMode = EnforcementMode.Enforce });

        var secondContext = CreateContext(CreateEndpoint(new EndOfLifeAttribute("2027-01-01")));
        await harness.Middleware.InvokeAsync(secondContext);

        Assert.That(secondContext.Response.StatusCode, Is.EqualTo(StatusCodes.Status410Gone));
    }

    [Test]
    public async Task InvokeAsync_LogsUserIdFromSubClaim()
    {
        var context = CreateWarningContext("sub", "user-42");
        var harness = CreateMiddleware(new EndOfLifeOptions { LogThrottleMinutes = 0 }, new FakeTimeProvider(WarnDay));

        await harness.Middleware.InvokeAsync(context);

        Assert.That(harness.Logger.Messages, Has.Exactly(1).Contains("user-42"));
    }

    [Test]
    public async Task InvokeAsync_LogsUserIdFromNameIdentifier_WhenSubClaimIsMapped()
    {
        // JwtBearer with MapInboundClaims=true rewrites "sub" into ClaimTypes.NameIdentifier.
        var context = CreateWarningContext(ClaimTypes.NameIdentifier, "user-42");
        var harness = CreateMiddleware(new EndOfLifeOptions { LogThrottleMinutes = 0 }, new FakeTimeProvider(WarnDay));

        await harness.Middleware.InvokeAsync(context);

        Assert.That(harness.Logger.Messages, Has.Exactly(1).Contains("user-42"));
    }

    [Test]
    public async Task InvokeAsync_LogsUserIdFromCustomClaimType()
    {
        var context = CreateWarningContext("client_id", "consuming-service");
        var harness = CreateMiddleware(
            new EndOfLifeOptions { LogThrottleMinutes = 0, UserIdClaimType = "client_id" },
            new FakeTimeProvider(WarnDay)
        );

        await harness.Middleware.InvokeAsync(context);

        Assert.That(harness.Logger.Messages, Has.Exactly(1).Contains("consuming-service"));
    }

    [Test]
    public async Task InvokeAsync_WhenSameConsumerCallsRepeatedly_ThrottlesLogButCountsEveryRequest()
    {
        var harness = CreateMiddleware(
            new EndOfLifeOptions { LogThrottleMinutes = 60 },
            new FakeTimeProvider(WarnDay)
        );

        await harness.Middleware.InvokeAsync(CreateWarningContext("sub", "user-42"));
        await harness.Middleware.InvokeAsync(CreateWarningContext("sub", "user-42"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(harness.Logger.Messages, Has.Count.EqualTo(1));
            Assert.That(harness.Metrics.MeasurementsOf(MetricsCollector.RequestsInstrument), Has.Length.EqualTo(2));
        }
    }

    [Test]
    public async Task InvokeAsync_WhenDifferentConsumersCall_LogsEachOfThem()
    {
        var harness = CreateMiddleware(
            new EndOfLifeOptions { LogThrottleMinutes = 60 },
            new FakeTimeProvider(WarnDay)
        );

        await harness.Middleware.InvokeAsync(CreateWarningContext("sub", "user-1"));
        await harness.Middleware.InvokeAsync(CreateWarningContext("sub", "user-2"));
        await harness.Middleware.InvokeAsync(CreateWarningContext("sub", "user-3"));

        Assert.That(harness.Logger.Messages, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task InvokeAsync_WhenConsumerCallsRepeatedly_CountsItOnceInConsumersMetric()
    {
        var harness = CreateMiddleware(new EndOfLifeOptions(), new FakeTimeProvider(WarnDay));

        await harness.Middleware.InvokeAsync(CreateWarningContext("sub", "user-1"));
        await harness.Middleware.InvokeAsync(CreateWarningContext("sub", "user-1"));
        await harness.Middleware.InvokeAsync(CreateWarningContext("sub", "user-2"));

        var consumers = harness.Metrics.MeasurementsOf(MetricsCollector.ConsumersInstrument);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(consumers.Sum(static x => x.Value), Is.EqualTo(2));
            Assert.That(consumers.Select(static x => x.Tag("route")), Is.All.EqualTo("/api/v1/test"));
        }
    }

    [Test]
    public async Task InvokeAsync_WhenCollectConsumersDisabled_DoesNotTrackConsumers()
    {
        var context = CreateWarningContext("sub", "user-1");
        var harness = CreateMiddleware(
            new EndOfLifeOptions { CollectConsumers = false },
            new FakeTimeProvider(WarnDay)
        );

        await harness.Middleware.InvokeAsync(context);

        Assert.That(harness.Metrics.MeasurementsOf(MetricsCollector.ConsumersInstrument), Is.Empty);
    }

    [Test]
    public async Task InvokeAsync_WhenBlockDateReachedInWarnOnlyMode_LogsThatRequestIsNotRejected()
    {
        var context = CreateContext(CreateEndpoint(new EndOfLifeAttribute("2027-01-01")));
        var harness = CreateMiddleware(new EndOfLifeOptions { LogThrottleMinutes = 0 }, new FakeTimeProvider(BlockedDay));

        await harness.Middleware.InvokeAsync(context);

        var message = harness.Logger.Messages.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(message, Does.Contain("after its block date"));
            Assert.That(message, Does.Contain(nameof(EnforcementMode.WarnOnly)));
        }
    }

    [Test]
    public async Task InvokeAsync_WhenBlockedAndEnforced_LogsThatRequestWasRejected()
    {
        var context = CreateContext(CreateEndpoint(new EndOfLifeAttribute("2027-01-01")));
        var harness = CreateMiddleware(
            new EndOfLifeOptions { LogThrottleMinutes = 0, EnforcementMode = EnforcementMode.Enforce },
            new FakeTimeProvider(BlockedDay)
        );

        await harness.Middleware.InvokeAsync(context);

        Assert.That(harness.Logger.Messages.Single(), Does.Contain("rejected"));
    }

    private static string ToHttpDate(DateOnly date) =>
        date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).ToString("R");

    private static ClaimsPrincipal PrincipalWith(string claimType, string value) =>
        new(new ClaimsIdentity([new Claim(claimType, value)], authenticationType: "test"));

    private static DefaultHttpContext CreateWarningContext(string claimType, string value)
    {
        var context = CreateContext(CreateEndpoint(new EndOfLifeAttribute("2027-01-01") { WarnAfter = "2026-10-23" }));
        context.User = PrincipalWith(claimType, value);

        return context;
    }

    private static string ReadBody(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);

        return reader.ReadToEnd();
    }

    private static DefaultHttpContext CreateContext(RouteEndpoint endpoint)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.SetEndpoint(endpoint);

        return context;
    }

    private static RouteEndpoint CreateEndpoint(params object[]? metadata) =>
        new(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse("/api/v1/test"),
            order: 0,
            new EndpointMetadataCollection(metadata ?? []),
            displayName: "Test endpoint"
        );

    private Harness CreateMiddleware(EndOfLifeOptions options, FakeTimeProvider timeProvider)
    {
        var next = new NextCallTracker();
        var optionsMonitor = new TestOptionsMonitor<EndOfLifeOptions>(options);
        var logger = new RecordingLogger();
        var metricsCollector = new MetricsCollector();
        var metrics = new EndOfLifeMetrics(metricsCollector, Options.Create(options));

        var middleware = new EndOfLifeMiddleware(
            next.InvokeAsync,
            timeProvider,
            optionsMonitor,
            metrics,
            logger,
            ThrottleCache
        );

        _harness = new Harness
        {
            Middleware = middleware,
            Options = optionsMonitor,
            Metrics = metricsCollector,
            MetricsCollector = metrics,
            Logger = logger,
            Next = next
        };

        return _harness;
    }

    private sealed class Harness : IDisposable
    {
        public required EndOfLifeMiddleware Middleware { get; init; }

        public required TestOptionsMonitor<EndOfLifeOptions> Options { get; init; }

        public required MetricsCollector Metrics { get; init; }

        public required EndOfLifeMetrics MetricsCollector { get; init; }

        public required RecordingLogger Logger { get; init; }

        public required NextCallTracker Next { get; init; }

        public void Dispose()
        {
            MetricsCollector.Dispose();
            Metrics.Dispose();
        }
    }

    private sealed class NextCallTracker
    {
        public bool Called { get; private set; }

        public Task InvokeAsync(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            Called = true;

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingLogger : ILogger<EndOfLifeMiddleware>
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_messages)
                {
                    return [.. _messages];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            ArgumentNullException.ThrowIfNull(formatter);

            lock (_messages)
            {
                _messages.Add(formatter(state, exception));
            }
        }
    }
}