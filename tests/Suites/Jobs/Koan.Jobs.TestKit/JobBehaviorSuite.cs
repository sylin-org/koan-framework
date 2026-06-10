using AwesomeAssertions;
using Koan.Jobs;
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
    protected abstract Task<JobsHarness> CreateHostAsync(Action<JobsOptions>? configure = null);

    // --- discovery + edge ---

    [Fact]
    public async Task discovery_binds_job_types()
    {
        await using var host = await CreateHostAsync();
        host.Registry.Count.Should().BeGreaterThan(0);
        host.Registry.Get(typeof(GreetJob).FullName!).Should().NotBeNull();
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
    public async Task batch_submit_runs_every_work_item()
    {
        GreetJob.Reset();
        await using var host = await CreateHostAsync();
        var jobs = Enumerable.Range(0, 25).Select(i => new GreetJob { Name = $"n{i}" }).ToList();
        await jobs.Submit();
        await host.Drain();
        GreetJob.Executions.Should().Be(25);
    }

    [Fact]
    public async Task idempotent_submit_coalesces_duplicates()
    {
        DedupeJob.Reset();
        await using var host = await CreateHostAsync();
        await new DedupeJob { Key = "k1" }.Job.Submit();
        await new DedupeJob { Key = "k1" }.Job.Submit();
        await new DedupeJob { Key = "k2" }.Job.Submit();
        await host.Drain();
        DedupeJob.Executions.Should().Be(2);
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
    public async Task continuous_schedule_fires_every_tick()
    {
        Heartbeat.Reset();
        await using var host = await CreateHostAsync();
        await host.TriggerDue(); await host.Drain();
        await host.TriggerDue(); await host.Drain();
        await host.TriggerDue(); await host.Drain();
        Heartbeat.Executions.Should().Be(3);
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
