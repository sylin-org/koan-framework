using System.Diagnostics;
using Koan.Cache.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Koan.Tests.Cache.Topology.Specs;

public sealed class CacheInstrumentationSpec
{
    [Fact]
    public void ActivitySource_name_is_Koan_Cache()
    {
        CacheInstrumentation.ActivitySource.Name.Should().Be("Koan.Cache");
    }

    [Fact]
    public void MeterName_is_Koan_Cache()
    {
        CacheInstrumentation.MeterName.Should().Be("Koan.Cache");
    }

    [Fact]
    public void StartActivity_returns_null_when_no_listener_subscribed()
    {
        // Zero-listener case must be zero-allocation — Activity is null.
        var activity = CacheInstrumentation.StartActivity("cache.read", "Todo:_:abc");
        activity.Should().BeNull("ActivitySource returns null when no listener is subscribed — hot-path safety");
    }

    [Fact]
    public void StartActivity_with_listener_attaches_key_tag()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Koan.Cache",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = CacheInstrumentation.StartActivity("cache.read", "Todo:_:abc");

        activity.Should().NotBeNull();
        activity!.GetTagItem("cache.key").Should().Be("Todo:_:abc");
        activity.OperationName.Should().Be("cache.read");
    }

    [Fact]
    public void Counters_do_not_throw_when_no_listener_subscribed()
    {
        var instr = new CacheInstrumentation(NullLogger<CacheInstrumentation>.Instance);

        var act = () =>
        {
            instr.RecordHit("k", "memory");
            instr.RecordMiss("k", "memory");
            instr.RecordSet("k", "memory");
            instr.RecordRemove("k", "memory", success: true);
            instr.RecordInvalidation("k", "memory", "test");
            instr.RecordCoherencePublished("redis-pubsub", "EvictKey");
            instr.RecordCoherenceReceived("redis-pubsub", "EvictKey");
            instr.RecordCoherenceApplied("redis-pubsub", "EvictKey");
            instr.RecordTierFetch("local", hit: true);
            instr.RecordTierFetch("remote", hit: false);
            instr.RecordReadDuration(0.5, hit: true);
            instr.RecordWriteDuration(1.5);
        };

        act.Should().NotThrow();
        instr.Dispose();
    }
}
