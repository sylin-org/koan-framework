namespace Koan.Jobs.Tests.Specs;

/// <summary>Retry/poison, durable cancellation (queued + running), and per-action timeout.</summary>
public sealed class RetryCancelTimeoutSpec
{
    [Fact]
    public async Task retries_then_succeeds()
    {
        FlakyJob.Reset();
        FlakyJob.SucceedAtAttempt = 3; // fail on attempts 1,2; succeed on 3 (MaxAttempts=3)
        await using var host = await JobsTestHost.StartAsync();
        var job = new FlakyJob();
        var id = job.Id;
        await job.Job.Submit(FlakyJob.Action);

        await host.Drain();                                   // attempt 1 fails → backoff
        host.Advance(TimeSpan.FromMinutes(1));
        await host.Drain();                                   // attempt 2 fails → backoff
        host.Advance(TimeSpan.FromMinutes(1));
        await host.Drain();                                   // attempt 3 succeeds

        FlakyJob.Executions.Should().Be(3);
        (await host.StatusOf<FlakyJob>(id)).Should().Be(JobStatus.Completed);
    }

    [Fact]
    public async Task poison_fails_after_exhausting_retries()
    {
        FlakyJob.Reset();
        FlakyJob.SucceedAtAttempt = 99; // always fails (MaxAttempts=3)
        await using var host = await JobsTestHost.StartAsync();
        var job = new FlakyJob();
        var id = job.Id;

        await job.Job.Submit(FlakyJob.Action);
        for (var i = 0; i < 3; i++)
        {
            await host.Drain();
            host.Advance(TimeSpan.FromMinutes(1));
        }

        FlakyJob.Executions.Should().Be(3);
        var rec = await host.JobFor<FlakyJob>(id);
        rec!.Status.Should().Be(JobStatus.Failed);
        rec.DeadReason.Should().Be(nameof(DeadReason.Poison));
    }

    [Fact]
    public async Task cancel_queued_job_never_runs()
    {
        GreetJob.Reset();
        await using var host = await JobsTestHost.StartAsync();
        var job = new GreetJob { Name = "x" };
        var id = job.Id;

        await job.Job.Submit();                               // queued (worker disabled → not run yet)
        await job.Job.Cancel();

        (await host.StatusOf<GreetJob>(id)).Should().Be(JobStatus.Cancelled);
        await host.Drain();
        GreetJob.Executions.Should().Be(0);
    }

    [Fact]
    public async Task cancel_running_job_cooperatively()
    {
        WaitJob.Reset();
        await using var host = await JobsTestHost.StartAsync();
        var job = new WaitJob();
        var id = job.Id;

        await job.Job.Submit(WaitJob.Action);
        var drain = host.Drain();                             // claims + runs WaitJob (blocks on ct)
        await Wait.Until(() => WaitJob.Executions == 1);

        await host.Coordinator.CancelWorkAsync(typeof(WaitJob).FullName!, id, default);
        await drain;

        (await host.StatusOf<WaitJob>(id)).Should().Be(JobStatus.Cancelled);
        WaitJob.Cancellations.Should().Be(1);
    }

    [Fact]
    public async Task timeout_fails_the_job()
    {
        WaitJob.Reset();
        await using var host = await JobsTestHost.StartAsync();
        var job = new WaitJob();
        var id = job.Id;

        await job.Job.Submit(WaitJob.Action);                 // Timeout=30s, MaxAttempts=1
        var drain = host.Drain();
        await Wait.Until(() => WaitJob.Executions == 1);

        host.Advance(TimeSpan.FromSeconds(31));               // fires the timeout CTS on the fake clock
        await drain;

        (await host.StatusOf<WaitJob>(id)).Should().Be(JobStatus.Failed);
        WaitJob.Cancellations.Should().Be(1);
    }
}
