using System.Diagnostics.Metrics;

namespace Sstv.EndpointEol.IntegrationTests;

/// <summary>
/// Verifies that requests routed through the sample application produce the documented
/// <c>eol.requests</c> metric with low-cardinality tags.
/// </summary>
[TestFixture]
public sealed class MetricsIntegrationTests
{
    private static readonly Guid BookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset WarningDay = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task RequestToDeprecatedEndpoint_RecordsRequestMetric()
    {
        var measurements = new List<Measurement>();
        using var listener = new MeterListener
        {
            InstrumentPublished = static (instrument, currentListener) =>
            {
                if (instrument.Meter.Name == "Sstv.EndpointEol")
                {
                    currentListener.EnableMeasurementEvents(instrument);
                }
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var tagValues = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (var tag in tags)
            {
                tagValues[tag.Key] = tag.Value;
            }

            lock (measurements)
            {
                measurements.Add(new Measurement(instrument.Name, value, tagValues));
            }
        });

        listener.Start();

        using var factory = new SampleAppFactory(WarningDay, EnforcementMode.Enforce);
        using var client = factory.CreateClient();

        using var response = await client.DeleteAsync($"/api/v1/books/{BookId}");

        Assert.That(response.IsSuccessStatusCode, Is.True);

        var request = measurements.Single(static x => x.Instrument == "eol.requests");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.Value, Is.EqualTo(1));
            Assert.That(request.Tag("phase"), Is.EqualTo("warning"));
            Assert.That(request.Tag("route"), Is.EqualTo("/api/v1/books/{id:guid}"));
            Assert.That(request.Tag("method"), Is.EqualTo("delete"));
        }
    }

    private sealed record Measurement(string Instrument, long Value, IReadOnlyDictionary<string, object?> Tags)
    {
        public string? Tag(string name) => Tags.TryGetValue(name, out var value) ? value?.ToString() : null;
    }
}
