using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Jobs.Execution;
using Koan.Jobs.Options;

namespace Koan.Jobs.Core.Tests.Specs.Execution;

/// <summary>
/// Behaviour specs for the concurrency-lane gate (JOBS-0002/JOBS-0003): capacity resolution from
/// config and the SemaphoreSlim-backed permit. Lane *names* are now decided per-type by a job's
/// <c>Lane</c> override, so there is no attribute/name-resolution surface to test here.
/// </summary>
public sealed class JobLaneRegistrySpec
{
    private static JobLaneRegistry Build(Action<JobsOptions>? configure = null)
    {
        var opts = new JobsOptions { DefaultLaneConcurrency = 4 };
        configure?.Invoke(opts);
        return new JobLaneRegistry(Microsoft.Extensions.Options.Options.Create(opts));
    }

    [Fact]
    public void DefaultLane_is_named_default()
        => JobLanes.Default.Should().Be("default");

    [Fact]
    public void CapacityFor_uses_configured_max_concurrency()
    {
        var reg = Build(o => o.Lanes["cpu-transform"] = new JobLaneOptions { MaxConcurrency = 3 });
        reg.CapacityFor("cpu-transform").Should().Be(3);
    }

    [Fact]
    public void CapacityFor_falls_back_to_default_for_unlisted_lane()
    {
        var reg = Build(o => o.DefaultLaneConcurrency = 7);
        reg.CapacityFor("anything").Should().Be(7);
    }

    [Fact]
    public async Task AcquireAsync_bounds_concurrency_to_capacity_and_releases()
    {
        var reg = Build(o => o.Lanes["x"] = new JobLaneOptions { MaxConcurrency = 2 });

        var p1 = await reg.AcquireAsync("x", CancellationToken.None);
        var p2 = await reg.AcquireAsync("x", CancellationToken.None);

        var third = reg.AcquireAsync("x", CancellationToken.None);
        third.IsCompleted.Should().BeFalse();
        await Task.Delay(100);
        third.IsCompleted.Should().BeFalse();

        p1.Dispose();
        var winner = await Task.WhenAny(third, Task.Delay(1000));
        winner.Should().BeSameAs(third);

        (await third).Dispose();
        p2.Dispose();
    }

    [Fact]
    public async Task AcquireAsync_isolates_lanes_from_each_other()
    {
        var reg = Build(o =>
        {
            o.Lanes["busy"] = new JobLaneOptions { MaxConcurrency = 1 };
            o.Lanes["free"] = new JobLaneOptions { MaxConcurrency = 1 };
        });

        var busy = await reg.AcquireAsync("busy", CancellationToken.None);

        var free = reg.AcquireAsync("free", CancellationToken.None);
        var winner = await Task.WhenAny(free, Task.Delay(1000));
        winner.Should().BeSameAs(free);

        (await free).Dispose();
        busy.Dispose();
    }

    // --- JOBS-0004: per-partition concurrency tier -----------------------------------------------

    [Fact]
    public void PartitionCapacityFor_is_zero_when_lane_has_no_partition_tier()
    {
        var reg = Build(o => o.Lanes["egress"] = new JobLaneOptions { MaxConcurrency = 10 });
        reg.PartitionCapacityFor("egress", "anybrand").Should().Be(0);
    }

    [Fact]
    public void PartitionCapacityFor_uses_default_then_override()
    {
        var reg = Build(o =>
        {
            var lane = new JobLaneOptions { MaxConcurrency = 50, MaxConcurrencyPerPartition = 10 };
            lane.PartitionOverrides["xivmodarchive"] = 2;
            o.Lanes["egress"] = lane;
        });
        reg.PartitionCapacityFor("egress", "nexus").Should().Be(10);        // lane default
        reg.PartitionCapacityFor("egress", "xivmodarchive").Should().Be(2); // per-key override
    }

    [Fact]
    public async Task AcquireAsync_caps_concurrency_per_partition_within_a_lane()
    {
        var reg = Build(o => o.Lanes["egress"] = new JobLaneOptions { MaxConcurrency = 10, MaxConcurrencyPerPartition = 1 });

        var a1 = await reg.AcquireAsync("egress", "brandA", CancellationToken.None);

        // A second job for the same partition must wait (per-partition cap = 1)...
        var a2 = reg.AcquireAsync("egress", "brandA", CancellationToken.None);
        a2.IsCompleted.Should().BeFalse();

        // ...while a different partition in the same lane proceeds immediately (isolation).
        var b1 = reg.AcquireAsync("egress", "brandB", CancellationToken.None);
        (await Task.WhenAny(b1, Task.Delay(1000))).Should().BeSameAs(b1);

        a1.Dispose();
        (await Task.WhenAny(a2, Task.Delay(1000))).Should().BeSameAs(a2);

        (await a2).Dispose();
        (await b1).Dispose();
    }

    [Fact]
    public async Task AcquireAsync_hot_partition_waiters_do_not_occupy_lane_global_slots()
    {
        // The whole point of JOBS-0004: a hot partition (cap 1) must not be able to fill the
        // lane-global gate (cap 3) with waiters and starve the other partitions.
        var reg = Build(o => o.Lanes["egress"] = new JobLaneOptions { MaxConcurrency = 3, MaxConcurrencyPerPartition = 1 });

        var hot = await reg.AcquireAsync("egress", "hot", CancellationToken.None); // hot: 1 partition + 1 global

        // Five more "hot" jobs pile up — all blocked on the hot partition gate, holding NO global slot.
        var hotWaiters = new System.Collections.Generic.List<Task<IDisposable>>();
        for (var i = 0; i < 5; i++) hotWaiters.Add(reg.AcquireAsync("egress", "hot", CancellationToken.None));
        await Task.Delay(100);
        hotWaiters.Should().OnlyContain(t => !t.IsCompleted);

        // Two distinct cold partitions must still acquire (global had 2 free after the single hot job).
        var c1 = reg.AcquireAsync("egress", "cold1", CancellationToken.None);
        var c2 = reg.AcquireAsync("egress", "cold2", CancellationToken.None);
        (await Task.WhenAny(c1, Task.Delay(1000))).Should().BeSameAs(c1);
        (await Task.WhenAny(c2, Task.Delay(1000))).Should().BeSameAs(c2);

        // Cleanup.
        hot.Dispose();
        (await c1).Dispose();
        (await c2).Dispose();
        // Drain the hot backlog now that capacity is free.
        foreach (var w in hotWaiters) (await w).Dispose();
    }
}
