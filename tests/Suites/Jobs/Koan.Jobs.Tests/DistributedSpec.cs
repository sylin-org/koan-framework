using Koan.Jobs;
using Koan.Jobs.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Koan.Jobs.Tests;

/// <summary>Distributed tier: two orchestrators (the "nodes") share one ledger. The ledger is the coordinator —
/// competing consumers never double-claim, and a resource gate set by one node is honored by the other.</summary>
public sealed class DistributedSpec
{
    private static JobOrchestrator SecondNode(JobsHarness host)
        => new(
            host.Ledger,
            host.Registry,
            host.Services.GetRequiredService<IOptions<JobsOptions>>(),
            host.Clock,
            NullLogger<JobOrchestrator>.Instance,
            host.Services.GetRequiredService<IServiceScopeFactory>());

    [Fact]
    public async Task competing_consumers_never_double_claim()
    {
        GreetJob.Reset();
        await using var host = await JobsHarness.StartInMemoryAsync();
        var nodeB = SecondNode(host);

        var jobs = Enumerable.Range(0, 50).Select(i => new GreetJob { Name = $"n{i}" }).ToList();
        await jobs.Submit();

        await Task.WhenAll(host.Drain(), nodeB.DrainAsync());   // both nodes pull from the one ledger

        GreetJob.Executions.Should().Be(50, "each job is claimed and run exactly once across the two nodes");
    }

    [Fact]
    public async Task a_gate_set_by_one_node_is_honored_by_another()
    {
        GatedJob.Reset();
        GatedJob.Trip429 = true;
        await using var host = await JobsHarness.StartInMemoryAsync();
        var nodeB = SecondNode(host);

        var a = new GatedJob { Host = "api" };
        await a.Job.Submit();
        await host.Drain();                 // node A runs a → trips 429 → gates "api" in the shared ledger
        GatedJob.Executions.Should().Be(1);

        var b = new GatedJob { Host = "api" };
        await b.Job.Submit();
        await nodeB.DrainAsync();            // node B honors the cross-node gate → b deferred, not run
        GatedJob.Executions.Should().Be(1, "the other node must not hammer a host node A was 429'd on");

        host.Advance(TimeSpan.FromMinutes(5));
        await nodeB.DrainAsync();            // gate released → b runs on node B
        (await host.StatusOf<GatedJob>(b.Id)).Should().Be(JobStatus.Completed);
    }
}
