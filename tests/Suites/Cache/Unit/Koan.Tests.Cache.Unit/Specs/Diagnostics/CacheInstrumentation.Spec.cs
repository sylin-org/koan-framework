using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Koan.Cache.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Koan.Tests.Cache.Unit.Specs.Diagnostics;

public sealed class CacheInstrumentationSpec
{
    private readonly ITestOutputHelper _output;

    public CacheInstrumentationSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public Task Recorders_update_counters()
        => TestPipeline.For<CacheInstrumentationSpec>(_output, nameof(Recorders_update_counters))
            .Assert(_ =>
            {
                using var instrumentation = new CacheInstrumentation(NullLogger<CacheInstrumentation>.Instance);
                using var listener = new MeterListener();
                var measurements = new ConcurrentDictionary<string, long>();

                listener.InstrumentPublished = (instrument, meterListener) =>
                {
                    if (instrument.Meter.Name == "Koan.Cache")
                    {
                        meterListener.EnableMeasurementEvents(instrument);
                    }
                };

                listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
                {
                    measurements.AddOrUpdate(instrument.Name, measurement, (_, current) => current + measurement);
                });

                listener.Start();

                instrumentation.RecordHit("key", "memory");
                instrumentation.RecordMiss("key", "memory");
                instrumentation.RecordSet("key", "memory");
                instrumentation.RecordRemove("key", "memory", success: true);
                instrumentation.RecordInvalidation("key", "memory", "manual");

                listener.RecordObservableInstruments();
                listener.Dispose();

                measurements.Should().ContainKey("koan.cache.hits").WhoseValue.Should().Be(1);
                measurements.Should().ContainKey("koan.cache.misses").WhoseValue.Should().Be(1);
                measurements.Should().ContainKey("koan.cache.sets").WhoseValue.Should().Be(1);
                measurements.Should().ContainKey("koan.cache.removes").WhoseValue.Should().Be(1);
                measurements.Should().ContainKey("koan.cache.invalidations").WhoseValue.Should().Be(1);
                return ValueTask.CompletedTask;
            })
            .RunAsync();
}
