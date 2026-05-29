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
}
