using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Observability.Health;
using Koan.Data.Core;
using Koan.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Jobs.TestKit;

/// <summary>
/// The behavioral contract of the orchestrator, identical across every tier (ARCH-0079). A tier provides a
/// <see cref="CreateHostAsync"/>; these specs then run unchanged on in-memory and on each durable adapter — that
/// sameness is the proof of the constant at-least-once + idempotent guarantee.
/// </summary>
public abstract class JobBehaviorSuite
{
    /// <summary>Build a harness for this tier (in-memory, SQLite, Mongo, …).</summary>
    protected abstract Task<JobsHarness> CreateHostAsync(Action<JobsOptions>? configure = null, Action<IServiceCollection>? configureServices = null);

    // --- discovery + edge ---

    [Fact]
    public async Task invalid_work_address_is_dead_lettered_without_blocking_the_next_job()
    {
        await using var host = await CreateHostAsync();
        var now = host.Clock.GetUtcNow();
        var invalid = new JobRecord
        {
            WorkType = typeof(GreetJob).FullName!, WorkId = "invalid-address",
            WorkSource = "Default", WorkAdapter = "mongo", Exclusive = true,
            Status = JobStatus.Queued, Lane = "default", VisibleAt = now, FirstSubmittedAt = now
        };
        await host.Ledger.Append(invalid, default);
        var valid = new GreetJob { Name = "after-invalid-address" };
        await valid.Job.Submit();
        await host.Drain();
        (await host.Ledger.Get(invalid.Id, default))!.Status.Should().Be(JobStatus.Dead);
        (await GreetJob.Get(valid.Id))!.Greeting.Should().Be("Hello, after-invalid-address");
    }

    [Fact]
    public async Task data_address_scopes_coalescing_status_and_cancellation()
    {
        await using var host = await CreateHostAsync();
        var work = new DedupeJob { Key = Guid.NewGuid().ToString("N") };
        await work.Job.Submit();
        using (EntityContext.Partition("fr"))
        {
            await work.Job.Submit();
            await work.Job.Submit();
            await work.Job.Cancel();
            (await work.Job.Status()).Should().Be(JobStatus.Cancelled);
        }
        (await work.Job.Status()).Should().Be(JobStatus.Queued);
        var records = await host.Ledger.Query(new JobQuery(WorkId: work.Id), default);
        records.Should().HaveCount(2);
        records.Single(r => r.WorkPartition is null).CoalesceKey.Should().Be(
            host.Registry.Require(typeof(DedupeJob).FullName!).CoalesceKey(work, ""),
            "default-address coalescing remains compatible with existing records");
        await host.Drain();
        (await work.Job.Status()).Should().Be(JobStatus.Completed);
    }

    [Fact]
    public async Task async_source_cannot_change_the_captured_data_partition()
    {
        await using var host = await CreateHostAsync();
        var first = new GreetJob { Name = "first" };
        var second = new GreetJob { Name = "second" };
        async IAsyncEnumerable<GreetJob> Source()
        {
            yield return first;
            using (EntityContext.Partition("de")) yield return second;
            await Task.CompletedTask;
        }
        using (EntityContext.Partition("fr")) await Source().Submit();
        await host.Drain();
        using (EntityContext.Partition("fr"))
        {
            (await GreetJob.Get(first.Id))!.Greeting.Should().Be("Hello, first");
            (await GreetJob.Get(second.Id))!.Greeting.Should().Be("Hello, second");
        }
        using (EntityContext.Partition("de")) (await GreetJob.Get(second.Id)).Should().BeNull();
    }

    [Fact]
    public async Task partitioned_success_and_failure_chains_retain_the_work_address()
    {
        await using var host = await CreateHostAsync();
        var pipeline = new Pipeline();
        var continuation = new ContinueChain();
        using (EntityContext.Partition("fr"))
        {
            await pipeline.Job.Submit(Stage.Fetch);
            await continuation.Job.Submit(Step.One);
        }
        await host.Drain();
        using (EntityContext.Partition("fr"))
        {
            (await Pipeline.Get(pipeline.Id))!.Published.Should().BeTrue();
            (await ContinueChain.Get(continuation.Id))!.TwoRan.Should().BeTrue();
        }
        var records = await host.Ledger.Query(new JobQuery(WorkId: pipeline.Id), default);
        records.Should().HaveCount(4).And.OnlyContain(r => r.WorkPartition == "fr" && r.Status == JobStatus.Completed);
    }

    [Fact]
    public async Task exclusivity_distinguishes_same_id_in_different_data_partitions()
    {
        await using var host = await CreateHostAsync();
        var work = new GreetJob();
        await work.Job.Submit();
        using (EntityContext.Partition("fr")) await work.Job.Submit();
        var now = host.Clock.GetUtcNow();
        var first = await host.Ledger.ClaimNext("one", now, now.AddMinutes(1), [], default);
        var second = await host.Ledger.ClaimNext("two", now, now.AddMinutes(1), [], default);
        first.Should().NotBeNull();
        second.Should().NotBeNull("different physical work items do not share the exclusivity lock");
        second!.WorkPartition.Should().NotBe(first!.WorkPartition);
    }

    [Fact]
    public async Task partitioned_submission_uses_central_ledger_and_executes_at_original_address()
    {
        await using var host = await CreateHostAsync();
        var root = new GreetJob { Name = "root" };
        var localized = new GreetJob { Id = root.Id, Name = "localized" };
        await root.Job.Submit();
        using (EntityContext.Partition("fr")) await localized.Job.Submit();

        var records = await host.Ledger.Query(new JobQuery(WorkType: typeof(GreetJob).FullName, WorkId: root.Id), default);
        records.Should().HaveCount(2, "all scheduling rows must be visible to the central worker");
        using (EntityContext.Partition("unrelated")) await host.Drain();

        (await host.Ledger.Query(new JobQuery(WorkId: root.Id), default))
            .Should().OnlyContain(record => record.Status == JobStatus.Completed);

        (await GreetJob.Get(root.Id))!.Greeting.Should().Be("Hello, root");
        using (EntityContext.Partition("fr"))
        {
            (await GreetJob.Get(root.Id))!.Greeting.Should().Be("Hello, localized");
            (await localized.Job.Status()).Should().Be(JobStatus.Completed);
            (await JobRecord.Query(r => r.WorkId == root.Id)).Should().BeEmpty();
        }
        (await host.Ledger.Query(new JobQuery(WorkId: root.Id), default))
            .Should().OnlyContain(record => record.Status == JobStatus.Completed);
    }

    [Fact]
    public async Task discovery_binds_job_types()
    {
        await using var host = await CreateHostAsync();
        host.Registry.Count.Should().BeGreaterThan(0);
        host.Registry.Get(typeof(GreetJob).FullName!).Should().NotBeNull();
    }

    [Fact]
    public async Task retired_work_types_are_dead_lettered_without_blocking_valid_work()
    {
        GreetJob.Reset();
        await using var host = await CreateHostAsync();
        var now = host.Clock.GetUtcNow();
        const string retiredJobId = "retired-work-type";
        // Older than the work behind it, so the claim order is the one this spec is about rather than an
        // accident. The subject is head-of-line blocking: a retired row the conveyor reaches first must be
        // settled and stepped over, not left to stall the lane. Seeding it at the same instant as the valid
        // job left the two tied on (VisibleAt, FirstSubmittedAt) and the winner unspecified, which is what
        // made this spec fail on one store and then another as tie-breaking changed underneath it.
        var earlier = now - TimeSpan.FromMinutes(1);
        await host.Ledger.Append(new JobRecord
        {
            Id = retiredJobId,
            WorkType = "Example.RetiredWork",
            WorkId = "old-work",
            Action = "run",
            Status = JobStatus.Queued,
            Lane = "default",
            VisibleAt = earlier,
            FirstSubmittedAt = earlier
        }, TestContext.Current.CancellationToken);

        var valid = new GreetJob { Name = "still-runs" };
        await valid.Job.Submit();
        var run = await host.Driver.RunOneAsync(TestContext.Current.CancellationToken);

        var retired = await host.Ledger.Get(retiredJobId, TestContext.Current.CancellationToken);
        retired.Should().NotBeNull();
        retired!.Status.Should().Be(JobStatus.Dead);
        retired.DeadReason.Should().Be(nameof(DeadReason.UnregisteredWorkType));
        retired.Owner.Should().BeNull();
        retired.LeaseUntil.Should().BeNull();
        retired.Attempt.Should().Be(1);
        retired.Transitions.Should().ContainSingle(value => value.To == JobStatus.Dead);
        run.Should().NotBeNull("one-stage execution must continue past retired durable rows");
        run!.WorkType.Should().Be(typeof(GreetJob).FullName);
        GreetJob.Executions.Should().Be(1, "retired durable rows must not make a drain look empty");
    }

    [Fact]
    public async Task single_action_runs_and_mutates_work_item()
    {
        GreetJob.Reset();
        await using var host = await CreateHostAsync();
        var j = new GreetJob { Name = "Koan" };
        var id = j.Id;
        await j.Job.Submit();
        await host.Drain();
        GreetJob.Executions.Should().Be(1);
        (await GreetJob.Get(id))!.Greeting.Should().Be("Hello, Koan");
        (await host.StatusOf<GreetJob>(id)).Should().Be(JobStatus.Completed);
    }

    [Fact]
    public async Task terminal_settlement_preserves_reported_progress()
    {
        await using var host = await CreateHostAsync();
        var work = new ProgressJob();

        await work.Job.Submit();
        await host.Drain();

        var record = await host.JobFor<ProgressJob>(work.Id);
        record.Should().NotBeNull();
        record!.Status.Should().Be(JobStatus.Completed);
        record.ProgressFraction.Should().Be(0.75);
        record.ProgressMessage.Should().Be("three quarters");
    }

    [Fact]
    public async Task finite_source_submit_runs_every_work_item()
    {
        GreetJob.Reset();
        await using var host = await CreateHostAsync();
        var jobs = Enumerable.Range(0, 25).Select(i => new GreetJob { Name = $"n{i}" }).ToList();
        var submission = await jobs.Submit();
        submission.Enumerated.Should().Be(25);
        submission.Accepted.Should().Be(25);
        submission.Submitted.Should().Be(25);
        submission.Coalesced.Should().Be(0);
        submission.Failed.Should().Be(0);
        submission.SourceCompleted.Should().BeTrue();
        submission.PendingCommit.Should().BeFalse();
        await host.Drain();
        GreetJob.Executions.Should().Be(25);
    }

    [Fact]
    public async Task idempotent_submit_coalesces_duplicates()
    {
        // JOBS-0010: a duplicate submission of one work item coalesces; another item with the same key gets its own job.
        DedupeJob.Reset();
        await using var host = await CreateHostAsync();
        var item = new DedupeJob { Key = "k1" };
        await item.Job.Submit();
        await item.Job.Submit();
        await new DedupeJob { Key = "k1" }.Job.Submit();
        await new DedupeJob { Key = "k2" }.Job.Submit();
        await host.Drain();
        DedupeJob.Executions.Should().Be(3);
    }

