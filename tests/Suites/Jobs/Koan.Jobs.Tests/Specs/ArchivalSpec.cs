namespace Koan.Jobs.Tests.Specs;

/// <summary>Terminal archival: benign terminals (Completed/Cancelled) past retention are purged; recent ones and
/// Failed/Dead are kept (replayable).</summary>
public sealed class ArchivalSpec
{
    [Fact]
    public async Task purges_completed_jobs_past_retention()
    {
        GreetJob.Reset();
        await using var host = await JobsTestHost.StartAsync(o => o.ArchiveAfter = TimeSpan.FromHours(1));
        var j = new GreetJob { Name = "x" };
        var id = j.Id;
        await j.Job.Submit();
        await host.Drain();
        (await host.StatusOf<GreetJob>(id)).Should().Be(JobStatus.Completed);

        host.Advance(TimeSpan.FromHours(2));
        var purged = await host.Archive();

        purged.Should().Be(1);
        (await host.StatusOf<GreetJob>(id)).Should().BeNull();
    }

    [Fact]
    public async Task keeps_recent_completed_jobs()
    {
        GreetJob.Reset();
        await using var host = await JobsTestHost.StartAsync(o => o.ArchiveAfter = TimeSpan.FromHours(1));
        var j = new GreetJob { Name = "x" };
        var id = j.Id;
        await j.Job.Submit();
        await host.Drain();

        host.Advance(TimeSpan.FromMinutes(10));  // within retention
        await host.Archive();

        (await host.StatusOf<GreetJob>(id)).Should().Be(JobStatus.Completed);
    }

    [Fact]
    public async Task keeps_failed_jobs_even_past_retention()
    {
        FlakyJob.Reset();
        FlakyJob.SucceedAtAttempt = 99; // always fails → Failed
        await using var host = await JobsTestHost.StartAsync(o => o.ArchiveAfter = TimeSpan.FromHours(1));
        var j = new FlakyJob();
        var id = j.Id;
        await j.Job.Submit(FlakyJob.Action);
        for (var i = 0; i < 3; i++) { await host.Drain(); host.Advance(TimeSpan.FromMinutes(1)); }
        (await host.StatusOf<FlakyJob>(id)).Should().Be(JobStatus.Failed);

        host.Advance(TimeSpan.FromHours(2));
        await host.Archive();

        (await host.StatusOf<FlakyJob>(id)).Should().Be(JobStatus.Failed, "Failed jobs are retained (replayable)");
    }
}
