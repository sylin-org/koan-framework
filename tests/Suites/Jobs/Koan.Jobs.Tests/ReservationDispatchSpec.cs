using Koan.Jobs.TestKit;

namespace Koan.Jobs.Tests;

/// <summary>PMC-056: the reservation dispatch modality — "the jar hands a cookie to a named hand". A fresh
/// reservation binds exactly its hand; lapses and confirmed-dead hands return the cookie to the shelf; every
/// claim stays CAS-fenced and assignment is ledger-verifiable metadata on the row itself.</summary>
public sealed class ReservationDispatchSpec
{
    private static DateTimeOffset Now(JobsHarness host) => host.Clock.GetUtcNow();

    [Fact]
    public async Task a_fresh_reservation_binds_its_named_hand_and_blocks_peers()
    {
        GreetJob.Reset();
        await using var host = await JobsHarness.StartInMemoryAsync();
        var g = new GreetJob { Name = "pinned" };
        var id = g.Id;
        await GreetJob.Upsert(g);
        var now = Now(host);
        await host.Ledger.Append(new JobRecord
        {
            WorkType = typeof(GreetJob).FullName!,
            WorkId = id,
            Action = "",
            Status = JobStatus.Queued,
            Lane = "default",
            FirstSubmittedAt = now,
            VisibleAt = now,
        }, default);

        // The coordinator stamps a cookie for node-b. (The ledger keys reservations by the JOB row's own id,
        // not the work-item id.)
        var queuedRow = await host.JobFor<GreetJob>(id);
        (await host.Ledger.TryReserve(queuedRow!.Id, "node-b", now + TimeSpan.FromMinutes(5), now, default)).Should().BeTrue();

        // A peer cannot take it while the reservation is fresh…
        (await host.Ledger.ClaimNext("node-a", now, now + TimeSpan.FromMinutes(10), Array.Empty<string>(), default))
            .Should().BeNull("a fresh reservation hides the row from every other hand");

        // …and the named hand claims it through the same CAS path as any pull.
        var claimed = await host.Ledger.ClaimNext("node-b", now, now + TimeSpan.FromMinutes(10), Array.Empty<string>(), default);
        claimed.Should().NotBeNull();
        claimed!.Owner.Should().Be("node-b");

        // The claim consumes the cookie.
        (await host.JobFor<GreetJob>(id))!.ReservedFor.Should().BeNull();
    }

    [Fact]
    public async Task a_lapsed_reservation_is_open_to_any_hand()
    {
        GreetJob.Reset();
        await using var host = await JobsHarness.StartInMemoryAsync();
        var g = new GreetJob { Name = "stale" };
        var id = g.Id;
        await GreetJob.Upsert(g);
        var now = Now(host);
        await host.Ledger.Append(new JobRecord
        {
            WorkType = typeof(GreetJob).FullName!,
            WorkId = id,
            Action = "",
            Status = JobStatus.Queued,
            Lane = "default",
            FirstSubmittedAt = now,
            VisibleAt = now,
        }, default);
        await host.Ledger.TryReserve((await host.JobFor<GreetJob>(id))!.Id, "node-b", now + TimeSpan.FromSeconds(30), now, default);

        host.Advance(TimeSpan.FromSeconds(31));

        var stolen = await host.Ledger.ClaimNext("node-a", Now(host), Now(host) + TimeSpan.FromMinutes(10), Array.Empty<string>(), default);
        stolen.Should().NotBeNull("a lapsed reservation is dispatch metadata past its usefulness, not a lock");
        stolen!.Owner.Should().Be("node-a");
    }

