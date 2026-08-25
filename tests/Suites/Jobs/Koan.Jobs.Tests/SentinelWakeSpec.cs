using Koan.Jobs.TestKit;

namespace Koan.Jobs.Tests;

/// <summary>JOBS-0009 §3: in-memory hosts leave no wake stamp — a volatile tier never crosses nodes.</summary>
public sealed class SentinelWakeSpec
{
    [Fact]
    public async Task in_memory_hosts_leave_no_stamp()
    {
        await using var host = await JobsHarness.StartInMemoryAsync();
        GreetJob.Reset();
        var g = new GreetJob { Name = "local" };
        await g.Job.Submit("");
        (await WakeStamp.Get(WakeStamp.SingletonId)).Should().BeNull("a volatile tier never crosses nodes");

        await host.Drain();
        GreetJob.Executions.Should().Be(1);
    }
}
