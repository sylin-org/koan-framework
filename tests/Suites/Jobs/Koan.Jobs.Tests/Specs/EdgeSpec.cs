namespace Koan.Jobs.Tests.Specs;

/// <summary>Edge-triggered submit: batch enqueue and declared idempotency/coalescing.</summary>
public sealed class EdgeSpec
{
    [Fact]
    public async Task batch_submit_runs_every_work_item()
    {
        GreetJob.Reset();
        await using var host = await JobsTestHost.StartAsync();

        var jobs = Enumerable.Range(0, 50).Select(i => new GreetJob { Name = $"n{i}" }).ToList();
        await jobs.Submit();          // constrained IEnumerable<T> extension
        await host.Drain();

        GreetJob.Executions.Should().Be(50);
    }

    [Fact]
    public async Task idempotent_submit_coalesces_duplicates()
    {
        DedupeJob.Reset();
        await using var host = await JobsTestHost.StartAsync();

        await new DedupeJob { Key = "k1" }.Job.Submit();
        await new DedupeJob { Key = "k1" }.Job.Submit();  // same coalesce key → collapses onto the first
        await new DedupeJob { Key = "k2" }.Job.Submit();
        await host.Drain();

        DedupeJob.Executions.Should().Be(2); // k1 once, k2 once
    }
}