    [Fact]
    public async Task the_senior_hand_releases_a_dead_workers_reservations_and_reassigns_them()
    {
        GreetJob.Reset();
        await using var host = await JobsHarness.StartInMemoryAsync(o =>
        {
            o.DispatchMode = JobDispatchMode.Reservation;
            o.WorkerHeartbeatInterval = TimeSpan.Zero;   // the assign duty must run on demand under the fake clock
        });
        var orchestrator = (JobOrchestrator)host.Services.GetService(typeof(JobOrchestrator))!;

        var g = new GreetJob { Name = "orphan-cookie" };
        var id = g.Id;
        await GreetJob.Upsert(g);
        var now = Now(host);
        await host.Ledger.Append(new JobRecord
        {
            WorkType = typeof(GreetJob).FullName!,
            WorkId = id,
            Action = "",
            Status = JobStatus.Queued,
            Lane = "default",
            FirstSubmittedAt = now,
            VisibleAt = now,
        }, default);

        // A since-departed worker holds the cookie; my node holds roster seniority (the roster has no one else).
        await host.Ledger.TryReserve((await host.JobFor<GreetJob>(id))!.Id, "ghost", now + TimeSpan.FromMinutes(5), now, default);

        await orchestrator.AssignReservationsAsync();

        var after = await host.JobFor<GreetJob>(id);
        after!.ReservedFor.Should().NotBeNull();
        after.ReservedFor.Should().Be(orchestrator.Owner,
            "the senior coordinator released the dead hand's cookie and re-handed it to a live member");
        after.Status.Should().Be(JobStatus.Queued);

        // The named hand can still claim it normally afterwards.
        await orchestrator.DrainAsync();
        (await host.StatusOf<GreetJob>(id)).Should().Be(JobStatus.Completed);
    }

    [Fact]
    public async Task assignment_targets_live_roster_members_only()
    {
        GreetJob.Reset();
        await using var host = await JobsHarness.StartInMemoryAsync(o =>
        {
            o.DispatchMode = JobDispatchMode.Reservation;
            o.WorkerHeartbeatInterval = TimeSpan.Zero;
        });
        var orchestrator = (JobOrchestrator)host.Services.GetService(typeof(JobOrchestrator))!;

        // Join the roster as its most senior member (deterministic under the fake clock): beat, then backdate
        // my StartedAt ahead of the live peer's.
        await orchestrator.BeatAsync();
        var now0 = Now(host);
        var mine = await WorkerNode.Get(orchestrator.Owner, default);
        mine!.StartedAt = now0 - TimeSpan.FromSeconds(10);
        await WorkerNode.Upsert(mine, default);
        await WorkerNode.Upsert(new WorkerNode
        {
            Id = "peer-live",
            LastSeenAt = now0,
            StartedAt = now0 - TimeSpan.FromSeconds(5),
        }, default);

        // Three pieces of work; an ex-member sits on the roster confirmed-dead.
        for (var i = 0; i < 3; i++)
        {
            var g = new GreetJob { Name = $"fleet-{i}" };
            await GreetJob.Upsert(g);
            await host.Ledger.Append(new JobRecord
            {
                WorkType = typeof(GreetJob).FullName!,
                WorkId = g.Id,
                Action = "",
                Status = JobStatus.Queued,
                Lane = "default",
                FirstSubmittedAt = now0,
                VisibleAt = now0,
            }, default);
        }
        await WorkerNode.Upsert(new WorkerNode
        {
            Id = "ghost",
            LastSeenAt = now0 - TimeSpan.FromSeconds(60),
            StartedAt = now0 - TimeSpan.FromSeconds(30),
        }, default);

        await orchestrator.AssignReservationsAsync();

        var reserved = await host.Ledger.Reservations(default);
        reserved.Should().NotBeEmpty("a due backlog with open capacity gets cookies");
        reserved.Select(r => r.ReservedFor).Should().OnlyContain(h =>
            h == orchestrator.Owner || h == "peer-live",
            "assignment may only land on live roster members");
        reserved.Should().OnlyContain(r => r.ReservedUntil > Now(host), "fresh stamps carry their lapse time");
    }

    [Fact]
    public async Task inline_mode_refuses_reservation_dispatch_with_a_corrective_message()
    {
        GreetJob.Reset();
        Func<Task> boot = async () => await JobsHarness.StartInMemoryAsync(o =>
        {
            o.Mode = JobMode.Inline;
            o.DispatchMode = JobDispatchMode.Reservation;
        });
        (await boot.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*DispatchMode.Reservation*Mode.Inline*no fleet roster*");
    }
}
