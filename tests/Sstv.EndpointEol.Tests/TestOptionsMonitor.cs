using Microsoft.Extensions.Options;

namespace Sstv.EndpointEol.Tests;

/// <summary>
/// Test double for <see cref="IOptionsMonitor{TOptions}"/> with mutable value,
/// so hot-reloaded options can be simulated without rebuilding the middleware.
/// </summary>
/// <typeparam name="TOptions">Options type.</typeparam>
internal sealed class TestOptionsMonitor<TOptions>(TOptions value) : IOptionsMonitor<TOptions>
{
    /// <inheritdoc />
    public TOptions CurrentValue { get; private set; } = value;

    /// <inheritdoc />
    public TOptions Get(string? name) => CurrentValue;

    /// <inheritdoc />
    public IDisposable? OnChange(Action<TOptions, string?> listener) => null;

    /// <summary>
    /// Simulates a configuration reload.
    /// </summary>
    /// <param name="value">New options value.</param>
    public void Set(TOptions value) => CurrentValue = value;
}