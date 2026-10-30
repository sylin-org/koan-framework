using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace Koan.Jobs.Tests;

/// <summary>JOBS-0010: the volatile tier releases settled jobs beyond its terminal capacity, and a claim walks only the
/// due rows at the head of each lane instead of scanning and sorting every record.</summary>
public sealed class InMemoryLedgerSpec(ITestOutputHelper output)
{
    [Fact]
    public async Task settled_jobs_beyond_the_terminal_capacity_are_released()
    {
        GreetJob.Reset();
        await using var host = await JobsHarness.StartInMemoryAsync(o => o.InMemoryTerminalCapacity = 3);
        var jobs = Enumerable.Range(0, 10).Select(i => new GreetJob { Name = $"n{i}" }).ToList();
        await jobs.Submit();
        await host.Drain();

        GreetJob.Executions.Should().Be(10);
        var retained = await host.Ledger.Query(new JobQuery(WorkType: typeof(GreetJob).FullName!), default);
        retained.Should().HaveCount(3, "only the newest settled jobs stay in memory")
            .And.OnlyContain(r => r.Status == JobStatus.Completed);
    }

    [Fact]
    public async Task a_handle_hears_the_outcome_of_a_job_evicted_when_it_settled()
    {
        // JOBS-0010: with no settled history kept, the ledger forgets a job the moment it settles; a handle waiting on it
        // still hears its real outcome, and a handle that asks afterwards is told nobody can read it, never Dead.
        GreetJob.Reset();
        await using var host = await JobsHarness.StartInMemoryAsync(o => o.InMemoryTerminalCapacity = 0);
        var handle = await new GreetJob { Name = "evicted" }.Job.Submit();
        var waiting = handle.Completion(TimeSpan.FromMinutes(1));
        await host.Drain();

        var outcome = await waiting.WaitAsync(TimeSpan.FromSeconds(10));
        outcome.Status.Should().Be(JobStatus.Completed);
        outcome.Succeeded.Should().BeTrue();

        var late = await host.Coordinator.Handle(handle.JobId).Completion(TimeSpan.FromMinutes(1)).WaitAsync(TimeSpan.FromSeconds(10));
        late.Status.Should().BeNull("the ledger no longer holds the job and nobody here heard it settle");
        late.Error.Should().Contain("evicted");
        late.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task a_settlement_is_heard_before_the_ledger_evicts_it()
    {
        // JOBS-0010: with no settled history kept, the row leaves the ledger inside the write that settles it. The ledger
        // tells the waiting handles first — on a settle and on a cancellation alike — so none of them ever reads the gap.
        var completions = new JobCompletions();
        var ledger = new InMemoryJobLedger(Options.Create(new JobsOptions { InMemoryTerminalCapacity = 0 }), completions);
        var now = DateTimeOffset.UnixEpoch;
        await ledger.AppendMany([Row("t", "settled", now), Row("t", "cancelled", now.AddMinutes(10))], default);

        var claimed = (await ledger.ClaimNext("owner", now, now.AddMinutes(1), [], default))!;
        using var settling = completions.Hear(claimed.Id);
        claimed.Status = JobStatus.Completed;
        (await ledger.Update(claimed, default)).Should().BeTrue();
        (await ledger.Get(claimed.Id, default)).Should().BeNull("no settled history is kept");
        settling.Heard.IsCompleted.Should().BeTrue("the ledger told the settlement before it evicted the row");
        (await settling.Heard).Status.Should().Be(JobStatus.Completed);

        var queued = (await ledger.Get("t:cancelled", default))!;
        using var cancelling = completions.Hear(queued.Id);
        queued.Status = JobStatus.Cancelled;
        (await ledger.Update(queued, default)).Should().BeTrue();
        (await ledger.Get(queued.Id, default)).Should().BeNull();
        cancelling.Heard.IsCompleted.Should().BeTrue("a cancellation is told before its row is evicted too");
        (await cancelling.Heard).Status.Should().Be(JobStatus.Cancelled);
    }

    [Fact]
    public async Task reads_never_wait_for_the_claim_lock()
    {
        // JOBS-0010: a status read by work item, a read by id, a count and a query read maintained structures without the
        // claim lock, so none of them waits for a claim. Shown by holding the lock while they run beside a deep backlog.
        var ledger = new InMemoryJobLedger(Options.Create(new JobsOptions()));
        var now = DateTimeOffset.UnixEpoch;
        await ledger.AppendMany(Enumerable.Range(0, 20_000).Select(i => Row("t", $"later{i}", now.AddDays(1))).ToList(), default);
        await ledger.Append(Row("t", "target", now), default);

        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = new Thread(() =>
        {
            lock (ledger.ClaimLock)
            {
                held.Set();
                release.Wait(TimeSpan.FromSeconds(30));
            }
        }) { IsBackground = true };
        holder.Start();
        held.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
        try
        {
            var reads = Task.Run(async () => (
                Status: await ledger.Query(new JobQuery("t", "target") { Size = 1 }, default),
                ById: await ledger.Get("t:target", default),
                Count: await ledger.CountActive("t", default),
                Page: await ledger.Query(new JobQuery(Status: JobStatus.Queued) { Size = 10 }, default)));
            var done = await reads.WaitAsync(TimeSpan.FromSeconds(10));

            done.Status.Should().ContainSingle().Which.WorkId.Should().Be("target");
            done.ById!.WorkId.Should().Be("target");
            done.Count.Should().Be(20_001);
            done.Page.Should().HaveCount(10);
        }
        finally
        {
            release.Set();
            holder.Join(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task claims_cost_the_same_beside_a_deep_backlog_that_is_not_due()
    {
        // A claim used to filter every record and build its busy set from every record. Measured as a ratio, so JIT and
        // machine load land on both sides: claims beside 20,000 rows that are not due against the same claims alone.
        const int claims = 200;
        await TimeClaims(claims, backlog: 0);   // warm-up: JIT and first-use costs land here, not in either side
        var alone = await TimeClaims(claims, backlog: 0);
        var beside = await TimeClaims(claims, backlog: 20_000);
        output.WriteLine($"{claims} claims: alone {alone.TotalMilliseconds:F1}ms, beside the backlog {beside.TotalMilliseconds:F1}ms");

        (beside.TotalMilliseconds / Math.Max(alone.TotalMilliseconds, 1)).Should().BeLessThan(5,
            "rows that are not due are never visited, so the backlog costs a claim nothing");
    }

    private static async Task<TimeSpan> TimeClaims(int claims, int backlog)
    {
        await using var host = await JobsHarness.StartInMemoryAsync();
        var now = host.Clock.GetUtcNow();
        var wt = typeof(GreetJob).FullName!;
        await host.Ledger.AppendMany(Enumerable.Range(0, backlog).Select(i => Row(wt, $"later{i}", now.AddDays(1).AddSeconds(i))).ToList(), default);
        await host.Ledger.AppendMany(Enumerable.Range(0, claims).Select(i => Row(wt, $"due{i}", now.AddSeconds(-claims + i))).ToList(), default);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < claims; i++)
            (await host.Ledger.ClaimNext("owner", now, now.AddMinutes(1), Array.Empty<string>(), default)).Should().NotBeNull();
        watch.Stop();
        return watch.Elapsed;
    }

    private static JobRecord Row(string workType, string workId, DateTimeOffset visibleAt) => new()
    {
        Id = $"{workType}:{workId}",
        WorkType = workType,
        WorkId = workId,
        Action = "",
        Lane = "default",
        Status = JobStatus.Queued,
        VisibleAt = visibleAt,
        FirstSubmittedAt = visibleAt,
    };
}
