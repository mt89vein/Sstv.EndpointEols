using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Sstv.EndpointEol.IntegrationTests;

/// <summary>
/// Boots <c>tests/Sstv.EndpointEol.Sample</c> with a controllable clock and enforcement mode,
/// so every lifecycle phase can be exercised through a real HTTP pipeline.
/// </summary>
internal sealed class SampleAppFactory : WebApplicationFactory<Program>
{
    private readonly EnforcementMode? _enforcementMode;

    /// <summary>
    /// Creates a factory that serves requests as if the current UTC date was <paramref name="now"/>.
    /// </summary>
    /// <param name="now">Fake current time.</param>
    /// <param name="enforcementMode">Optional enforcement mode override.</param>
    public SampleAppFactory(DateTimeOffset now, EnforcementMode? enforcementMode = null)
    {
        TimeProvider = new FakeTimeProvider(now);
        _enforcementMode = enforcementMode;
    }

    /// <summary>
    /// Clock injected into the sample application.
    /// </summary>
    public FakeTimeProvider TimeProvider { get; }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(TimeProvider);

            if (_enforcementMode is { } mode)
            {
                services.PostConfigure<EndOfLifeOptions>(options => options.EnforcementMode = mode);
            }
        });
    }
}