    [Fact]
    public async Task concurrent_duplicate_submits_coalesce()
    {
        // JOBS-0010: the coalescing check and the append are one conditional insert, so duplicates submitted at the same
        // moment coalesce onto one job instead of each missing the other.
        DedupeJob.Reset();
        await using var host = await CreateHostAsync();
        var id = new DedupeJob { Key = "burst" }.Id;   // minted once: an Entity id is minted on first read
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var submits = Enumerable.Range(0, 24).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            return await new DedupeJob { Id = id, Key = "burst" }.Job.Submit();   // one item, from many requests
        })).ToList();
        start.SetResult();
        var handles = await Task.WhenAll(submits);

        handles.Select(h => h.JobId).Distinct().Should().ContainSingle("every duplicate coalesces onto one job");
        (await host.Ledger.Query(new JobQuery(WorkType: typeof(DedupeJob).FullName!), default)).Should().ContainSingle();
        await host.Drain();
        DedupeJob.Executions.Should().Be(1);
    }

    [Fact]
    public async Task a_deferred_idempotent_job_takes_a_duplicate_and_runs_after_its_deferral()
    {
        // JOBS-0006 / JOBS-0010: a duplicate submitted while a job of its key waits in the queue coalesces onto it instead
        // of queuing beside it and running at once.
        DeferringJob.Reset();
        await using var host = await CreateHostAsync();
        var item = new DeferringJob { Key = "deferred" };
        await item.Job.Submit();
        await host.Drain();                                   // runs once and defers by five minutes
        await item.Job.Submit();
        await host.Drain();

        DeferringJob.Executions.Should().Be(1, "the duplicate did not bypass the deferral");
        (await QueuedOf<DeferringJob>(host)).Should().ContainSingle();

        host.Advance(DeferringJob.Deferral + TimeSpan.FromSeconds(1));
        await host.Drain();
        DeferringJob.Executions.Should().Be(2);
        (await QueuedOf<DeferringJob>(host)).Should().BeEmpty();
    }

    [Fact]
    public async Task duplicates_submitted_while_their_job_is_claimed_append_or_coalesce_without_failing()
    {
        // JOBS-0010: a duplicate whose append meets a slot its holder gives back a moment later appends again; a submit
        // never fails on a coalescing race.
        DedupeJob.Reset();
        await using var host = await CreateHostAsync();
        var id = new DedupeJob { Key = "claimed-while-submitted" }.Id;   // minted once: an Entity id is minted on first read
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var submits = Enumerable.Range(0, 24).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            return await new DedupeJob { Id = id, Key = "claimed-while-submitted" }.Job.Submit();
        })).ToList();
        var drains = Enumerable.Range(0, 4).Select(_ => Task.Run(async () => { await start.Task; await host.Drain(); })).ToList();
        start.SetResult();

        var handles = await Task.WhenAll(submits);
        await Task.WhenAll(drains);
        await host.Drain();

        handles.Should().OnlyContain(h => !string.IsNullOrEmpty(h.JobId));
        DedupeJob.Executions.Should().BeGreaterThan(0);
        (await QueuedOf<DedupeJob>(host)).Should().BeEmpty();
    }

    private static async Task<IReadOnlyList<JobRecord>> QueuedOf<T>(JobsHarness host) where T : Koan.Data.Core.Model.Entity<T>, IKoanJob<T>
        => await host.Ledger.Query(new JobQuery(WorkType: typeof(T).FullName!, Status: JobStatus.Queued), default);

    [Fact]
    public async Task coalesced_submit_does_not_save_work_item()
    {
        // A submission coalesced onto its item's queued job writes nothing — not the caller's copy of the item, not a
        // ledger row. Another item with the same key is never coalesced away: it is saved and gets its own job (JOBS-0010).
        DedupeJob.Reset();
        await using var host = await CreateHostAsync();

        var first = new DedupeJob { Key = "k1" };
        var job = await first.Job.Submit();

        var copy = new DedupeJob { Id = first.Id, Key = "k1", Note = "the caller's newer copy" };
        (await copy.Job.Submit()).JobId.Should().Be(job.JobId);
        (await DedupeJob.Get(first.Id))!.Note.Should().BeEmpty("a coalesced submit must not write the work item");

        var other = new DedupeJob { Key = "k1" };
        (await other.Job.Submit()).JobId.Should().NotBe(job.JobId, "coalescing never crosses work items");
        (await DedupeJob.Get(other.Id)).Should().NotBeNull();

        await host.Drain();
        DedupeJob.Executions.Should().Be(2);
    }

    [Fact]
    public async Task trailing_submit_during_run_queues_a_follow_up_execution()
    {
        // With queued-only coalescing, a submit arriving while a job is Running queues a trailing execution
        // rather than being silently absorbed. This gives the debounce / trailing-edge pattern.
        DedupeJob.Reset();
        await using var host = await CreateHostAsync();

        var first = new DedupeJob { Key = "trail" };
        await first.Job.Submit();

        // Advance the job to Running state to simulate in-flight execution.
        var now = host.Clock.GetUtcNow();
        _ = await host.Ledger.ClaimNext("test-claim-owner", now, now + TimeSpan.FromMinutes(1), Array.Empty<string>(), default);

        // A submit arriving while the job is Running must queue a follow-up record, not be silently absorbed.
        await first.Job.Submit();

        var records = await host.Ledger.Query(new JobQuery(WorkType: typeof(DedupeJob).FullName!), default);
        records.Should().HaveCount(2, "one Running + one Queued trailing-edge follow-up");
        records.Should().ContainSingle(r => r.Status == JobStatus.Running);
        records.Should().ContainSingle(r => r.Status == JobStatus.Queued, "trailing submit must create a follow-up record");
    }

    // --- chain ---

    [Fact]
    public async Task linear_chain_advances_and_carries_saga_state()
    {
        await using var host = await CreateHostAsync();
        var p = new Pipeline();
        var id = p.Id;
        await p.Job.Submit(Stage.Fetch);
        await host.Drain();
        var saved = await Pipeline.Get(id);
        saved!.Fetched.Should().Be("raw");
        saved.Parsed.Should().Be("raw-parsed");
        saved.Minted.Should().BeTrue();
        saved.Published.Should().BeTrue();
        saved.Trail.Should().Equal(Stage.Fetch, Stage.Parse, Stage.Mint, Stage.Publish);
    }

    [Fact]
    public async Task stop_chain_halts_after_current_step()
    {
        BranchJob.Reset();
        BranchJob.Mode = "stop";
        await using var host = await CreateHostAsync();
        var j = new BranchJob();
        var id = j.Id;
        await j.Job.Submit("a");
        await host.Drain();
        (await BranchJob.Get(id))!.Trail.Should().Equal("a");
    }

    [Fact]
    public async Task continue_with_branches_to_an_off_chain_action()
    {
        BranchJob.Reset();
        BranchJob.Mode = "branch";
        await using var host = await CreateHostAsync();
        var j = new BranchJob();
        var id = j.Id;
        await j.Job.Submit("a");
        await host.Drain();
        (await BranchJob.Get(id))!.Trail.Should().Equal("a", "z");
    }

    [Fact]
    public async Task on_failure_continue_advances_the_chain()
    {
        await using var host = await CreateHostAsync();
        var j = new ContinueChain();
        var id = j.Id;
        await j.Job.Submit(Step.One);
        await host.Drain();
        (await ContinueChain.Get(id))!.TwoRan.Should().BeTrue();
    }

    [Fact]
    public async Task on_failure_abort_stops_the_chain()
    {
        await using var host = await CreateHostAsync();
        var j = new AbortChain();
        var id = j.Id;
        await j.Job.Submit(Step.One);
        await host.Drain();
        (await AbortChain.Get(id))!.TwoRan.Should().BeFalse();
    }

    // --- cooperative backoff ---

    [Fact]
    public async Task reschedule_defers_without_consuming_an_attempt()
    {
        RescheduleJob.Reset();
        RescheduleJob.RescheduleUntil = 2;
        await using var host = await CreateHostAsync();
        var j = new RescheduleJob();
        var id = j.Id;
        await j.Job.Submit();
        await host.Drain();
        RescheduleJob.Executions.Should().Be(1);
        await host.Drain();
        RescheduleJob.Executions.Should().Be(1);
        host.Advance(TimeSpan.FromMinutes(5)); await host.Drain();
        host.Advance(TimeSpan.FromMinutes(5)); await host.Drain();
        RescheduleJob.Executions.Should().Be(3);
        var rec = await host.JobFor<RescheduleJob>(id);
        rec!.Status.Should().Be(JobStatus.Completed);
        rec.Reschedules.Should().Be(2);
        rec.Attempt.Should().Be(1);
    }

    [Fact]
    public async Task max_reschedules_dead_letters()
    {
        RescheduleJob.Reset();
        RescheduleJob.RescheduleUntil = 99;
        await using var host = await CreateHostAsync(o => o.DefaultMaxReschedules = 1);
        var j = new RescheduleJob();
        var id = j.Id;
        await j.Job.Submit();
        await host.Drain();
        host.Advance(TimeSpan.FromMinutes(5)); await host.Drain();
        var rec = await host.JobFor<RescheduleJob>(id);
        rec!.Status.Should().Be(JobStatus.Dead);
        rec.DeadReason.Should().Be(nameof(DeadReason.PerpetuallyDeferred));
    }

    [Fact]
    public async Task backoff_gate_defers_peers_at_dispatch_without_running()
    {
        GatedJob.Reset();
        GatedJob.Trip429 = true;
        await using var host = await CreateHostAsync();
        var a = new GatedJob { Host = "api" };
        var b = new GatedJob { Host = "api" };
        var c = new GatedJob { Host = "api" };
        await a.Job.Submit();
        await host.Drain();
        GatedJob.Executions.Should().Be(1);
        await b.Job.Submit();
        await c.Job.Submit();
        await host.Drain();
        GatedJob.Executions.Should().Be(1);
        host.Advance(TimeSpan.FromMinutes(5));
        await host.Drain();
        (await host.StatusOf<GatedJob>(a.Id)).Should().Be(JobStatus.Completed);
        (await host.StatusOf<GatedJob>(b.Id)).Should().Be(JobStatus.Completed);
        (await host.StatusOf<GatedJob>(c.Id)).Should().Be(JobStatus.Completed);
    }

    [Fact]
    public async Task state_gatekey_is_null_for_an_ungated_job()
    {
        GreetJob.Reset();
        await using var host = await CreateHostAsync();
        var j = new GreetJob { Name = "ungated" };
        await j.Job.Submit();
        await host.Drain();
        GreetJob.Executions.Should().Be(1);
        GreetJob.LastGateKey.Should().BeNull("an ungated job claims no gate");
    }

    [Fact]
    public async Task claimed_gate_state_survives_a_persisted_routing_change()
    {
        // The snapshot's GateKey is the key captured with THIS claim, not a re-resolution of the work item's
        // current routing: even after the TEST changes and persists the routing between attempts, both claims
        // retain the gate recorded on the ledger row at submit. (Deferral performs no work-item auto-save; the
        // routing change is written by the test, not the handler.)
        GatedJob.Reset();
        await using var host = await CreateHostAsync();
        var j = new GatedJob { Host = "api-a" };
        await j.Job.Submit();

        GatedJob.Trip429 = true;   // attempt 1 takes the existing backoff deferral path
        await host.Drain();

        var stored = await GatedJob.Get(j.Id);
        stored!.Host = "api-b";
        await GatedJob.Upsert(stored);

        host.Advance(TimeSpan.FromMinutes(5));
        await host.Drain();

        (await GatedJob.Get(j.Id))!.Host.Should().Be("api-b", "the work item's routing actually changed on disk");
        (await host.StatusOf<GatedJob>(j.Id)).Should().Be(JobStatus.Completed);
        GatedJob.ClaimedGates.Where(o => o.WorkId == j.Id).Select(o => o.GateKey)
            .Should().Equal(new[] { "api-a", "api-a" },
                "both claims project the gate captured with the claim, not the work item's new routing");
    }

    [Fact]
    public async Task backoff_override_gates_peers_while_the_snapshot_keeps_the_claimed_gate()
    {
        // An explicit Backoff key redirects the shared gate WITHOUT rewriting the claimed row: peers of the
        // override key defer at dispatch, while every execution of the original job still sees its own gate.
        GatedJob.Reset();
        await using var host = await CreateHostAsync();
        var a = new GatedJob { Host = "api" };
        var b = new GatedJob { Host = "manual:hot-key" };   // its declared gate IS the override key
        await a.Job.Submit();

        GatedJob.Trip429 = true;                  // attempt 1 takes the 429 path
        GatedJob.OverrideKey = "manual:hot-key";  // and backs off the override key instead of "api"
        await host.Drain();

        await b.Job.Submit();
        await host.Drain();

        (await host.StatusOf<GatedJob>(b.Id)).Should().Be(JobStatus.Queued, "the override gate defers peers at dispatch");

        host.Advance(TimeSpan.FromMinutes(5));
        await host.Drain();

        (await host.StatusOf<GatedJob>(a.Id)).Should().Be(JobStatus.Completed);
        (await host.StatusOf<GatedJob>(b.Id)).Should().Be(JobStatus.Completed);
        GatedJob.ClaimedGates.Where(o => o.WorkId == a.Id).Select(o => o.GateKey)
            .Should().Equal(new[] { "api", "api" },
                "both claims project the claimed gate, even after Backoff(override) ran in the first execution");
        GatedJob.ClaimedGates.Single(o => o.WorkId == b.Id).GateKey.Should().Be("manual:hot-key",
            "the peer's snapshot is its own declared gate, not the shared gate state");
    }

    // --- retry / cancel / timeout ---

    [Fact]
    public async Task retries_then_succeeds()
    {
        FlakyJob.Reset();
        FlakyJob.SucceedAtAttempt = 3;
        await using var host = await CreateHostAsync();
        var job = new FlakyJob();
        var id = job.Id;
        await job.Job.Submit(FlakyJob.Action);
        await host.Drain();
        host.Advance(TimeSpan.FromMinutes(1)); await host.Drain();
        host.Advance(TimeSpan.FromMinutes(1)); await host.Drain();
        FlakyJob.Executions.Should().Be(3);
        (await host.StatusOf<FlakyJob>(id)).Should().Be(JobStatus.Completed);
    }

    [Fact]
    public async Task poison_fails_after_exhausting_retries()
    {
        FlakyJob.Reset();
        FlakyJob.SucceedAtAttempt = 99;
        await using var host = await CreateHostAsync();
        var job = new FlakyJob();
        var id = job.Id;
        await job.Job.Submit(FlakyJob.Action);
        for (var i = 0; i < 3; i++) { await host.Drain(); host.Advance(TimeSpan.FromMinutes(1)); }
        FlakyJob.Executions.Should().Be(3);
        var rec = await host.JobFor<FlakyJob>(id);
        rec!.Status.Should().Be(JobStatus.Failed);
        rec.DeadReason.Should().Be(nameof(DeadReason.Poison));
    }

    [Fact]
    public async Task cancel_queued_job_never_runs()
    {
        GreetJob.Reset();
        await using var host = await CreateHostAsync();
        var job = new GreetJob { Name = "x" };
        var id = job.Id;
        await job.Job.Submit();
        await job.Job.Cancel();
        (await host.StatusOf<GreetJob>(id)).Should().Be(JobStatus.Cancelled);
        await host.Drain();
        GreetJob.Executions.Should().Be(0);
    }

    [Fact]
    public async Task cancel_running_job_cooperatively()
    {
        WaitJob.Reset();
        await using var host = await CreateHostAsync();
        var job = new WaitJob();
        var id = job.Id;
        await job.Job.Submit(WaitJob.Action);
        var drain = host.Drain();
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
        await using var host = await CreateHostAsync();
        var job = new WaitJob();
        var id = job.Id;
        await job.Job.Submit(WaitJob.Action);
        var drain = host.Drain();
        await Wait.Until(() => WaitJob.Executions == 1);
        host.Advance(TimeSpan.FromSeconds(31));
        await drain;
        (await host.StatusOf<WaitJob>(id)).Should().Be(JobStatus.Failed);
        WaitJob.Cancellations.Should().Be(1);
    }

    [Fact]
    public async Task cancel_at_settle_window_stops_chain()
    {
        // Regression: a cancel request landing between handler completion and the chain-record append must
        // prevent the chain from advancing. The handler deliberately ignores the cancellation token to reproduce
        // the settle-time window where Execute already returned but SettleSuccessAsync hasn't appended the next.
        ChainCancelRaceJob.Reset();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ChainCancelRaceJob.Unblock = tcs;
        await using var host = await CreateHostAsync();

        var j = new ChainCancelRaceJob();
        var id = j.Id;
        await j.Job.Submit("a");

        var drain = host.Drain();
        await Wait.Until(() => ChainCancelRaceJob.StepAExecutions == 1);  // handler has entered Execute
        await host.Coordinator.CancelWorkAsync(typeof(ChainCancelRaceJob).FullName!, id, default);
        tcs.SetResult();   // let the handler return (it ignores ct — models the settle-time race)
        await drain;

        (await ChainCancelRaceJob.Get(id))?.StepBRan.Should().BeFalse(
            "a cancel landing in the settle window must stop the chain from advancing");
    }

    // --- lanes ---

    [Fact]
    public async Task lane_caps_concurrency()
    {
        SlowJob.Reset();
        await using var host = await CreateHostAsync();
        var jobs = Enumerable.Range(0, 6).Select(_ => new SlowJob()).ToList();
        await jobs.Submit(SlowJob.Action);
        await host.Drain();
        SlowJob.Peak.Should().BeLessThanOrEqualTo(2);
        var done = await host.Coordinator.WhereAsync(new JobQuery(WorkType: typeof(SlowJob).FullName!, Status: JobStatus.Completed), default);
        done.Should().HaveCount(6);
    }

    [Fact]
    public async Task worker_concurrency_caps_total_in_flight()
    {
        ExclusiveJob.Reset();
        await using var host = await CreateHostAsync(o =>
        {
            o.WorkerConcurrency = 2;
            o.DefaultMaxConcurrency = 8;
        });
        // 8 distinct entities — no per-entity serialization constraint — only WorkerConcurrency limits throughput.
        var jobs = Enumerable.Range(0, 8).Select(_ => new ExclusiveJob()).ToList();
        await jobs.Submit(ExclusiveJob.ActionA);
        await host.Drain();
        ExclusiveJob.Peak.Should().BeLessThanOrEqualTo(2, "WorkerConcurrency caps total in-flight concurrency");
        (await host.Coordinator.WhereAsync(new JobQuery(WorkType: typeof(ExclusiveJob).FullName!, Status: JobStatus.Completed), default))
            .Should().HaveCount(8);
    }

    // --- scheduling (initiator submits on cadence) ---

    [Fact]
    public async Task scheduled_action_runs_when_the_scheduler_ticks()
    {
        Reconciled.Reset();
        await using var host = await CreateHostAsync();
        await host.Drain();
        Reconciled.Executions.Should().Be(0);
        await host.TriggerDue();
        await host.Drain();
        Reconciled.Executions.Should().Be(1);
    }

    [Fact]
    public async Task recurring_schedule_actually_recurs()
    {
        Reconciled.Reset();
        await using var host = await CreateHostAsync();
        await host.TriggerDue(); await host.Drain();
        host.Advance(TimeSpan.FromMinutes(10)); await host.TriggerDue(); await host.Drain();
        host.Advance(TimeSpan.FromMinutes(10)); await host.TriggerDue(); await host.Drain();
        Reconciled.Executions.Should().Be(3);
    }

    [Fact]
    public async Task schedule_respects_its_interval()
    {
        Reconciled.Reset();
        await using var host = await CreateHostAsync();
        await host.TriggerDue(); await host.Drain();
        Reconciled.Executions.Should().Be(1);
        await host.TriggerDue(); await host.Drain();
        Reconciled.Executions.Should().Be(1);
        host.Advance(TimeSpan.FromMinutes(10));
        await host.TriggerDue(); await host.Drain();
        Reconciled.Executions.Should().Be(2);
    }

    [Fact]
    public async Task continuous_schedule_re_arms_after_each_settled_run()
    {
        Heartbeat.Reset();
        await using var host = await CreateHostAsync();
        var poll = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<JobsOptions>>().Value.PollInterval;
        await host.TriggerDue(); await host.Drain();
        host.Advance(poll); await host.TriggerDue(); await host.Drain();
        host.Advance(poll); await host.TriggerDue(); await host.Drain();
        Heartbeat.Executions.Should().Be(3);
    }

    [Fact]
    public async Task continuous_pump_writes_one_row_per_settled_run()
    {
        // JOBS-0010: @continuous re-arms only after its previous run settled, and no sooner than one PollInterval
        // after its previous fire. Ticks that arrive while a run is in flight write nothing.
        Heartbeat.Reset();
        await using var host = await CreateHostAsync();
        var poll = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<JobsOptions>>().Value.PollInterval;
        var workType = typeof(Heartbeat).FullName!;

        for (var tick = 0; tick < 10; tick++) { await host.TriggerDue(); host.Advance(poll); }
        (await host.Ledger.Query(new JobQuery(WorkType: workType), default))
            .Should().ContainSingle("ten ticks over one unsettled run write one row");

        await host.Drain();
        await host.TriggerDue();                 // settled, and a poll interval has passed since the fire
        await host.TriggerDue();                 // the same instant: the floor holds
        await host.Drain();
        host.Advance(poll);
        await host.TriggerDue();
        await host.Drain();

        Heartbeat.Executions.Should().Be(3);
        (await host.Ledger.Query(new JobQuery(WorkType: workType), default))
            .Should().HaveCount(3, "one row per settled run");
    }

    [Fact]
    public async Task cron_schedule_fires_at_its_occurrence_and_recurs_daily()
    {
        NightlyJob.Reset();
        await using var host = await CreateHostAsync();
        await host.TriggerDue(); await host.Drain();
        NightlyJob.Executions.Should().Be(0);
        host.Advance(TimeSpan.FromHours(3));
        await host.TriggerDue(); await host.Drain();
        NightlyJob.Executions.Should().Be(1);
        host.Advance(TimeSpan.FromHours(1));
        await host.TriggerDue(); await host.Drain();
        NightlyJob.Executions.Should().Be(1);
        host.Advance(TimeSpan.FromHours(23));
        await host.TriggerDue(); await host.Drain();
        NightlyJob.Executions.Should().Be(2);
    }

    [Fact]
    public async Task boot_action_runs_once_at_boot()
    {
        BootOnce.Reset();
        await using var host = await CreateHostAsync();
        await host.Boot();
        await host.Drain();
        BootOnce.Executions.Should().Be(1);
    }

    [Fact]
    public async Task reaper_reclaims_a_lapsed_lease()
    {
        GreetJob.Reset();
        await using var host = await CreateHostAsync();
        var g = new GreetJob { Name = "recovered" };
        var id = g.Id;
        await GreetJob.Upsert(g);
        var now = host.Clock.GetUtcNow();
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
            LeaseUntil = now - TimeSpan.FromMinutes(1),
            Owner = "dead-worker",
        }, default);
        await host.Reap();
        await host.Drain();
        GreetJob.Executions.Should().Be(1);
        (await host.StatusOf<GreetJob>(id)).Should().Be(JobStatus.Completed);
    }

    [Fact]
    public async Task facade_query_filters_converge_across_tiers()
    {
        // PMC-060: the JobQuery surface is a filter-only, unordered contract identical on every tier — no
        // sort/paging axis exists to diverge. These asserts pin the semantics: same filters, same record set,
        // regardless of dictionary insertion order (in-memory) or store-side layout (durable).
        GreetJob.Reset();
        await using var host = await CreateHostAsync();
        var ids = new List<string>();
        foreach (var (name, status) in new[] { ("a", JobStatus.Queued), ("b", JobStatus.Queued), ("c", JobStatus.Completed) })
        {
            var g = new GreetJob { Name = name };
            ids.Add(g.Id);
            await GreetJob.Upsert(g);
            var now = host.Clock.GetUtcNow();
            await host.Ledger.Append(new JobRecord
            {
                WorkType = typeof(GreetJob).FullName!,
                WorkId = g.Id,
                Action = "",
                Status = status,
                Attempt = status == JobStatus.Completed ? 1 : 0,
                Lane = "default",
                FirstSubmittedAt = now,
                VisibleAt = now,
                LastSettledAt = status == JobStatus.Completed ? now : null,
            }, default);
        }
        var workType = typeof(GreetJob).FullName!;

        var all = await host.Ledger.Query(new JobQuery(WorkType: workType), default);
        all.Select(r => r.WorkId).OrderBy(x => x).Should().Equal(ids.OrderBy(x => x),
            "the wildcard-axis query returns the whole filtered set, unordered");

        var queued = await host.Ledger.Query(new JobQuery(WorkType: workType, Status: JobStatus.Queued), default);
        queued.Select(r => r.WorkId).OrderBy(x => x).Should().HaveCount(2);

        var single = await host.Ledger.Query(new JobQuery(WorkType: workType, WorkId: ids[0]), default);
        single.Should().ContainSingle(r => r.WorkId == ids[0]);
    }

    [Fact]
    public async Task claim_honors_reservations_on_every_tier()
    {
        // PMC-056: the reservation eligibility clause converges across tiers (ARCH-0079) — a fresh foreign
        // cookie hides the row, restamping is refused, and the named hand claims through the normal path.
        GreetJob.Reset();
        await using var host = await CreateHostAsync();
        var g = new GreetJob { Name = "reserved" };
        var id = g.Id;
        await GreetJob.Upsert(g);
        var now = host.Clock.GetUtcNow();
        var record = new JobRecord
        {
            WorkType = typeof(GreetJob).FullName!,
            WorkId = id,
            Action = "",
            Status = JobStatus.Queued,
            Lane = "default",
            FirstSubmittedAt = now,
            VisibleAt = now,
        };
        await host.Ledger.Append(record, default);
        var jobId = (await host.JobFor<GreetJob>(id))!.Id;

        (await host.Ledger.TryReserve(jobId, "peer-hand", now + TimeSpan.FromMinutes(5), now, default)).Should().BeTrue();

        (await host.Ledger.ClaimNext("me", now, now + TimeSpan.FromMinutes(1), Array.Empty<string>(), default))
            .Should().BeNull("a fresh foreign reservation hides the row from another hand");
        (await host.Ledger.TryReserve(jobId, "second-hand", now + TimeSpan.FromMinutes(5), now, default))
            .Should().BeFalse("a row already carrying a fresh reservation cannot be re-stamped");

        var claimed = await host.Ledger.ClaimNext("peer-hand", now, now + TimeSpan.FromMinutes(1), Array.Empty<string>(), default);
        claimed.Should().NotBeNull();
        claimed!.Owner.Should().Be("peer-hand");
    }

    [Fact]
    public async Task a_running_job_renews_its_lease_and_outlives_the_original_window()
    {
        // JOBS-0009: a live handler holds its lease past the original window; the reaper never sees it lapse and
        // no second claim mints Attempt=2.
        WaitJob.Reset();
        await using var host = await CreateHostAsync(o => o.LeaseDuration = TimeSpan.FromSeconds(2));
        var job = new WaitJob();
        var id = job.Id;
        var start = host.Clock.GetUtcNow();
        await job.Job.Submit(WaitJob.Action);
        var drain = host.Drain();
        await Wait.Until(() => WaitJob.Executions == 1);

        for (var i = 0; i < 5; i++)   // 3.5s wall-of-fake-time ≫ the 2s lease; each step fires a renewal tick
            host.Advance(TimeSpan.FromMilliseconds(700));

        DateTimeOffset renewedUntil = default;
        await Wait.Until(() =>
        {
            var r = host.JobFor<WaitJob>(id).GetAwaiter().GetResult();
            renewedUntil = r?.LeaseUntil ?? default;
            return renewedUntil > start + TimeSpan.FromSeconds(2);
        });
        (await host.JobFor<WaitJob>(id))!.Status.Should().Be(JobStatus.Running);
        (await host.JobFor<WaitJob>(id))!.Attempt.Should().Be(1);

        await host.Coordinator.CancelWorkAsync(typeof(WaitJob).FullName!, id, default);
        await drain;
        (await host.StatusOf<WaitJob>(id)).Should().Be(JobStatus.Cancelled);
        (await host.JobFor<WaitJob>(id))!.Attempt.Should().Be(1);
        renewedUntil.Should().BeOnOrAfter(start + TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task losing_the_lease_abandons_without_settling()
    {
        // JOBS-0009: when another claimant takes the row mid-run, the loser cancels its handler and writes
        // NOTHING — the winner's ownership and state survive untouched.
        WaitJob.Reset();
        await using var host = await CreateHostAsync(o => o.LeaseDuration = TimeSpan.FromSeconds(2));
        var job = new WaitJob();
        var id = job.Id;
        await job.Job.Submit(WaitJob.Action);
        var drain = host.Drain();
        await Wait.Until(() => WaitJob.Executions == 1);

        var stolen = await host.JobFor<WaitJob>(id);
        stolen!.Owner = "node-b";
        stolen.LeaseUntil = host.Clock.GetUtcNow() + TimeSpan.FromMinutes(5);
        (await host.Ledger.Update(stolen, default)).Should().BeTrue();

        host.Advance(TimeSpan.FromMilliseconds(700));   // next renewal tick discovers the foreign owner
        await Wait.Until(() => WaitJob.Cancellations >= 1);
        await drain;

        var final = await host.JobFor<WaitJob>(id);
        final!.Owner.Should().Be("node-b");
        final.Status.Should().Be(JobStatus.Running);
        final.Attempt.Should().Be(1);
    }

    // --- type-level trigger ---

    [Fact]
    public async Task trigger_runs_a_type_level_action_without_an_instance()
    {
        TickJob.Reset();
        await using var host = await CreateHostAsync();
        await TickJob.Jobs.Trigger("sweep");
        await host.Drain();
        TickJob.Executions.Should().Be(1);
        TickJob.LastAction.Should().Be("sweep");
    }

    [Fact]
    public async Task overlapping_triggers_coalesce_on_an_idempotent_singleton()
    {
        SweepTick.Reset();
        await using var host = await CreateHostAsync();
        await SweepTick.Jobs.Trigger("sweep");
        await SweepTick.Jobs.Trigger("sweep");
        await host.Drain();
        SweepTick.Executions.Should().Be(1);
    }

    [Fact]
    public async Task trigger_does_not_persist_scheduler_singleton()
    {
        // TriggerAsync must not write a __koan_job_singleton__ entity row into the consumer's collection.
        TickJob.Reset();
        await using var host = await CreateHostAsync();
        await TickJob.Jobs.Trigger("sweep");
        await host.Drain();
        TickJob.Executions.Should().Be(1);
        (await TickJob.Get(Koan.Jobs.Infrastructure.Constants.Work.SingletonId)).Should().BeNull(
            "the scheduler singleton must not be persisted to the consumer's entity collection");
    }

    // --- archival ---

    [Fact]
    public async Task archival_purges_completed_past_retention_keeps_failed()
    {
        GreetJob.Reset();
        FlakyJob.Reset();
        FlakyJob.SucceedAtAttempt = 99;
        await using var host = await CreateHostAsync(o => o.ArchiveAfter = TimeSpan.FromHours(1));

        var ok = new GreetJob { Name = "x" };
        var okId = ok.Id;
        await ok.Job.Submit();
        await host.Drain();

        var bad = new FlakyJob();
        var badId = bad.Id;
        await bad.Job.Submit(FlakyJob.Action);
        for (var i = 0; i < 3; i++) { await host.Drain(); host.Advance(TimeSpan.FromMinutes(1)); }
        (await host.StatusOf<FlakyJob>(badId)).Should().Be(JobStatus.Failed);

        host.Advance(TimeSpan.FromHours(2));
        await host.Archive();

        (await host.StatusOf<GreetJob>(okId)).Should().BeNull("completed past retention is purged");
        (await host.StatusOf<FlakyJob>(badId)).Should().Be(JobStatus.Failed, "failed is retained");
    }

    [Fact]
    public async Task released_gates_are_purged_by_the_archival_sweep()
    {
        // JOBS-0010: one row per gate key, and the archival sweep removes a gate once it has released.
        await using var host = await CreateHostAsync();
        var now = host.Clock.GetUtcNow();
        await host.Ledger.SetGate("api:released", now + TimeSpan.FromMinutes(5), "429", default);
        await host.Ledger.SetGate("api:held", now + TimeSpan.FromHours(2), "429", default);
        await host.Ledger.SetGate("api:released", now + TimeSpan.FromMinutes(1), "an earlier release never shortens", default);

        var gates = await host.Ledger.ActiveGates(DateTimeOffset.MinValue, default);
        gates.Should().HaveCount(2, "one gate key is one row");
        gates.Single(g => g.GateKey == "api:released").ReleaseAt.Should().Be(now + TimeSpan.FromMinutes(5));

        host.Advance(TimeSpan.FromMinutes(10));
        await host.Archive();

        (await host.Ledger.ActiveGates(DateTimeOffset.MinValue, default))
            .Should().ContainSingle().Which.GateKey.Should().Be("api:held");
    }

    // --- work-item write safety (§17) ---

    [Fact]
    public async Task autosave_persists_a_mutation_to_the_passed_reference()
    {
        GreetJob.Reset();
        await using var host = await CreateHostAsync();
        var g = new GreetJob { Name = "Ada" };
        var id = g.Id;
        await g.Job.Submit();
        await host.Drain();
        (await GreetJob.Get(id))!.Greeting.Should().Be("Hello, Ada", "mutating the passed reference persists (the 80% path)");
    }

    [Fact]
    public async Task conditional_autosave_does_not_clobber_a_handlers_own_write()
    {
        await using var host = await CreateHostAsync();
        var j = new SelfSaveJob();
        var id = j.Id;
        await j.Job.Submit();
        await host.Drain();
        // The handler saved its own copy and never touched the passed reference; the framework must not overwrite it.
        (await SelfSaveJob.Get(id))!.Result.Should().Be(SelfSaveJob.Written);
    }

    [Fact]
    public async Task submit_never_overwrites_a_stored_work_item()
    {
        // JOBS-0010: the caller's copy was read before another writer committed; submitting it must not revert that write.
        GreetJob.Reset();
        await using var host = await CreateHostAsync();
        var stored = new GreetJob { Name = "renamed by another writer" };
        await GreetJob.Upsert(stored);
        var callersCopy = new GreetJob { Id = stored.Id, Name = "the caller's stale copy" };

        await callersCopy.Job.Submit();
        (await GreetJob.Get(stored.Id))!.Name.Should().Be("renamed by another writer");

        await host.Drain();
        (await GreetJob.Get(stored.Id))!.Greeting.Should().Be("Hello, renamed by another writer");
    }

    [Fact]
    public async Task a_change_made_while_the_job_runs_survives_settle()
    {
        // JOBS-0010: settle writes only what the handler changed, onto the row as stored at settle.
        await using var host = await CreateHostAsync();
        var item = new MergeJob();
        await item.Job.Submit();
        await host.Drain();

        var stored = await MergeJob.Get(item.Id);
        stored!.Note.Should().Be(MergeJob.WrittenMeanwhile, "the other writer's change stands");
        stored.Result.Should().Be(MergeJob.Done, "the handler's change is written");
        (await host.StatusOf<MergeJob>(item.Id)).Should().Be(JobStatus.Completed);
    }

    [Fact]
    public async Task a_nested_record_the_handler_fills_is_written_back()
    {
        // JOBS-0010: a nested object the handler fills is one changed member, written back on every store.
        await using var host = await CreateHostAsync();
        var item = new ReceiptJob { Kind = ReceiptKind.Remote, Quantity = 3 };
        await item.Job.Submit();
        await host.Drain();

        (await host.StatusOf<ReceiptJob>(item.Id)).Should().Be(JobStatus.Completed);
        var stored = (await ReceiptJob.Get(item.Id))!;
        stored.Receipt.Should().NotBeNull();
        stored.Receipt!.Kind.Should().Be(ReceiptKind.Remote);
        stored.Receipt.Lines.Should().Equal("a", "b");
        stored.Receipt.Took.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task a_change_behind_a_non_public_setter_survives_settle()
    {
        // JOBS-0010: Koan Data persists state behind a non-public setter, so a settle compares and writes it too.
        StateJob.Reset();
        await using var host = await CreateHostAsync();
        var item = new StateJob();
        await item.Job.Submit();
        await host.Drain();

        (await StateJob.Get(item.Id))!.State.Should().Be("done");
        (await host.StatusOf<StateJob>(item.Id)).Should().Be(JobStatus.Completed);
    }

    [Fact]
    public async Task a_concurrent_change_behind_a_non_public_setter_is_a_conflict()
    {
        // JOBS-0010: the handler and another writer changed one member. Nothing is written, and the attempt fails: the job
        // runs again on the stored item.
        StateJob.Reset();
        StateJob.ConflictOnFirstRun = true;
        await using var host = await CreateHostAsync();
        var item = new StateJob();
        await item.Job.Submit();
        await host.Drain();

        (await StateJob.Get(item.Id))!.State.Should().Be("theirs", "the conflicting run wrote nothing");
        var retried = await host.JobFor<StateJob>(item.Id);
        retried!.Status.Should().Be(JobStatus.Queued);
        retried.LastError.Should().Contain("changed while the job ran").And.Contain(nameof(StateJob.State).ToLowerInvariant());

        host.Advance(TimeSpan.FromMinutes(1));
        await host.Drain();
        (await StateJob.Get(item.Id))!.State.Should().Be("done");
    }

    // --- claims, cancellations and chains under interleaving (JOBS-0010) ---

    /// <summary>Wait, in real time and bounded, for a condition a background execution brings about.</summary>
    private static async Task Until(Func<Task<bool>> condition, string what)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException(what);
            await Task.Delay(20);
        }
    }

    private static string Marker(JobRecord row) => row.CancelRequestedAt is { } at ? at.ToString("O") : "none";

    /// <summary>The in-memory ledger writes no Entity rows, so lifecycle hooks cannot step between its writes; each of its
    /// writes is taken whole under its lock.</summary>
    private static void SkipWithoutEntityRows(JobsHarness host)
    {
        if (host.Ledger is InMemoryJobLedger) Assert.Skip("this ledger writes no JobRecord rows; the interleaving cannot be stepped in");
    }

    [Fact]
    public async Task a_cancellation_a_renewal_carried_survives_the_owners_deferral()
    {
        // JOBS-0010: another node asks a running job to cancel. The owner's renewal merges its lease onto the row and keeps
        // the request; its deferral then settles the job Cancelled instead of requeueing it.
        HeldThenDeferredJob.Reset();
        await using var host = await CreateHostAsync();
        host.Advance(TimeSpan.FromTicks(1_234_567));
        var item = new HeldThenDeferredJob { Key = "laundered-" + Guid.NewGuid().ToString("N") };
        var handle = await item.Job.Submit();
        var draining = host.Drain();
        await HeldThenDeferredJob.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var row = (await host.Ledger.Get(handle.JobId, default))!;
        var lease = row.LeaseUntil;
        row.CancelRequestedAt = host.Clock.GetUtcNow();
        (await host.Ledger.Update(row, default)).Should().BeTrue("a cancellation from another node lands on the running row");
        host.Advance(TimeSpan.FromSeconds(21));
        // The renewal carries the request — and reading it, cancels the handler, which may settle at once.
        await Until(async () => (await host.Ledger.Get(handle.JobId, default)) is { } r && (r.LeaseUntil > lease || r.IsTerminal), "renewal");
        (await host.Ledger.Get(handle.JobId, default))!.CancelRequestedAt.Should().NotBeNull("the renewal carried the request");

        HeldThenDeferredJob.Release.TrySetResult();
        await draining.WaitAsync(TimeSpan.FromSeconds(30));
        host.Advance(HeldThenDeferredJob.Deferral + TimeSpan.FromSeconds(1));
        await host.Drain();
        var after = (await host.Ledger.Get(handle.JobId, default))!;
        (after.Status == JobStatus.Cancelled && HeldThenDeferredJob.Executions == 1).Should().BeTrue(
            "status=" + after.Status + ", marker=" + Marker(after) + ", executions=" + HeldThenDeferredJob.Executions);
    }

    [Fact]
    public async Task a_cancellation_that_meets_a_requeue_already_landed_still_cancels()
    {
        // JOBS-0010: the owner's requeue lands just before the canceller's request; the canceller reads the job again and
        // cancels it where it now is, in the queue.
        GreetJob.Reset();
        var interleave = new Interleave();
        await using var host = await CreateHostAsync(configureServices: interleave.Install);
        SkipWithoutEntityRows(host);
        var item = new GreetJob { Name = "requeued-first" };
        var handle = await item.Job.Submit();
        var now = host.Clock.GetUtcNow();
        var mine = (await host.Ledger.ClaimNext("owner-a", now, now + TimeSpan.FromMinutes(5), [], default))!;
        mine.Owner = null;
        mine.LeaseUntil = null;
        mine.LastError = "transient";
        mine.Status = JobStatus.Queued;
        mine.VisibleAt = now;
        JobRecord? requeued = null;
        interleave.Before(row => row.Id == handle.JobId && row.Status == JobStatus.Running && row.CancelRequestedAt != null,
            async () => requeued = await host.Ledger.Update(mine, default) ? mine : null);

        await host.Coordinator.CancelWorkAsync(typeof(GreetJob).FullName!, item.Id, default);
        await host.Drain();
        var after = (await host.Ledger.Get(handle.JobId, default))!;
        interleave.Fired.Should().Be(1);
        (after.Status == JobStatus.Cancelled && GreetJob.Executions == 0).Should().BeTrue(
            "requeued=" + requeued?.Status + ", status=" + after.Status + ", marker=" + Marker(after) + ", executions=" + GreetJob.Executions);
    }

    [Fact]
    public async Task a_cancellation_landing_on_the_completion_stops_the_chain()
    {
        // JOBS-0010: a cancellation lands while stage a's completion is being written. The completion is applied on the row
        // as stored, so it settles Completed with the request and its successor is cancelled.
        ChainCancelRaceJob.Reset();
        var interleave = new Interleave();
        await using var host = await CreateHostAsync(configureServices: interleave.Install);
        SkipWithoutEntityRows(host);
        var job = new ChainCancelRaceJob();
        interleave.Before(row => row.WorkId == job.Id && row.Action == "a" && row.Status == JobStatus.Completed,
            () => host.Coordinator.CancelWorkAsync(typeof(ChainCancelRaceJob).FullName!, job.Id, default));
        await job.Job.Submit("a");
        await host.Drain();
        await host.Drain();
        interleave.Fired.Should().Be(1);
        var stored = await ChainCancelRaceJob.Get(job.Id);
        var rows = await host.Ledger.Query(new JobQuery(WorkType: typeof(ChainCancelRaceJob).FullName!), default);
        stored!.StepBRan.Should().BeFalse(
            "rows: " + string.Join(" | ", rows.Select(r => r.Action + ":" + r.Status + ":marker=" + Marker(r))));
    }

    [Fact]
    public async Task a_cancellation_between_a_completion_and_its_successor_stops_the_chain()
    {
        // JOBS-0010: a cancellation lands while stage a's successor is being appended. Stage a's settlement then reads the
        // request and cancels the successor.
        ChainCancelRaceJob.Reset();
        var interleave = new Interleave();
        await using var host = await CreateHostAsync(configureServices: interleave.Install);
        SkipWithoutEntityRows(host);
        var job = new ChainCancelRaceJob();
        interleave.Before(row => row.WorkId == job.Id && row.Action == "b" && row.Status == JobStatus.Queued,
            () => host.Coordinator.CancelWorkAsync(typeof(ChainCancelRaceJob).FullName!, job.Id, default));
        await job.Job.Submit("a");
        await host.Drain();
        await host.Drain();
        interleave.Fired.Should().Be(1);
        var stored = await ChainCancelRaceJob.Get(job.Id);
        var rows = await host.Ledger.Query(new JobQuery(WorkType: typeof(ChainCancelRaceJob).FullName!), default);
        stored!.StepBRan.Should().BeFalse(
            "rows: " + string.Join(" | ", rows.Select(r => r.Action + ":" + r.Status + ":marker=" + Marker(r))));
    }

    [Fact]
    public async Task a_settle_whose_answer_is_lost_still_advances_the_chain()
    {
        // JOBS-0010: stage a's completion lands but its answer is lost. The successor was appended before it, so the chain
        // advances, and the failure this run then meets finds the claim ended and writes nothing.
        ChainCancelRaceJob.Reset();
        var interleave = new Interleave();
        await using var host = await CreateHostAsync(configureServices: interleave.Install);
        SkipWithoutEntityRows(host);
        var job = new ChainCancelRaceJob();
        interleave.After(row => row.WorkId == job.Id && row.Action == "a" && row.Status == JobStatus.Completed,
            () => throw new TimeoutException("the settle committed, its answer was lost"));
        await job.Job.Submit("a");
        await host.Drain();
        host.Advance(TimeSpan.FromHours(1));
        await host.Drain();
        var stored = await ChainCancelRaceJob.Get(job.Id);
        var rows = await host.Ledger.Query(new JobQuery(WorkType: typeof(ChainCancelRaceJob).FullName!), default);
        interleave.Fired.Should().Be(1);
        stored!.StepBRan.Should().BeTrue(
            "rows: " + string.Join(" | ", rows.Select(r => r.Action + ":" + r.Status + ":" + string.Join(">", r.Transitions.Select(t => t.Note)))));
    }

    [Fact]
    public async Task a_zombie_of_the_same_node_neither_renews_nor_settles_over_its_reclaim()
    {
        // JOBS-0010: the same node claims a job, stalls, is reaped and claims the job again. The stalled execution's write
        // misses its revision, and the row it reads back runs at another attempt, so nothing it writes lands.
        GreetJob.Reset();
        await using var host = await CreateHostAsync();
        var item = new GreetJob { Name = "zombie" };
        var handle = await item.Job.Submit();
        var now = host.Clock.GetUtcNow();
        var first = (await host.Ledger.ClaimNext("owner-a", now, now + TimeSpan.FromMinutes(1), [], default))!;
        var zombie = new ClaimedRow(host.Ledger, first);
        host.Advance(TimeSpan.FromMinutes(2));
        await host.Reap();
        var later = host.Clock.GetUtcNow();
        var second = await host.Ledger.ClaimNext("owner-a", later, later + TimeSpan.FromMinutes(1), [], default);
        second.Should().NotBeNull("the same node claims the requeued job again");

        (await zombie.Write(row => row.LeaseUntil = later + TimeSpan.FromHours(1), default)).Should().BeNull();
        (await zombie.Write(row => row.Status = JobStatus.Completed, default)).Should().BeNull();
        var after = (await host.Ledger.Get(handle.JobId, default))!;
        after.Status.Should().Be(JobStatus.Running);
        after.Attempt.Should().Be(second!.Attempt);
        after.Revision.Should().Be(second.Revision);
    }

    [Fact]
    public async Task lost_renewal_answers_never_cost_the_settlement()
    {
        // JOBS-0010: two renewals land but their answers are lost. The next write misses, reads the row and finds it still
        // runs under this claim, so the settlement lands.
        HeldJob.Reset();
        var interleave = new Interleave();
        await using var host = await CreateHostAsync(configureServices: interleave.Install);
        SkipWithoutEntityRows(host);
        host.Advance(TimeSpan.FromTicks(1_234_567));
        var item = new HeldJob();
        var handle = await item.Job.Submit();
        var draining = host.Drain();
        await Until(() => Task.FromResult(HeldJob.Executions == 1), "execution");
        var lost = 0;
        for (var i = 0; i < 2; i++)
            interleave.After(row => row.Id == handle.JobId && row.Status == JobStatus.Running,
                () => { Interlocked.Increment(ref lost); throw new TimeoutException("the renewal committed, its answer was lost"); });
        for (var i = 0; i < 2; i++)
        {
            var lease = (await host.Ledger.Get(handle.JobId, default))!.LeaseUntil;
            host.Advance(TimeSpan.FromSeconds(21));
            await Until(async () => (await host.Ledger.Get(handle.JobId, default))!.LeaseUntil > lease, "renewal " + i);
        }
        HeldJob.Release();
        await draining.WaitAsync(TimeSpan.FromSeconds(30));
        var after = (await host.Ledger.Get(handle.JobId, default))!;
        lost.Should().Be(2, "both renewal answers were lost");
        after.Status.Should().Be(JobStatus.Completed, "answers lost=" + lost + ", owner=" + after.Owner);
    }

    [Fact]
    public async Task a_stored_claim_reads_back_exactly_as_written()
    {
        // The claim a ledger returns is the row it stored: its revision exactly — what every owner write compares on — and
        // its times to the millisecond, the finest every store keeps (MongoDB keeps no finer).
        await using var host = await CreateHostAsync();
        host.Advance(TimeSpan.FromTicks(1_234_567));
        var handle = await new GreetJob { Name = "fidelity" }.Job.Submit();
        var now = host.Clock.GetUtcNow();
        var mine = (await host.Ledger.ClaimNext("owner-a", now, now + TimeSpan.FromMinutes(1), [], default))!;
        var stored = (await host.Ledger.Get(handle.JobId, default))!;
        var differences = new List<string>();
        void Same(string name, DateTimeOffset? written, DateTimeOffset? read)
        {
            var w = written is { } a ? a.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fff") : "none";
            var r = read is { } b ? b.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fff") : "none";
            if (!string.Equals(w, r, StringComparison.Ordinal)) differences.Add(name + ": wrote " + w + ", read " + r);
        }
        Same("VisibleAt", mine.VisibleAt, stored.VisibleAt);
        Same("FirstSubmittedAt", mine.FirstSubmittedAt, stored.FirstSubmittedAt);
        Same("LeaseUntil", mine.LeaseUntil, stored.LeaseUntil);
        for (var i = 0; i < Math.Min(mine.Transitions.Count, stored.Transitions.Count); i++)
            Same("Transitions[" + i + "].At", mine.Transitions[i].At, stored.Transitions[i].At);
        if (mine.Revision != stored.Revision) differences.Add("Revision");
        differences.Should().BeEmpty();
    }

    [Fact]
    public async Task a_submission_after_a_cancellation_runs()
    {
        // JOBS-0010: a cancellation leaves no tombstone; the item submitted again afterwards runs.
        GreetJob.Reset();
        await using var host = await CreateHostAsync();
        var item = new GreetJob { Name = "again" };
        await item.Job.Submit();
        await item.Job.Cancel();
        var again = await item.Job.Submit();
        await host.Drain();
        var row = (await host.Ledger.Get(again.JobId, default))!;
        (row.Status == JobStatus.Completed && GreetJob.Executions == 1).Should().BeTrue(
            "resubmission status=" + row.Status + ", executions=" + GreetJob.Executions);
    }

    [Fact]
    public async Task a_trailing_submission_after_cancelling_a_running_job_runs()
    {
        // JOBS-0006 / JOBS-0010: a running job is asked to cancel, and the item is submitted again; the trailing run runs.
        DedupeJob.Reset();
        await using var host = await CreateHostAsync();
        var item = new DedupeJob { Key = "trail-" + Guid.NewGuid().ToString("N") };
        var first = await item.Job.Submit();
        var now = host.Clock.GetUtcNow();
        var mine = (await host.Ledger.ClaimNext("owner-a", now, now + TimeSpan.FromMinutes(5), [], default))!;
        await item.Job.Cancel();
        var trailing = await item.Job.Submit();
        trailing.JobId.Should().NotBe(first.JobId, "the running job holds no coalescing slot, so the resubmission queues");
        (await new ClaimedRow(host.Ledger, mine).Write(settled =>
        {
            settled.Owner = null;
            settled.LeaseUntil = null;
            settled.Status = JobStatus.Completed;
            settled.LastSettledAt = now;
        }, default)).Should().NotBeNull();
        await host.Drain();
        var row = (await host.Ledger.Get(trailing.JobId, default))!;
        (row.Status == JobStatus.Completed && DedupeJob.Executions == 1).Should().BeTrue(
            "trailing status=" + row.Status + ", executions=" + DedupeJob.Executions);
    }

    [Fact]
    public async Task cancelling_one_item_keeps_the_submission_of_another_with_the_same_key()
    {
        // JOBS-0010: coalescing never crosses work items, so another item's submission with the same key has its own job,
        // and cancelling the first item leaves it to run.
        DedupeJob.Reset();
        await using var host = await CreateHostAsync();
        var key = "shared-" + Guid.NewGuid().ToString("N");
        var w1 = new DedupeJob { Key = key };
        var w2 = new DedupeJob { Key = key };
        var h1 = await w1.Job.Submit();
        var h2 = await w2.Job.Submit();
        h2.JobId.Should().NotBe(h1.JobId);
        await w1.Job.Cancel();
        await host.Drain();
        var o1 = await h1.Completion(TimeSpan.FromSeconds(5));
        var o2 = await h2.Completion(TimeSpan.FromSeconds(5));
        DedupeJob.Executions.Should().Be(1);
        o1.Status.Should().Be(JobStatus.Cancelled);
        o2.Status.Should().Be(JobStatus.Completed);
    }

    [Fact]
    public async Task a_marker_from_another_node_reaches_the_running_handler()
    {
        // JOBS-0010: another node asks a running job to cancel through the ledger alone. The owner's next renewal reads the
        // request and cancels the handler, which stops before it would have finished on its own.
        HeldJob.Reset();
        await using var host = await CreateHostAsync();
        var handle = await new HeldJob().Job.Submit();
        var draining = host.Drain();
        await Until(() => Task.FromResult(HeldJob.Executions == 1), "execution");
        var row = (await host.Ledger.Get(handle.JobId, default))!;
        var lease = row.LeaseUntil;
        row.CancelRequestedAt = host.Clock.GetUtcNow();
        (await host.Ledger.Update(row, default)).Should().BeTrue();
        host.Advance(TimeSpan.FromSeconds(21));
        await Until(async () => (await host.Ledger.Get(handle.JobId, default)) is { } r && (r.LeaseUntil > lease || r.IsTerminal), "renewal");
        var stopped = await Task.WhenAny(draining, Task.Delay(TimeSpan.FromSeconds(3))) == draining;
        HeldJob.Release();
        await draining.WaitAsync(TimeSpan.FromSeconds(30));
        var after = (await host.Ledger.Get(handle.JobId, default))!;
        stopped.Should().BeTrue("the handler ran on after the request and its renewal; it settled " + after.Status + " only when released");
        after.Status.Should().Be(JobStatus.Cancelled);
    }

    [Fact]
    public async Task a_chain_step_runs_once_when_a_stalled_run_and_its_reclaim_both_finish()
    {
        // JOBS-0010: stage a's first run stalls — its renewals fail — and is reaped, and the same node claims it again. Both
        // runs finish. Both address one successor row, and exclusivity holds it back until stage a settles, so stage b runs
        // exactly once.
        ChainCancelRaceJob.Reset();
        var unblock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ChainCancelRaceJob.Unblock = unblock;
        var failRenewals = false;
        string? work = null;
        void Install(IServiceCollection services)
        {
            using var scope = Koan.Core.Composition.KoanCompositionScope.Enter(services);
            JobRecord.Lifecycle.BeforeUpsert(context =>
            {
                var row = context.Current;
                if (Volatile.Read(ref failRenewals) && row.WorkId == work && row.Action == "a" && row.Status == JobStatus.Running
                    && row.Transitions.Count == 2)
                    throw new TimeoutException("the renewal of the stalled run fails");
                return ValueTask.FromResult(context.Proceed());
            });
        }
        await using var host = await CreateHostAsync(configureServices: Install);
        SkipWithoutEntityRows(host);
        var job = new ChainCancelRaceJob();
        work = job.Id;
        await job.Job.Submit("a");
        var first = host.Drain();
        await Until(() => Task.FromResult(ChainCancelRaceJob.StepAExecutions == 1), "first run");
        Volatile.Write(ref failRenewals, true);
        for (var i = 0; i < 4; i++) { host.Advance(TimeSpan.FromSeconds(20)); await Task.Delay(100); }
        await host.Reap();
        Volatile.Write(ref failRenewals, false);
        (await host.Ledger.Query(new JobQuery(WorkType: typeof(ChainCancelRaceJob).FullName!, Status: JobStatus.Queued), default))
            .Should().ContainSingle("the reaper requeued the stalled run");
        var second = host.Drain();
        await Until(() => Task.FromResult(ChainCancelRaceJob.StepAExecutions == 2), "second run");
        unblock.TrySetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(30));
        await second.WaitAsync(TimeSpan.FromSeconds(30));
        await host.Drain();
        await host.Reap();
        await host.Drain();
        var rows = await host.Ledger.Query(new JobQuery(WorkType: typeof(ChainCancelRaceJob).FullName!), default);
        var b = rows.Where(r => r.Action == "b").ToList();
        (b.Count(r => r.Status == JobStatus.Completed) == 1 && b.All(r => r.IsTerminal)).Should().BeTrue(
            "rows: " + string.Join(" | ", rows.Select(r => r.Action + ":" + r.Status + ":attempt " + r.Attempt)));
    }

    [Fact]
    public async Task a_cancellation_survives_a_failed_attempt()
    {
        // JOBS-0010: the cancellation lands while a failed attempt's retry is being written; the retry, applied on the row
        // as stored, settles the job Cancelled.
        FlakyJob.Reset();
        FlakyJob.SucceedAtAttempt = 2;
        var interleave = new Interleave();
        await using var host = await CreateHostAsync(configureServices: interleave.Install);
        SkipWithoutEntityRows(host);
        var item = new FlakyJob();
        await item.Job.Submit(FlakyJob.Action);
        interleave.Before(row => row.WorkId == item.Id && row.Status == JobStatus.Queued && row.Transitions.Count > 0
                                 && (row.Transitions[^1].Note ?? "").StartsWith("retry", StringComparison.Ordinal),
            () => host.Coordinator.CancelWorkAsync(typeof(FlakyJob).FullName!, item.Id, default));
        await host.Drain();
        host.Advance(TimeSpan.FromHours(1));
        await host.Drain();
        var job = (await host.JobFor<FlakyJob>(item.Id))!;
        (FlakyJob.Executions == 1 && job.Status == JobStatus.Cancelled).Should().BeTrue(
            "executions=" + FlakyJob.Executions + ", status=" + job.Status + ", fired=" + interleave.Fired
            + ", transitions=" + string.Join(" / ", job.Transitions.Select(t => t.Note)));
    }

    [Fact]
    public async Task a_reaper_reclaim_never_lands_over_a_renewal()
    {
        // JOBS-0010: the reaper reads a lapsed lease, the owner renews, and the reaper's reclaim — a compare on the revision
        // it read — misses; the job stays claimed by its owner.
        GreetJob.Reset();
        await using var host = await CreateHostAsync();
        var item = new GreetJob { Name = "r" };
        await item.Job.Submit();
        var now = host.Clock.GetUtcNow();
        var mine = (await host.Ledger.ClaimNext("owner-a", now, now + TimeSpan.FromMinutes(1), [], default))!;
        var later = now + TimeSpan.FromMinutes(2);
        var stuck = await host.Ledger.Stuck(later, 10, default);
        stuck.Should().ContainSingle();
        mine.LeaseUntil = later + TimeSpan.FromMinutes(5);
        (await host.Ledger.Update(mine, default)).Should().BeTrue();

        var row = stuck[0];
        row.Owner = null;
        row.LeaseUntil = null;
        row.Status = JobStatus.Queued;
        row.VisibleAt = later;
        var reclaimed = await host.Ledger.Update(row, default);
        var byB = await host.Ledger.ClaimNext("owner-b", later, later + TimeSpan.FromMinutes(5), [], default);
        (reclaimed || byB is not null).Should().BeFalse(
            "owner-a renewed its lease after the sweep read; reclaimed=" + reclaimed + ", owner-b claimed=" + (byB is not null));
    }

    [Fact]
    public async Task the_reaper_never_overwrites_a_renewal()
    {
        // JOBS-0010: the owner's renewal lands while the reaper's reclaim is being written; the reclaim misses, and the job
        // stays running under its owner.
        GreetJob.Reset();
        var interleave = new Interleave();
        await using var host = await CreateHostAsync(configureServices: interleave.Install);
        SkipWithoutEntityRows(host);
        await new GreetJob { Name = "r2" }.Job.Submit();
        var now = host.Clock.GetUtcNow();
        var mine = (await host.Ledger.ClaimNext("owner-a", now, now + TimeSpan.FromMinutes(1), [], default))!;
        host.Advance(TimeSpan.FromMinutes(2));
        var later = host.Clock.GetUtcNow();
        // owner-a is alive — its roster entry is fresh — so only its lapsed lease makes the reaper look at the job.
        await WorkerNode.Upsert(new WorkerNode { Id = "owner-a", StartedAt = now, LastSeenAt = later }, default);
        var renewed = false;
        interleave.Before(row => row.Id == mine.Id && row.Status == JobStatus.Queued && row.Owner == null, async () =>
        {
            mine.LeaseUntil = later + TimeSpan.FromMinutes(5);
            renewed = await host.Ledger.Update(mine, default);
        });
        await host.Reap();
        var stored = (await host.Ledger.Get(mine.Id, default))!;
        renewed.Should().BeTrue();
        stored.Status.Should().Be(JobStatus.Running, "the renewal landed first; owner=" + stored.Owner + ", fired=" + interleave.Fired);
        stored.Owner.Should().Be("owner-a");
    }

    [Fact]
    public async Task a_reaped_job_asked_to_cancel_settles()
    {
        // JOBS-0010: a running job is asked to cancel and its runner dies; the reaper's reclaim settles it Cancelled.
        GreetJob.Reset();
        await using var host = await CreateHostAsync();
        var item = new GreetJob { Name = "orphan" };
        var handle = await item.Job.Submit();
        var now = host.Clock.GetUtcNow();
        (await host.Ledger.ClaimNext("owner-dead", now, now + TimeSpan.FromMinutes(1), [], default)).Should().NotBeNull();
        await host.Coordinator.CancelWorkAsync(typeof(GreetJob).FullName!, item.Id, default);
        host.Advance(TimeSpan.FromMinutes(2));
        await host.Reap();
        await host.Drain();
        var row = (await host.Ledger.Get(handle.JobId, default))!;
        row.Status.Should().Be(JobStatus.Cancelled, "marker=" + Marker(row) + ", executions=" + GreetJob.Executions);
        GreetJob.Executions.Should().Be(0);
    }

    /// <summary>Steps other nodes' actions in around writes of job rows, each step once, when a write matches it
    /// (JOBS-0010). The in-memory ledger writes no Entity rows, so no step fires there.</summary>
    private sealed class Interleave
    {
        private readonly List<Step> _before = [];
        private readonly List<Step> _after = [];
        private int _fired;

        /// <summary>How many steps have run.</summary>
        public int Fired => Volatile.Read(ref _fired);

        public void Install(IServiceCollection services)
        {
            using var scope = Koan.Core.Composition.KoanCompositionScope.Enter(services);
            Func<Koan.Data.Core.Lifecycle.EntityLifecycleContext<JobRecord>, ValueTask<Koan.Data.Core.Lifecycle.EntityLifecycleResult>> before =
                async context =>
                {
                    await Run(_before, context.Current);
                    return context.Proceed();
                };
            Func<Koan.Data.Core.Lifecycle.EntityLifecycleContext<JobRecord>, ValueTask> after =
                context => new ValueTask(Run(_after, context.Current));
            JobRecord.Lifecycle.BeforeUpsert(before).AfterUpsert(after);
        }

        /// <summary>Run <paramref name="then"/> just before the first write matching <paramref name="when"/>.</summary>
        public void Before(Func<JobRecord, bool> when, Func<Task> then)
        {
            lock (_before) _before.Add(new Step(when, then));
        }

        /// <summary>Run <paramref name="then"/> right after the first write matching <paramref name="when"/> lands.</summary>
        public void After(Func<JobRecord, bool> when, Func<Task> then)
        {
            lock (_after) _after.Add(new Step(when, then));
        }

        private async Task Run(List<Step> steps, JobRecord row)
        {
            Step[] snapshot;
            lock (steps) snapshot = steps.ToArray();
            foreach (var step in snapshot)
                if (step.When(row) && step.TryFire())
                {
                    Interlocked.Increment(ref _fired);
                    await step.Then();
                }
        }

        private sealed class Step(Func<JobRecord, bool> when, Func<Task> then)
        {
            private int _fired;

            public Func<JobRecord, bool> When { get; } = when;

            public Func<Task> Then { get; } = then;

            public bool TryFire() => Interlocked.CompareExchange(ref _fired, 1, 0) == 0;
        }
    }

    [Fact]
    public async Task exclusive_is_the_default_one_action_per_entity_at_a_time()
    {
        ExclusiveJob.Reset();
        await using var host = await CreateHostAsync();
        var e = new ExclusiveJob();
        await e.Job.Submit(ExclusiveJob.ActionA);
        await e.Job.Submit(ExclusiveJob.ActionB);   // same entity, different action, different lane
        await host.Drain();
        ExclusiveJob.Peak.Should().Be(1, "different actions on one entity must not run concurrently by default");
    }

    [Fact]
    public async Task parallel_safe_allows_concurrent_actions_on_one_entity()
    {
        ParallelJob.Reset();
        await using var host = await CreateHostAsync();
        var e = new ParallelJob();
        await e.Job.Submit(ParallelJob.ActionA);
        await e.Job.Submit(ParallelJob.ActionB);
        await host.Drain();
        ParallelJob.Peak.Should().Be(2, "[ParallelSafe] lifts per-entity serialization");
    }

    [Fact]
    public async Task different_entities_run_concurrently()
    {
        ExclusiveJob.Reset();
        await using var host = await CreateHostAsync();
        var e1 = new ExclusiveJob();
        var e2 = new ExclusiveJob();
        await e1.Job.Submit(ExclusiveJob.ActionA);
        await e2.Job.Submit(ExclusiveJob.ActionA);   // same action/lane, different entities
        await host.Drain();
        ExclusiveJob.Peak.Should().Be(2, "serialization is per-entity, not global — different entities still parallelize");
    }

    // --- runtime-resolved gate keys (§18) ---

    [Fact]
    public async Task a_resolved_gate_key_defers_only_same_host_siblings()
    {
        RemoteFetch.Reset();
        await using var host = await CreateHostAsync();
        var xma = new RemoteTarget { Host = "xivmodarchive.com" }; await RemoteTarget.Upsert(xma);
        var gh  = new RemoteTarget { Host = "github.com" };        await RemoteTarget.Upsert(gh);
        RemoteFetch.Throttled = "xivmodarchive.com";

        // 1. one xivmodarchive fetch hits the 503 and backs off the resolved host pool.
        var first = new RemoteFetch { TargetId = xma.Id };
        await first.Job.Submit(RemoteFetch.Refresh);
        await host.Drain();
        RemoteFetch.Executed.Should().Be(1);
        RemoteFetch.Ran.Should().NotContain(first.Id, "it backed off, it didn't complete");

        // 2. siblings submitted while the gate is active: same host is gated at dispatch (never runs); other host runs.
        var xmaSibling = new RemoteFetch { TargetId = xma.Id };
        var ghJob      = new RemoteFetch { TargetId = gh.Id };
        await xmaSibling.Job.Submit(RemoteFetch.Refresh);
        await ghJob.Job.Submit(RemoteFetch.Refresh);
        await host.Drain();
        RemoteFetch.Ran.Should().Contain(ghJob.Id, "a different host is unaffected by the xivmodarchive gate");
        RemoteFetch.Ran.Should().NotContain(xmaSibling.Id);
        RemoteFetch.Executed.Should().Be(2, "the gated sibling never ran — only the github fetch executed");

        // 3. host recovers, gate releases → the xivmodarchive jobs run.
        RemoteFetch.Throttled = null;
        host.Advance(TimeSpan.FromMinutes(11));
        await host.Drain();
        RemoteFetch.Ran.Should().Contain(first.Id);
        RemoteFetch.Ran.Should().Contain(xmaSibling.Id);
    }

    // --- high-throughput / bulk (§19): the same guarantees on every tier ---
    // Seeded through the LEDGER (not JobRecord.Save) so the in-memory tier — whose ledger is a dictionary, not the
    // data store — stays consistent. Volume is enough to exercise a real index, small enough to stay fast on containers.

    /// <summary>Backlog size for the §19 push-down specs; override per tier if a store needs a lighter touch.</summary>
    protected virtual int ScaleVolume => 2000;

    [Fact]
    public async Task query_returns_only_the_matching_status_among_a_backlog()
    {
        await using var host = await CreateHostAsync();
        var wt = typeof(GreetJob).FullName!;
        var now = host.Clock.GetUtcNow();
        await host.Ledger.AppendMany(Seed(wt, ScaleVolume, JobStatus.Completed, now, "c"), default);   // noise
        await host.Ledger.AppendMany(Seed(wt, 5, JobStatus.Queued, now, "q"), default);                // needles

        var active = await host.Ledger.Query(new JobQuery(WorkType: wt, Status: JobStatus.Queued), default);

        active.Should().HaveCount(5);                                  // the status filter is applied on the store
        active.Should().OnlyContain(r => r.Status == JobStatus.Queued);
    }

    [Fact]
    public async Task claim_takes_the_fifo_head_of_a_large_backlog()
    {
        await using var host = await CreateHostAsync();
        var now = host.Clock.GetUtcNow();
        var wt = typeof(GreetJob).FullName!;
        await host.Ledger.AppendMany(Seed(wt, ScaleVolume, JobStatus.Queued, now.AddDays(-1), "q", visible: true), default);

        var claimed = await host.Ledger.ClaimNext("owner", now, now.AddMinutes(1), Array.Empty<string>(), default);

        claimed.Should().NotBeNull();
        claimed!.WorkId.Should().Be("q0");            // earliest FirstSubmittedAt → ordered claim, true FIFO head
        claimed.Status.Should().Be(JobStatus.Running);
    }

    [Fact]
    public async Task retention_purges_old_failed_keeps_recent_and_active()
    {
        await using var host = await CreateHostAsync(o =>
        {
            o.ArchiveAfter = TimeSpan.FromDays(7);
            o.FailedAfter = TimeSpan.FromDays(30);
            o.RetainPerWorkType = 0;
        });
        var wt = typeof(GreetJob).FullName!;
        var now = host.Clock.GetUtcNow();
        await host.Ledger.AppendMany(new[]
        {
            Rec(wt, "c-old", JobStatus.Completed, now.AddDays(-10)),  // > 7d  → purged
            Rec(wt, "c-new", JobStatus.Completed, now.AddDays(-1)),   // < 7d  → kept
            Rec(wt, "f-old", JobStatus.Failed, now.AddDays(-40)),     // > 30d → purged (formerly retained forever)
            Rec(wt, "f-new", JobStatus.Failed, now.AddDays(-5)),      // < 30d → kept
            Rec(wt, "active", JobStatus.Queued, now),                 // active → never purged
        }, default);

        var purged = await host.Archive();

        purged.Should().Be(2);
        var ids = (await host.Ledger.Query(new JobQuery(WorkType: wt), default)).Select(r => r.WorkId).ToHashSet();
        ids.Should().BeEquivalentTo(new[] { "c-new", "f-new", "active" });
    }

    [Fact]
    public async Task retention_cap_trims_terminal_to_the_newest_per_worktype()
    {
        await using var host = await CreateHostAsync(o =>
        {
            o.ArchiveAfter = TimeSpan.Zero;   // windows off — exercise the cap alone
            o.FailedAfter = TimeSpan.Zero;
            o.RetainPerWorkType = 10;
        });
        var wt = typeof(GreetJob).FullName!;
        var now = host.Clock.GetUtcNow();
        await host.Ledger.AppendMany(
            Enumerable.Range(0, 50).Select(i => Rec(wt, $"t{i:D3}", JobStatus.Completed, now.AddSeconds(-i))).ToList(), default);

        var purged = await host.Archive();

        purged.Should().Be(40);   // keep newest 10
        (await host.Ledger.Query(new JobQuery(WorkType: wt, Status: JobStatus.Completed), default)).Should().HaveCount(10);
    }

    [Fact]
    public async Task retention_reads_and_removes_one_bounded_page_per_call()
    {
        // JOBS-0010: purges and trims read and remove at most one page; the sweep pages until a page falls short.
        await using var host = await CreateHostAsync(o =>
        {
            o.ArchiveAfter = TimeSpan.FromDays(7);
            o.FailedAfter = TimeSpan.FromDays(30);
            o.RetainPerWorkType = 0;
        });
        var wt = typeof(GreetJob).FullName!;
        var now = host.Clock.GetUtcNow();
        await host.Ledger.AppendMany(Seed(wt, 25, JobStatus.Completed, now.AddDays(-10), "stale"), default);
        await host.Ledger.AppendMany(Seed(wt, 25, JobStatus.Completed, now, "recent"), default);

        (await host.Ledger.PurgeArchivable(now.AddDays(-7), 10, default)).Should().Be(10);
        (await host.Ledger.PurgeArchivable(now.AddDays(-7), 10, default)).Should().Be(10);
        (await host.Ledger.PurgeArchivable(now.AddDays(-7), 10, default)).Should().Be(5);
        (await host.Ledger.PurgeArchivable(now.AddDays(-7), 10, default)).Should().Be(0);

        (await host.Ledger.TrimTerminal(wt, 5, 10, default)).Should().Be(10);
        (await host.Ledger.TrimTerminal(wt, 5, 10, default)).Should().Be(10);
        (await host.Ledger.TrimTerminal(wt, 5, 10, default)).Should().Be(0);
        (await host.Ledger.CountActive(wt, default)).Should().Be(0);
        (await host.Ledger.Query(new JobQuery(WorkType: wt), default)).Should().HaveCount(5);

        // A backlog larger than one page is purged completely by the sweep, a page at a time.
        await host.Ledger.AppendMany(Seed(wt, ScaleVolume / 2, JobStatus.Completed, now.AddDays(-20), "backlog"), default);
        (await host.Archive()).Should().Be(ScaleVolume / 2);
    }

    [Fact]
    public async Task facade_query_reads_newest_first_one_page_at_a_time()
    {
        // JOBS-0010: Query and WithStatus read one bounded page, newest first by submission.
        await using var host = await CreateHostAsync();
        var wt = typeof(GreetJob).FullName!;
        var now = host.Clock.GetUtcNow();
        await host.Ledger.AppendMany(Seed(wt, 5, JobStatus.Completed, now, "p"), default);   // p0 newest … p4 oldest

        (await host.Ledger.Query(new JobQuery(WorkType: wt) { Size = 2 }, default)).Select(r => r.WorkId)
            .Should().Equal("p0", "p1");
        (await host.Ledger.Query(new JobQuery(WorkType: wt) { Page = 2, Size = 2 }, default)).Select(r => r.WorkId)
            .Should().Equal("p2", "p3");
        (await host.Ledger.Query(new JobQuery(WorkType: wt) { Page = 3, Size = 2 }, default)).Select(r => r.WorkId)
            .Should().Equal("p4");
        (await host.StatusOf<GreetJob>("p4")).Should().Be(JobStatus.Completed);
    }

    [Fact]
    public async Task count_active_excludes_terminal_rows()
    {
        await using var host = await CreateHostAsync();
        var wt = typeof(GreetJob).FullName!;
        var now = host.Clock.GetUtcNow();
        await host.Ledger.AppendMany(Seed(wt, 30, JobStatus.Queued, now, "a"), default);
        await host.Ledger.AppendMany(Seed(wt, 70, JobStatus.Completed, now, "t"), default);

        (await host.Ledger.CountActive(wt, default)).Should().Be(30);
    }

    [Fact]
    public async Task cursor_conveyor_drains_a_source_with_a_bounded_ledger()
    {
        Conveyor.Reset();
        await using var host = await CreateHostAsync();
        await new Conveyor { Total = 2000, Window = 100 }.Job.Submit(Conveyor.Pull);
        await host.Drain();

        Conveyor.ItemsProcessed.Should().Be(2000);
        Conveyor.WindowsRun.Should().Be(20);
        var rows = await host.Ledger.Query(new JobQuery(WorkType: typeof(Conveyor).FullName!), default);
        rows.Should().HaveCount(20);                  // one ledger row per window, not per item
        rows.Should().OnlyContain(r => r.Status == JobStatus.Completed);
    }

    [Fact]
    public async Task metrics_rollup_survives_a_retention_purge()
    {
        // §20.2: terminal outcomes accrue into a node-sharded JobMetric and SURVIVE a retention purge that deletes the
        // very JobRecords they counted — the throughput-history half (active counts come from the indexed ledger).
        GreetJob.Reset();
        await using var host = await CreateHostAsync(o =>
        {
            o.MetricsEnabled = true;
            o.ArchiveAfter = TimeSpan.FromMinutes(1);   // small window: a short advance makes the Completed rows purgeable
        });
        var wt = typeof(GreetJob).FullName!;

        await Enumerable.Range(0, 5).Select(i => new GreetJob { Name = $"n{i}" }).ToList().Submit();
        await host.Drain();              // 5 complete → 5 in-memory deltas
        await host.FlushMetrics();       // fold into this node's JobMetric shard row

        var from = host.Clock.GetUtcNow().AddHours(-1);
        (await JobMetrics.Summary(wt, from, host.Clock.GetUtcNow().AddHours(1))).GetValueOrDefault("Completed").Should().Be(5);

        // Purge the ledger: the 5 Completed JobRecords are deleted, but the rollup (retained 30d) is not.
        host.Advance(TimeSpan.FromMinutes(2));
        await host.Archive();
        (await host.Ledger.Query(new JobQuery(WorkType: wt, Status: JobStatus.Completed), default)).Should().BeEmpty();

        (await JobMetrics.Summary(wt, from, host.Clock.GetUtcNow().AddHours(1))).GetValueOrDefault("Completed").Should().Be(5);
    }

    [Fact]
    public async Task concurrent_claimers_take_distinct_jobs_no_double_claim()
    {
        // §20.3: under competing consumers the atomic claim hands each ready job to exactly ONE claimer — no double
        // claim. On durable tiers this exercises the conditional compare-and-set; the in-memory ledger is already
        // single-claim under its lock. (Without an atomic claim, last-write-wins lets two claimers grab the same row.)
        await using var host = await CreateHostAsync();
        var wt = typeof(GreetJob).FullName!;
        const int jobCount = 24;
        await host.Ledger.AppendMany(Seed(wt, jobCount, JobStatus.Queued, host.Clock.GetUtcNow(), "c", visible: true), default);
        host.Advance(TimeSpan.FromSeconds(1));               // all seeded VisibleAt are now in the past → claimable
        var now = host.Clock.GetUtcNow();

        var claimed = new System.Collections.Concurrent.ConcurrentBag<string>();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(async () =>
        {
            while (await host.Ledger.ClaimNext($"owner-{i}", now, now.AddMinutes(1), Array.Empty<string>(), default) is { } rec)
                claimed.Add(rec.Id);
        })));

        // Checked first, because it separates the two ways the counts below can be wrong. A claim handed to two
        // owners leaves nothing queued and shows up as MORE than jobCount — measured at 104 of 24 with the
        // conditional write removed. A claim nobody took leaves rows queued and shows up as fewer: a claimer
        // treats a CAS loss as "no work available" and stops for good, so a shortfall is the drive loop giving
        // up, not the ledger handing a job out twice. Same assertion, opposite directions, unrelated causes.
        (await host.Ledger.Query(new JobQuery(WorkType: wt, Status: JobStatus.Queued), default))
            .Should().BeEmpty("every seeded job was claimable and no claimer should have stopped early");

        claimed.Should().HaveCount(jobCount);                // every job claimed
        claimed.Distinct().Should().HaveCount(jobCount);     // each claimed exactly once — no double-claim
    }

    [Fact]
    public async Task a_settled_job_gets_an_absolute_expiry()
    {
        // §20.4: a terminal row carries ExpireAt = LastSettledAt + its per-outcome window (Completed → ArchiveAfter).
        // TTL-capable stores (Mongo) expire on it automatically via the [Index(Ttl)]; the rest purge by it on the sweep.
        GreetJob.Reset();
        await using var host = await CreateHostAsync(o => o.ArchiveAfter = TimeSpan.FromDays(3));
        var j = new GreetJob { Name = "x" };
        var id = j.Id;
        await j.Job.Submit();
        await host.Drain();

        var rec = await host.JobFor<GreetJob>(id);
        rec!.Status.Should().Be(JobStatus.Completed);
        rec.ExpireAt.Should().Be(rec.LastSettledAt!.Value + TimeSpan.FromDays(3));
    }

    // --- dispatch-time pool gates (JOBS-0007) ---

    [Fact]
    public async Task pool_job_is_dispatched_to_a_free_member()
    {
        PoolJob.Reset();
        var resolver = new TestPoolResolver(new[] { "server-a", "server-b" });
        await using var host = await CreateHostAsync(
            configureServices: s => s.AddSingleton<IJobPoolResolver>(resolver));
        var j = new PoolJob();
        var id = j.Id;
        await j.Job.Submit();
        await host.Drain();
        PoolJob.Executions.Should().Be(1);
        (await host.StatusOf<PoolJob>(id)).Should().Be(JobStatus.Completed);
        var rec = await host.JobFor<PoolJob>(id);
        rec!.GateKey.Should().BeOneOf("server-a", "server-b");
        rec.PoolKey.Should().Be("test-ai-servers");
    }

    [Fact]
    public async Task pool_gatekey_is_null_at_submit_and_stamped_at_claim()
    {
        var resolver = new TestPoolResolver(new[] { "server-a" });
        await using var host = await CreateHostAsync(
            configureServices: s => s.AddSingleton<IJobPoolResolver>(resolver));
        var j = new PoolJob();
        var id = j.Id;
        await j.Job.Submit();

        var queued = await host.JobFor<PoolJob>(id);
        queued!.GateKey.Should().BeNull("pool job GateKey is unresolved at submit");
        queued.PoolKey.Should().Be("test-ai-servers");

        await host.Drain();

        var done = await host.JobFor<PoolJob>(id);
        done!.GateKey.Should().NotBeNull("pool job GateKey is stamped at claim time");
        done.GateKey.Should().Be("server-a");
    }

    [Fact]
    public async Task pooled_job_sees_the_elected_member_as_its_claimed_gate()
    {
        // The queued row has no gate key, yet the handler's snapshot carries the member elected at claim:
        // State.GateKey projects THIS claim, so pool work can self-identify its dispatch target without
        // querying the ledger.
        PoolJob.Reset();
        var resolver = new TestPoolResolver(new[] { "server-a" });
        await using var host = await CreateHostAsync(
            configureServices: s => s.AddSingleton<IJobPoolResolver>(resolver));
        var j = new PoolJob();
        var id = j.Id;
        await j.Job.Submit();

        var queued = await host.JobFor<PoolJob>(id);
        queued!.GateKey.Should().BeNull("the member is elected only at claim time");

        await host.Drain();

        PoolJob.ClaimedGates.Single(o => o.WorkId == j.Id).GateKey.Should().Be("server-a",
            "the snapshot carries the elected member although the queued key was null");
        (await host.StatusOf<PoolJob>(id)).Should().Be(JobStatus.Completed);
    }

    [Fact]
    public async Task pool_all_slots_full_blocks_claim()
    {
        var resolver = new TestPoolResolver(new[] { "server-a", "server-b" }) { CapacityPerMember = 1 };
        await using var host = await CreateHostAsync(
            configureServices: s => s.AddSingleton<IJobPoolResolver>(resolver));

        var j = new PoolJob();
        await j.Job.Submit();

        var now = host.Clock.GetUtcNow();
        var wt = typeof(PoolJob).FullName!;
        await host.Ledger.AppendMany(new[]
        {
            new JobRecord { WorkType = wt, WorkId = "occupied-a", Action = "", Status = JobStatus.Running, Lane = "default", VisibleAt = now, FirstSubmittedAt = now, Exclusive = true, GateKey = "server-a", Owner = "other", LeaseUntil = now.AddMinutes(10) },
            new JobRecord { WorkType = wt, WorkId = "occupied-b", Action = "", Status = JobStatus.Running, Lane = "default", VisibleAt = now, FirstSubmittedAt = now, Exclusive = true, GateKey = "server-b", Owner = "other", LeaseUntil = now.AddMinutes(10) },
        }, default);

        var pools = new Dictionary<string, PoolDispatchContext>
        {
            ["test-ai-servers"] = new PoolDispatchContext("test-ai-servers", new[] { "server-a", "server-b" }, 1),
        };
        var claimed = await host.Ledger.ClaimNext("owner", now, now.AddMinutes(5), Array.Empty<string>(), default, pools);
        claimed.Should().BeNull("no slots are available when all pool members are at capacity");
    }

    [Fact]
    public async Task pool_dynamic_member_update_unblocks_queued_jobs()
    {
        PoolJob.Reset();
        var resolver = new TestPoolResolver(Array.Empty<string>());
        await using var host = await CreateHostAsync(
            configureServices: s => s.AddSingleton<IJobPoolResolver>(resolver));
        var j = new PoolJob();
        var id = j.Id;
        await j.Job.Submit();

        await host.Drain();
        PoolJob.Executions.Should().Be(0, "empty pool: no member to dispatch to");
        (await host.StatusOf<PoolJob>(id)).Should().Be(JobStatus.Queued);

        resolver.Members = new[] { "server-a" };
        await host.Drain();

        PoolJob.Executions.Should().Be(1, "pool member added at runtime unblocks the queued job");
        (await host.StatusOf<PoolJob>(id)).Should().Be(JobStatus.Completed);
    }

    [Fact]
    public async Task pool_capacity_per_member_allows_multiple_concurrent_claims()
    {
        var resolver = new TestPoolResolver(new[] { "server-a" }) { CapacityPerMember = 2 };
        await using var host = await CreateHostAsync(
            configureServices: s => s.AddSingleton<IJobPoolResolver>(resolver));

        var j1 = new PoolJob();
        var j2 = new PoolJob();
        await j1.Job.Submit();
        await j2.Job.Submit();

        var now = host.Clock.GetUtcNow();
        var pools = new Dictionary<string, PoolDispatchContext>
        {
            ["test-ai-servers"] = new PoolDispatchContext("test-ai-servers", new[] { "server-a" }, 2),
        };
        var first  = await host.Ledger.ClaimNext("owner", now, now.AddMinutes(5), Array.Empty<string>(), default, pools);
        var second = await host.Ledger.ClaimNext("owner", now, now.AddMinutes(5), Array.Empty<string>(), default, pools);

        first.Should().NotBeNull();
        second.Should().NotBeNull("capacity=2 allows two concurrent claims on the same member");
        first!.GateKey.Should().Be("server-a");
        second!.GateKey.Should().Be("server-a");
    }

    [Fact]
    public async Task pool_job_admission_is_governed_by_pool_capacity_not_resource_gates()
    {
        // ARCH-0079 convergence: a [JobPool] row is admitted by pool-member capacity, not by [JobGate] resource gates
        // (its GateKey is the elected member, not a backoff key). Both ledger tiers must agree — a gate that happens to
        // name a pool member key must not block a pool job the pool can still serve.
        PoolJob.Reset();
        var resolver = new TestPoolResolver(new[] { "server-a" });
        await using var host = await CreateHostAsync(
            configureServices: s => s.AddSingleton<IJobPoolResolver>(resolver));
        var now = host.Clock.GetUtcNow();
        var poolWt = typeof(PoolJob).FullName!;

        await host.Ledger.SetGate("server-a", now.AddMinutes(10), "unrelated gate on the member key", default);

        // a queued pool job that (atypically) already carries a GateKey equal to the gated member key.
        await host.Ledger.Append(new JobRecord
        {
            Id = $"{poolWt}:g0", WorkType = poolWt, WorkId = "g0", Action = "",
            Status = JobStatus.Queued, PoolKey = "test-ai-servers", GateKey = "server-a", Exclusive = true,
            VisibleAt = now, FirstSubmittedAt = now,
        }, default);

        var pools = new Dictionary<string, PoolDispatchContext>
        {
            ["test-ai-servers"] = new("test-ai-servers", new[] { "server-a" }, 1),
        };
        var claimed = await host.Ledger.ClaimNext("owner", now, now.AddMinutes(1), Array.Empty<string>(), default, pools);

        claimed.Should().NotBeNull("a pool job is governed by pool capacity, not a resource gate on its member key");
        claimed!.WorkId.Should().Be("g0");
    }

    // --- head-of-line starvation: the claim scan must page past unclaimable work (JOBS-0007 regression) ---
    // A window's worth of unclaimable rows at the FIFO head (an exhausted pool, a saturated lane, gated keys, or a
    // busy exclusive work-item) must not consume the durable ledger's bounded scan and strand runnable work queued
    // behind it. These specs set a tiny ClaimScanBatch and seed MORE than one window of unclaimable rows ahead of the
    // runnable work, so the windowed durable ledger reproduces the stall; the in-memory ledger (full scan) is already
    // immune, which is exactly the convergence the fix restores.

    [Fact]
    public async Task exhausted_pool_does_not_starve_other_work_in_the_claim_scan()
    {
        // The live incident: a [JobPool] with zero available members backed up 1000+ unclaimable jobs at the oldest
        // VisibleAt; runnable non-pool jobs behind them never got claimed and the whole pipeline stalled for ~18h.
        PoolJob.Reset();
        GreetJob.Reset();
        var resolver = new TestPoolResolver(Array.Empty<string>());   // exhausted: no members
        await using var host = await CreateHostAsync(
            o => o.ClaimScanBatch = 3,
            s => s.AddSingleton<IJobPoolResolver>(resolver));

        var now = host.Clock.GetUtcNow();
        var poolWt = typeof(PoolJob).FullName!;

        // 8 unclaimable pool jobs hold the oldest VisibleAt — more than two claim-scan windows.
        await host.Ledger.AppendMany(
            Enumerable.Range(0, 8).Select(i => new JobRecord
            {
                Id = $"{poolWt}:stuck{i}", WorkType = poolWt, WorkId = $"stuck{i}", Action = "",
                Status = JobStatus.Queued, PoolKey = "test-ai-servers", Exclusive = true,
                VisibleAt = now.AddMinutes(-10).AddMilliseconds(i), FirstSubmittedAt = now.AddMinutes(-10).AddMilliseconds(i),
            }).ToList(), default);

        // 3 runnable non-pool jobs queued behind them (newer VisibleAt).
        var greets = Enumerable.Range(0, 3).Select(i => new GreetJob { Name = $"n{i}" }).ToList();
        foreach (var g in greets) await g.Job.Submit();

        await host.Drain();

        GreetJob.Executions.Should().Be(3, "runnable non-pool work must not be starved by an exhausted pool's backlog");
        PoolJob.Executions.Should().Be(0, "the pool is exhausted — its jobs stay queued");
        foreach (var g in greets)
            (await host.StatusOf<GreetJob>(g.Id)).Should().Be(JobStatus.Completed);
    }

    [Fact]
    public async Task saturated_lane_does_not_starve_claims_for_other_lanes()
    {
        await using var host = await CreateHostAsync(o => o.ClaimScanBatch = 3);
        var now = host.Clock.GetUtcNow();
        var wt = typeof(GreetJob).FullName!;

        // 8 jobs on the saturated "slow" lane hold the oldest VisibleAt.
        await host.Ledger.AppendMany(
            Enumerable.Range(0, 8).Select(i => new JobRecord
            {
                Id = $"{wt}:slow{i}", WorkType = wt, WorkId = $"slow{i}", Action = "",
                Status = JobStatus.Queued, Lane = "slow", Exclusive = true,
                VisibleAt = now.AddMinutes(-10).AddMilliseconds(i), FirstSubmittedAt = now.AddMinutes(-10).AddMilliseconds(i),
            }).ToList(), default);

        // one job on a free lane, queued behind them.
        await host.Ledger.Append(new JobRecord
        {
            Id = $"{wt}:free0", WorkType = wt, WorkId = "free0", Action = "",
            Status = JobStatus.Queued, Lane = "default", Exclusive = true,
            VisibleAt = now, FirstSubmittedAt = now,
        }, default);

        var claimed = await host.Ledger.ClaimNext("owner", now, now.AddMinutes(1), new[] { "slow" }, default);

        claimed.Should().NotBeNull("a saturated lane must not block claims for other lanes");
        claimed!.WorkId.Should().Be("free0");
        claimed.Lane.Should().Be("default");
    }

    // --- cross-lane fairness (JOBS-0008): a perpetually-fed / older upstream lane must not monopolize the claim budget
    // and starve a downstream lane. Under the old global (VisibleAt, FirstSubmittedAt) order the oldest-N claims were
    // ALL the upstream lane's (a downstream chain job is stamped VisibleAt=now on each advance, so it is structurally
    // newer and never entered the oldest-N window). Weighted fair queuing per lane makes each lane's share guaranteed.
    // Runs on every tier — in-memory full-scan and durable per-lane indexed seek converge on the same selection.

    [Fact]
    public async Task fed_lane_does_not_starve_a_backlog_lane()
    {
        await using var host = await CreateHostAsync();
        var now = host.Clock.GetUtcNow();
        var crawlWt = typeof(CrawlJob).FullName!;
        var trWt = typeof(TranslationJob).FullName!;

        // upstream "crawl": 10 jobs holding the OLDEST VisibleAt (a continuously-fed head stage is always older).
        await host.Ledger.AppendMany(
            Enumerable.Range(0, 10).Select(i => new JobRecord
            {
                Id = $"{crawlWt}:crawl{i}", WorkType = crawlWt, WorkId = $"crawl{i}", Action = CrawlJob.Crawl,
                Status = JobStatus.Queued, Lane = "crawl", Exclusive = true,
                VisibleAt = now.AddMinutes(-10).AddMilliseconds(i), FirstSubmittedAt = now.AddMinutes(-10).AddMilliseconds(i),
            }).ToList(), default);

        // downstream "translation": 5 jobs with NEWER VisibleAt (produced later in the pipeline).
        await host.Ledger.AppendMany(
            Enumerable.Range(0, 5).Select(i => new JobRecord
            {
                Id = $"{trWt}:tr{i}", WorkType = trWt, WorkId = $"tr{i}", Action = TranslationJob.Translate,
                Status = JobStatus.Queued, Lane = "translation", Exclusive = true,
                VisibleAt = now.AddMinutes(-1).AddMilliseconds(i), FirstSubmittedAt = now.AddMinutes(-1).AddMilliseconds(i),
            }).ToList(), default);

        var lanes = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            var claimed = await host.Ledger.ClaimNext("owner", now, now.AddMinutes(1), Array.Empty<string>(), default);
            if (claimed is null) break;
            lanes.Add(claimed.Lane);
        }

        lanes.Count(l => l == "translation").Should().BeGreaterThan(0,
            "a downstream lane must get a fair share of the claim budget, not be starved by an older upstream lane");
    }

    [Fact]
    public async Task lane_weights_give_the_heavier_lane_a_larger_share()
    {
        await using var host = await CreateHostAsync(o =>
        {
            o.LaneWeights["crawl"] = 3;          // crawl gets ~3x translation's dispatch share
            o.LaneWeights["translation"] = 1;
        });
        var now = host.Clock.GetUtcNow();
        var crawlWt = typeof(CrawlJob).FullName!;
        var trWt = typeof(TranslationJob).FullName!;

        await host.Ledger.AppendMany(
            Enumerable.Range(0, 20).Select(i => new JobRecord
            {
                Id = $"{crawlWt}:c{i}", WorkType = crawlWt, WorkId = $"c{i}", Action = CrawlJob.Crawl,
                Status = JobStatus.Queued, Lane = "crawl", Exclusive = true,
                VisibleAt = now.AddMinutes(-5).AddMilliseconds(i), FirstSubmittedAt = now.AddMinutes(-5).AddMilliseconds(i),
            }).ToList(), default);
        await host.Ledger.AppendMany(
            Enumerable.Range(0, 20).Select(i => new JobRecord
            {
                Id = $"{trWt}:t{i}", WorkType = trWt, WorkId = $"t{i}", Action = TranslationJob.Translate,
                Status = JobStatus.Queued, Lane = "translation", Exclusive = true,
                VisibleAt = now.AddMinutes(-5).AddMilliseconds(i), FirstSubmittedAt = now.AddMinutes(-5).AddMilliseconds(i),
            }).ToList(), default);

        int crawl = 0, tr = 0;
        for (var i = 0; i < 8; i++)
        {
            var claimed = await host.Ledger.ClaimNext("owner", now, now.AddMinutes(1), Array.Empty<string>(), default);
            if (claimed is null) break;
            if (claimed.Lane == "crawl") crawl++; else if (claimed.Lane == "translation") tr++;
        }

        crawl.Should().BeGreaterThan(tr, "the higher-weighted lane gets the larger share (no strict priority — translation still runs)");
        tr.Should().BeGreaterThan(0, "weighting is proportional, not strict priority — the lighter lane is never starved");
    }

    // --- self-reporting (JOBS-0008): the Jobs pillar now ships an IHealthContributor so a stalled/starved lane is a
    // first-class signal (per-lane depth + oldest-queued-age), not an inference hours later. Resolved + Check()ed
    // directly (the same path StartupProbeService uses) so we assert on HealthReport.State, not the async aggregator.

    private static IHealthContributor JobsHealth(JobsHarness host)
        => host.Services.GetServices<IHealthContributor>().Single(c => c.Name == "Koan.Jobs");

    [Fact]
    public async Task jobs_health_reports_queued_and_running_facts()
    {
        await using var host = await CreateHostAsync();
        var now = host.Clock.GetUtcNow();
        var wt = typeof(CrawlJob).FullName!;
        await host.Ledger.AppendMany(Enumerable.Range(0, 3).Select(i => new JobRecord
        {
            Id = $"{wt}:q{i}", WorkType = wt, WorkId = $"q{i}", Action = CrawlJob.Crawl,
            Status = JobStatus.Queued, Lane = "crawl", VisibleAt = now.AddMinutes(-1), FirstSubmittedAt = now.AddMinutes(-1),
        }).ToList(), default);
        await host.Ledger.AppendMany(Enumerable.Range(0, 2).Select(i => new JobRecord
        {
            Id = $"{wt}:r{i}", WorkType = wt, WorkId = $"r{i}", Action = CrawlJob.Crawl,
            Status = JobStatus.Running, Lane = "crawl", Owner = "n", LeaseUntil = now.AddMinutes(1),
            VisibleAt = now.AddMinutes(-2), FirstSubmittedAt = now.AddMinutes(-2),
        }).ToList(), default);

        var r = await JobsHealth(host).Check();

        r.State.Should().Be(HealthState.Healthy);
        r.Data!["queued"].Should().Be(3L);
        r.Data!["running"].Should().Be(2L);
    }

    [Fact]
    public async Task jobs_health_flips_degraded_when_oldest_queued_age_exceeds_budget()
    {
        await using var host = await CreateHostAsync(o => o.QueueAgeWarning = TimeSpan.FromMinutes(5));
        var now = host.Clock.GetUtcNow();
        var wt = typeof(CrawlJob).FullName!;
        await host.Ledger.Append(new JobRecord
        {
            Id = $"{wt}:old", WorkType = wt, WorkId = "old", Action = CrawlJob.Crawl,
            Status = JobStatus.Queued, Lane = "crawl", VisibleAt = now.AddMinutes(-10), FirstSubmittedAt = now.AddMinutes(-10),
        }, default);

        var r = await JobsHealth(host).Check();

        r.State.Should().Be(HealthState.Degraded, "an oldest queued job past the budget is the starvation tripwire");
        r.Description.Should().Contain("budget");
    }

    [Fact]
    public async Task jobs_health_stays_healthy_when_budget_disabled_even_with_old_queued_work()
    {
        await using var host = await CreateHostAsync();   // QueueAgeWarning default = Zero (off)
        var now = host.Clock.GetUtcNow();
        var wt = typeof(CrawlJob).FullName!;
        await host.Ledger.Append(new JobRecord
        {
            Id = $"{wt}:old", WorkType = wt, WorkId = "old", Action = CrawlJob.Crawl,
            Status = JobStatus.Queued, Lane = "crawl", VisibleAt = now.AddMinutes(-30), FirstSubmittedAt = now.AddMinutes(-30),
        }, default);

        var r = await JobsHealth(host).Check();

        r.State.Should().Be(HealthState.Healthy, "the budget is opt-in — facts are always published, but no Degraded without one");
        r.Data!["oldestQueuedAgeSeconds"].Should().Be(1800L);
    }

    [Fact]
    public async Task jobs_health_counts_lapsed_lease_reclaim_backlog()
    {
        await using var host = await CreateHostAsync();
        var now = host.Clock.GetUtcNow();
        var wt = typeof(CrawlJob).FullName!;
        await host.Ledger.Append(new JobRecord
        {
            Id = $"{wt}:stuck", WorkType = wt, WorkId = "stuck", Action = CrawlJob.Crawl,
            Status = JobStatus.Running, Lane = "crawl", Owner = "dead-node", LeaseUntil = now.AddMinutes(-1),
            VisibleAt = now.AddMinutes(-5), FirstSubmittedAt = now.AddMinutes(-5),
        }, default);

        var r = await JobsHealth(host).Check();

        r.Data!["reclaimBacklog"].Should().Be(1L);
    }

    [Fact]
    public async Task active_gate_does_not_starve_ungated_claims()
    {
        await using var host = await CreateHostAsync(o => o.ClaimScanBatch = 3);
        var now = host.Clock.GetUtcNow();
        var wt = typeof(GreetJob).FullName!;

        await host.Ledger.SetGate("host:throttled", now.AddMinutes(10), "429", default);

        // 8 gated jobs (under the active gate) hold the oldest VisibleAt.
        await host.Ledger.AppendMany(
            Enumerable.Range(0, 8).Select(i => new JobRecord
            {
                Id = $"{wt}:gated{i}", WorkType = wt, WorkId = $"gated{i}", Action = "",
                Status = JobStatus.Queued, GateKey = "host:throttled", Exclusive = true,
                VisibleAt = now.AddMinutes(-10).AddMilliseconds(i), FirstSubmittedAt = now.AddMinutes(-10).AddMilliseconds(i),
            }).ToList(), default);

        await host.Ledger.Append(new JobRecord
        {
            Id = $"{wt}:ungated0", WorkType = wt, WorkId = "ungated0", Action = "",
            Status = JobStatus.Queued, Exclusive = true, VisibleAt = now, FirstSubmittedAt = now,
        }, default);

        var claimed = await host.Ledger.ClaimNext("owner", now, now.AddMinutes(1), Array.Empty<string>(), default);

        claimed.Should().NotBeNull("an active gate must not block claims for ungated work");
        claimed!.WorkId.Should().Be("ungated0");
    }

    [Fact]
    public async Task busy_exclusive_work_item_does_not_starve_other_work_items()
    {
        // The exclusive/busy vector is a set of (WorkType, WorkId) TUPLES — it can't be a scalar store-side filter, so
        // paging (not predicate-pushdown) is what keeps it from starving. One work-item runs; a backlog of its own
        // queued duplicates must not block a different work-item behind them.
        await using var host = await CreateHostAsync(o => o.ClaimScanBatch = 3);
        var now = host.Clock.GetUtcNow();
        var wt = typeof(GreetJob).FullName!;

        await host.Ledger.Append(new JobRecord
        {
            Id = $"{wt}:hot:running", WorkType = wt, WorkId = "hot", Action = "",
            Status = JobStatus.Running, Exclusive = true, Owner = "other", LeaseUntil = now.AddMinutes(10),
            VisibleAt = now.AddMinutes(-20), FirstSubmittedAt = now.AddMinutes(-20),
        }, default);

        // 8 more exclusive jobs queued for the SAME (busy) work-item, oldest VisibleAt.
        await host.Ledger.AppendMany(
            Enumerable.Range(0, 8).Select(i => new JobRecord
            {
                Id = $"{wt}:hot:q{i}", WorkType = wt, WorkId = "hot", Action = "",
                Status = JobStatus.Queued, Exclusive = true,
                VisibleAt = now.AddMinutes(-10).AddMilliseconds(i), FirstSubmittedAt = now.AddMinutes(-10).AddMilliseconds(i),
            }).ToList(), default);

        // a different work-item, queued behind them.
        await host.Ledger.Append(new JobRecord
        {
            Id = $"{wt}:cold:q0", WorkType = wt, WorkId = "cold", Action = "",
            Status = JobStatus.Queued, Exclusive = true, VisibleAt = now, FirstSubmittedAt = now,
        }, default);

        var claimed = await host.Ledger.ClaimNext("owner", now, now.AddMinutes(1), Array.Empty<string>(), default);

        claimed.Should().NotBeNull("a busy exclusive work-item must not block claims for other work-items");
        claimed!.WorkId.Should().Be("cold");
    }

    [Fact]
    public async Task claim_preserves_fifo_among_claimable_jobs_behind_an_unclaimable_head()
    {
        // Paging past the unclaimable head must not disturb FIFO order among the claimable jobs it reaches.
        await using var host = await CreateHostAsync(
            o => o.ClaimScanBatch = 3,
            s => s.AddSingleton<IJobPoolResolver>(new TestPoolResolver(Array.Empty<string>())));
        var now = host.Clock.GetUtcNow();
        var poolWt = typeof(PoolJob).FullName!;
        var wt = typeof(GreetJob).FullName!;

        await host.Ledger.AppendMany(
            Enumerable.Range(0, 8).Select(i => new JobRecord
            {
                Id = $"{poolWt}:stuck{i}", WorkType = poolWt, WorkId = $"stuck{i}", Action = "",
                Status = JobStatus.Queued, PoolKey = "test-ai-servers", Exclusive = true,
                VisibleAt = now.AddMinutes(-10).AddMilliseconds(i), FirstSubmittedAt = now.AddMinutes(-10).AddMilliseconds(i),
            }).ToList(), default);

        // claimable non-pool jobs in a known order: newer than the stuck head, but still visible (in the past).
        await host.Ledger.AppendMany(
            Enumerable.Range(0, 3).Select(i => new JobRecord
            {
                Id = $"{wt}:r{i}", WorkType = wt, WorkId = $"r{i}", Action = "",
                Status = JobStatus.Queued, Exclusive = true,
                VisibleAt = now.AddMinutes(-1).AddMilliseconds(i), FirstSubmittedAt = now.AddMinutes(-1).AddMilliseconds(i),
            }).ToList(), default);

        var pools = new Dictionary<string, PoolDispatchContext>
        {
            ["test-ai-servers"] = new("test-ai-servers", Array.Empty<string>(), 1),
        };
        var first  = await host.Ledger.ClaimNext("o", now, now.AddMinutes(1), Array.Empty<string>(), default, pools);
        var second = await host.Ledger.ClaimNext("o", now, now.AddMinutes(1), Array.Empty<string>(), default, pools);

        first!.WorkId.Should().Be("r0", "the FIFO head of the claimable set is claimed first");
        second!.WorkId.Should().Be("r1");
    }

    [Fact]
    public async Task exhausted_pool_backlog_larger_than_scan_window_returns_null_without_claiming()
    {
        // The paging scan must terminate at end-of-ready-set and return null when nothing is claimable — it must not
        // hang, and it must not over-claim an unclaimable pool job.
        PoolJob.Reset();
        var resolver = new TestPoolResolver(Array.Empty<string>());
        await using var host = await CreateHostAsync(
            o => o.ClaimScanBatch = 3,
            s => s.AddSingleton<IJobPoolResolver>(resolver));
        var now = host.Clock.GetUtcNow();
        var poolWt = typeof(PoolJob).FullName!;

        await host.Ledger.AppendMany(
            Enumerable.Range(0, 10).Select(i => new JobRecord
            {
                Id = $"{poolWt}:p{i}", WorkType = poolWt, WorkId = $"p{i}", Action = "",
                Status = JobStatus.Queued, PoolKey = "test-ai-servers", Exclusive = true,
                VisibleAt = now.AddMinutes(-5).AddMilliseconds(i), FirstSubmittedAt = now.AddMinutes(-5).AddMilliseconds(i),
            }).ToList(), default);

        var pools = new Dictionary<string, PoolDispatchContext>
        {
            ["test-ai-servers"] = new("test-ai-servers", Array.Empty<string>(), 1),
        };
        var claimed = await host.Ledger.ClaimNext("owner", now, now.AddMinutes(1), Array.Empty<string>(), default, pools);

        claimed.Should().BeNull("no member is available — the scan reaches end-of-ready-set and returns null");
        (await host.Ledger.Query(new JobQuery(WorkType: poolWt), default))
            .Should().OnlyContain(r => r.Status == JobStatus.Queued, "nothing was claimed");
    }

    private static IReadOnlyCollection<JobRecord> Seed(string workType, int count, JobStatus status, DateTimeOffset baseTime, string idPrefix, bool visible = false)
    {
        var list = new List<JobRecord>(count);
        for (var i = 0; i < count; i++)
        {
            var at = status >= JobStatus.Completed ? baseTime.AddSeconds(-i - 1)
                   : visible ? baseTime.AddMilliseconds(i)   // all in the past, strictly ordered (q0 = FIFO head)
                   : baseTime;
            list.Add(Rec(workType, $"{idPrefix}{i}", status, at));
        }
        return list;
    }

    private static JobRecord Rec(string workType, string workId, JobStatus status, DateTimeOffset at) => new()
    {
        Id = $"{workType}:{workId}",
        WorkType = workType,
        WorkId = workId,
        Action = "",
        Status = status,
        VisibleAt = at,
        FirstSubmittedAt = at,
        LastSettledAt = status >= JobStatus.Completed ? at : null,
        Exclusive = true,
    };
}
