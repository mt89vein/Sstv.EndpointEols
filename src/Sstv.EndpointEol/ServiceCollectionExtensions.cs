using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sstv.EndpointEol;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers end-of-life management for endpoints marked with <see cref="EndOfLifeAttribute"/>:
    /// middleware (headers, warnings, metrics, blocking), diagnostics registry and startup validation.
    /// Swagger/OpenAPI annotations live in the separate <c>Sstv.EndpointEol.Swashbuckle</c> package.
    /// </summary>
    /// <param name="services">Service registrator.</param>
    /// <param name="configuration">
    /// Optional configuration section to bind <see cref="EndOfLifeOptions"/> from.
    /// </param>
    /// <param name="configure">Optional options configuration delegate.</param>
    /// <returns>Service registrator.</returns>
    public static IServiceCollection AddEndOfLife(
        this IServiceCollection services,
        IConfiguration? configuration = null,
        Action<EndOfLifeOptions>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddMemoryCache();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<EndOfLifeMetrics>();
        services.TryAddSingleton<IEndpointEolRegistry, EndpointEolRegistry>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, EndOfLifeValidationHostedService>());

        services.AddOptions<EndOfLifeOptions>()
            .Configure(options => configuration?.Bind(options))
            .Configure(options => configure?.Invoke(options))
            .Validate(options => Enum.IsDefined(options.EnforcementMode), "EnforcementMode has unsupported value.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.MeterName), "MeterName must be non-empty.")
            .Validate(options => options.LogThrottleMinutes >= 0, "LogThrottleMinutes must be non-negative.")
            .ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Places the end-of-life middleware into the request pipeline.
    /// Must be called after <c>UseRouting</c>; recommended placement is right after authorization,
    /// so user identity is available in logs and metrics.
    /// </summary>
    /// <param name="app">Application builder.</param>
    /// <returns>Application builder.</returns>
    public static IApplicationBuilder UseEndOfLife(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<EndOfLifeMiddleware>();
    }
}

/// <summary>
/// Fails application startup when any <see cref="EndOfLifeAttribute"/> has invalid dates and logs
/// the full list of tracked endpoints once.
/// </summary>
/// <remarks>
/// Validation runs in <see cref="IHostedLifecycleService.StartedAsync"/>: by that moment every
/// hosted service (including the web host that builds the routing pipeline) has started, so the
/// endpoint registry sees controller and Minimal API endpoints instead of an empty routing table.
/// </remarks>
internal sealed class EndOfLifeValidationHostedService : IHostedLifecycleService
{
    private readonly IServiceProvider _services;
    private readonly IEndpointEolRegistry _registry;
    private readonly ILogger<EndOfLifeValidationHostedService> _logger;

    /// <summary>
    /// Creates new instance of <see cref="EndOfLifeValidationHostedService"/>.
    /// </summary>
    /// <param name="services">Service provider.</param>
    /// <param name="registry">Endpoint eol registry.</param>
    /// <param name="logger">Logger.</param>
    public EndOfLifeValidationHostedService(
        IServiceProvider services,
        IEndpointEolRegistry registry,
        ILogger<EndOfLifeValidationHostedService> logger
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(logger);

        _services = services;
        _registry = registry;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StartedAsync(CancellationToken cancellationToken)
    {
        // MVC builds action descriptors lazily. Touch them before the snapshot so attributes on
        // controller actions are validated at startup instead of on the first request.
        _ = _services.GetService<IActionDescriptorCollectionProvider>()?.ActionDescriptors;

        // Snapshot is always taken: it is what validates the attributes, regardless of log level.
        var items = _registry.GetSnapshot();

        if (_logger.IsEnabled(LogLevel.Information))
        {
            var warnAfterString = items.Count > 0 ? items.Min(x => x.WarnAfter)?.ToString("yyyy-MM-dd") : null;

            _logger.LogInformation(
                "End-of-life tracking initialized. {Count} endpoint(s) marked with [EndOfLife]. Earliest warning date: {EarliestWarnAfter}.",
                items.Count, warnAfterString
            );

            foreach (var item in items)
            {
                _logger.LogInformation(
                    "EOL endpoint {Method} {Path}. WarnAfter: {WarnAfter:yyyy-MM-dd}. BlockAfter: {BlockAfter:yyyy-MM-dd}. Phase: {Phase}. Replacement: {Replacement}",
                    item.Method, item.Path, item.WarnAfter, item.BlockAfter, item.Phase, item.ReplacedBy
                );
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
