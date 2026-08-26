using Koan.Jobs.TestKit;

namespace Koan.Jobs.Tests;

/// <summary>PMC-055: the fleet roster — liveness, confirmed-death reclaim, and zombie-settle fencing.</summary>
public sealed class FleetRosterSpec
{
    private static DateTimeOffset Now(JobsHarness host) => host.Clock.GetUtcNow();

    [Fact]
    public async Task reaper_reclaims_jobs_of_a_confirmed_dead_worker()
    {
        GreetJob.Reset();
        await using var host = await JobsHarness.StartInMemoryAsync();
        var g = new GreetJob { Name = "orphaned" };
        var id = g.Id;
        await GreetJob.Upsert(g);
        var now = Now(host);
        await host.Ledger.Append(new JobRecord
        {
            WorkType = typeof(GreetJob).FullName!,
            WorkId = id,
            Action = "",
            Status = JobStatus.Running,
            Attempt = 1,
            Lane = "default",
            FirstSubmittedAt = now,
            VisibleAt = now,
            LeaseUntil = now + TimeSpan.FromMinutes(10),   // lease is NOT lapsed — death is the reason
            Owner = "dead-worker",
        }, default);
        await WorkerNode.Upsert(new WorkerNode { Id = "dead-worker", LastSeenAt = now - TimeSpan.FromSeconds(60), StartedAt = now }, default);

        await host.Reap();

        var reclaimed = await host.JobFor<GreetJob>(id);
        reclaimed!.Status.Should().Be(JobStatus.Queued);
        reclaimed.Owner.Should().BeNull();
        reclaimed.Transitions.Last().Note.Should().Contain("confirmed dead");

        await host.Drain();
        (await host.StatusOf<GreetJob>(id)).Should().Be(JobStatus.Completed);
    }

    [Fact]
    public async Task jobs_of_live_workers_survive_the_reaper()
    {
        GreetJob.Reset();
        await using var host = await JobsHarness.StartInMemoryAsync();
        var g = new GreetJob { Name = "inflight" };
        var id = g.Id;
        await GreetJob.Upsert(g);
        var now = Now(host);
        await host.Ledger.Append(new JobRecord
        {
            WorkType = typeof(GreetJob).FullName!,
            WorkId = id,
            Action = "",
            Status = JobStatus.Running,
            Attempt = 1,
            Lane = "default",
            FirstSubmittedAt = now,
            VisibleAt = now,
            LeaseUntil = now + TimeSpan.FromMinutes(10),
            Owner = "live-worker",
        }, default);
        await WorkerNode.Upsert(new WorkerNode { Id = "live-worker", LastSeenAt = now, StartedAt = now }, default);

        await host.Reap();

        (await host.JobFor<GreetJob>(id))!.Status.Should().Be(JobStatus.Running);
    }

    [Fact]
    public async Task zombie_settlement_bounces_at_the_fence()
    {
        GreetJob.Reset();
        await using var host = await JobsHarness.StartInMemoryAsync();
        var g = new GreetJob { Name = "contested" };
        var id = g.Id;
        await GreetJob.Upsert(g);
        var now = Now(host);
        await host.Ledger.Append(new JobRecord
        {
            WorkType = typeof(GreetJob).FullName!,
            WorkId = id,
            Action = "",
            Status = JobStatus.Queued,
            Attempt = 0,
            Lane = "default",
            FirstSubmittedAt = now,
            VisibleAt = now,
        }, default);

        // Node A claims; then goes silent long enough to be confirmed dead; the reaper forfeits the row.
        var claimedByA = await host.Ledger.ClaimNext("node-a", now, now + TimeSpan.FromMinutes(10), Array.Empty<string>(), default);
        claimedByA.Should().NotBeNull();
        await WorkerNode.Upsert(new WorkerNode { Id = "node-a", LastSeenAt = now - TimeSpan.FromSeconds(60), StartedAt = now }, default);
        await host.Reap();

        // Node B claims the forfeited row.
        host.Advance(TimeSpan.FromSeconds(1));
        var claimedByB = await host.Ledger.ClaimNext("node-b", Now(host), Now(host) + TimeSpan.FromMinutes(10), Array.Empty<string>(), default);
        claimedByB.Should().NotBeNull();

        // Zombie A wakes and tries to settle with its stale record — the fence bounces it.
        var zombieRecord = claimedByA!;
        zombieRecord.Status = JobStatus.Completed;
        zombieRecord.Owner = null;
        zombieRecord.LastSettledAt = Now(host);
        (await host.Ledger.TrySettle(zombieRecord, "node-a", default)).Should().BeFalse(
            "the row was reclaimed; a revived zombie cannot clobber the new claimant");

        // The legitimate owner settles fine.
        var legitimate = claimedByB!;
        legitimate.Status = JobStatus.Completed;
        legitimate.Owner = null;
        legitimate.LastSettledAt = Now(host);
        (await host.Ledger.TrySettle(legitimate, "node-b", default)).Should().BeTrue();
    }

    [Fact]
    public async Task beat_registers_resign_removes_and_health_counts()
    {
        GreetJob.Reset();
        await using var host = await JobsHarness.StartInMemoryAsync();
        var orchestrator = (JobOrchestrator)host.Services.GetService(typeof(JobOrchestrator))!;

        await orchestrator.BeatAsync();
        (await WorkerNode.Query(w => w.LastSeenAt >= Now(host) - TimeSpan.FromSeconds(5))).Should().ContainSingle();

        await orchestrator.ResignAsync();
        (await WorkerNode.Query(_ => true)).Should().BeEmpty();
    }
}
