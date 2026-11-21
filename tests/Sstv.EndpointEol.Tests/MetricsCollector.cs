using System.Diagnostics.Metrics;

namespace Sstv.EndpointEol.Tests;

/// <summary>
/// <see cref="IMeterFactory"/> that keeps the created <see cref="Meter"/> and collects
/// measurements with their tags, so metric assertions are possible in unit tests.
/// </summary>
internal sealed class MetricsCollector : IMeterFactory
{
    internal static string RequestsInstrument => "eol.requests";

    internal static string ConsumersInstrument => "eol.consumers";

    private readonly Meter _meter;
    private readonly MeterListener _listener = new();
    private readonly List<Measurement> _measurements = [];

    public MetricsCollector(string meterName = "Sstv.EndpointEol")
    {
        _meter = new Meter(meterName);

        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (ReferenceEquals(instrument.Meter, _meter))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var tagsSnapshot = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var tag in tags)
            {
                tagsSnapshot[tag.Key] = tag.Value;
            }

            lock (_measurements)
            {
                _measurements.Add(new Measurement(instrument.Name, value, tagsSnapshot));
            }
        });

        _listener.Start();
    }

    /// <summary>
    /// All collected measurements, in recording order.
    /// </summary>
    public Measurement[] Measurements
    {
        get
        {
            lock (_measurements)
            {
                return [.. _measurements];
            }
        }
    }

    /// <summary>
    /// Measurements of a single instrument.
    /// </summary>
    /// <param name="instrument">Instrument name.</param>
    public Measurement[] MeasurementsOf(string instrument) =>
        [.. Measurements.Where(x => x.Instrument == instrument)];

    /// <inheritdoc />
    public Meter Create(MeterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return _meter;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _listener.Dispose();
        _meter.Dispose();
    }

    internal sealed record Measurement(
        string Instrument,
        long Value,
        IReadOnlyDictionary<string, object?> Tags
    )
    {
        public string? Tag(string name) => Tags.TryGetValue(name, out var value) ? value?.ToString() : null;
    }
}