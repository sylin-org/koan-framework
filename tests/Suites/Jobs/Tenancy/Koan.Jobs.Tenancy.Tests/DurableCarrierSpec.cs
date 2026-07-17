using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core.Diagnostics;
using Koan.Core.Semantics.Segmentation;
using Koan.Data.Abstractions;
using Koan.Data.Core.Model;
using Koan.Jobs;
using Koan.Jobs.TestKit;
using Koan.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Jobs.Tenancy.Tests;

/// <summary>
/// ARCH-0100 phases 5–6 — the durable carrier end to end through a real <c>AddKoan()</c> boot (SQLite durable tier
/// + <c>Koan.Tenancy</c> live, Closed posture). The ambient tenant is captured at submit, persisted on the (exempt)
/// <see cref="JobRecord"/>, and rehydrated before <c>binding.Load</c> so the handler — and its tenant-scoped
/// work-item reads/writes — run in the tenant the job was submitted under, across the async-hop. Covers capture,
/// restore, the load-through-settle wrapping, chain-successor propagation, the null-bag (unscoped) path, and
/// fail-closed on a captured-but-unregistered axis.
/// </summary>
public sealed class DurableCarrierSpec
{
    [Fact]
    public async Task Active_host_reports_jobs_logical_context_without_overstating_ledger_or_delivery()
    {
        await using var host = await JobsHarness.StartSqliteAsync();
        var facts = host.Services.GetRequiredService<IKoanRuntimeFacts>().Current.Facts;

        facts.Should().Contain(fact =>
            fact.Code == "koan.segmentation.realization.active"
            && fact.Subject == "segmentation:jobs"
            && fact.Kind == KoanFactKind.Guarantee
            && fact.Summary.Contains("durable-context-carriage", StringComparison.Ordinal));
        facts.Should().Contain(fact =>
            fact.Code == "koan.jobs.context.guarantees"
            && fact.Subject == "jobs:context"
            && fact.Kind == KoanFactKind.Guarantee
            && fact.Summary.Contains("host-trusted", StringComparison.Ordinal)
            && fact.Summary.Contains("shared control-plane", StringComparison.Ordinal)
            && fact.Summary.Contains("at-least-once", StringComparison.Ordinal)
            && fact.Summary.Contains("context-free latency hint", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Submit_under_a_tenant_captures_the_tenant_bag_on_the_job_record()
    {
        await using var host = await JobsHarness.StartSqliteAsync();
        var j = new TenantObservingJob();
        var id = j.Id;

        using (Tenant.Use("acme")) await j.Job.Submit();

        var rec = await host.JobFor<TenantObservingJob>(id);   // JobRecord is exempt → readable with no tenant
        rec.Should().NotBeNull();
        rec!.AmbientCarrier.Should().NotBeNull();
        rec.AmbientCarrier!.Should().ContainKey("koan:tenant").WhoseValue.Should().Be("v1:id:acme");
    }

    [Fact]
    public async Task Unscoped_tenant_job_is_refused_before_work_or_ledger_persistence()
    {
        await using var host = await JobsHarness.StartSqliteAsync();
        var work = new TenantObservingJob();

        var submit = () => work.Job.Submit();

        var failure = (await submit.Should().ThrowAsync<SegmentationRequiredException>()).Which;
        failure.DimensionId.Should().Be("tenant");
        using (Tenant.Use("acme"))
            (await TenantObservingJob.Get(work.Id)).Should().BeNull();
        (await host.JobFor<TenantObservingJob>(work.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Async_source_submission_seals_one_tenant_context_for_every_item()
    {
        await using var host = await JobsHarness.StartSqliteAsync();
        var first = new TenantObservingJob();
        var second = new TenantObservingJob();

        async IAsyncEnumerable<TenantObservingJob> Source()
        {
            yield return first;
            using (Tenant.Use("globex"))
            {
                yield return second;
            }
            await Task.CompletedTask;
        }

        JobSubmission submission;
        using (Tenant.Use("acme"))
        {
            submission = await Source().Submit();
        }

        submission.Accepted.Should().Be(2);
        var firstRecord = await host.JobFor<TenantObservingJob>(first.Id);
        var secondRecord = await host.JobFor<TenantObservingJob>(second.Id);
        firstRecord!.AmbientCarrier!["koan:tenant"].Should().Be("v1:id:acme");
        secondRecord!.AmbientCarrier!["koan:tenant"].Should().Be("v1:id:acme");
        using (Tenant.Use("acme"))
        {
            (await TenantObservingJob.Get(first.Id)).Should().NotBeNull();
            (await TenantObservingJob.Get(second.Id)).Should().NotBeNull();
        }
        using (Tenant.Use("globex"))
        {
            (await TenantObservingJob.Get(second.Id)).Should().BeNull();
        }
    }

    [Fact]
    public async Task Unscoped_submit_persists_a_null_bag()
    {
        TenantGatedProbe.Reset();   // exempt work-item → an unscoped submit is allowed under Closed posture
        await using var host = await JobsHarness.StartSqliteAsync();
        var j = new TenantGatedProbe { Resource = "x" };
        var id = j.Id;

        await j.Job.Submit();   // no tenant scope

        (await host.JobFor<TenantGatedProbe>(id))!.AmbientCarrier.Should().BeNull();
    }

    [Fact]
    public async Task Handler_runs_in_the_submitted_tenant_and_its_work_item_round_trips_in_scope()
    {
        TenantObservingJob.Reset();
        await using var host = await JobsHarness.StartSqliteAsync();
        var j = new TenantObservingJob();
        var id = j.Id;

        using (Tenant.Use("acme")) await j.Job.Submit();
        await host.Drain();   // claim + load + execute + settle on the worker thread (no ambient tenant)

        TenantObservingJob.ObservedTenant.Should().Be("acme");                       // restore worked
        (await host.StatusOf<TenantObservingJob>(id)).Should().Be(JobStatus.Completed);
        // the work-item was loaded from and auto-saved to acme's partition
        using (Tenant.Use("acme")) (await TenantObservingJob.Get(id))!.Note.Should().Be("ran");
        using (Tenant.Use("globex")) (await TenantObservingJob.Get(id)).Should().BeNull();
    }

    [Fact]
    public async Task A_chain_carries_the_tenant_into_every_stage()
    {
        TenantChainJob.Reset();
        await using var host = await JobsHarness.StartSqliteAsync();
        var j = new TenantChainJob();
        var id = j.Id;

        using (Tenant.Use("acme")) await j.Job.Submit("s1");   // chains start at their first declared stage
        await host.Drain();   // stage s1 runs, advances to s2; s2's record must carry the parent's bag

        TenantChainJob.Observed.Should().Equal("acme", "acme");   // BOTH stages observed the submitted tenant
        (await host.StatusOf<TenantChainJob>(id)).Should().Be(JobStatus.Completed);
    }

    [Theory]
    [InlineData("koan:ghost", "v1:whatever")]
    [InlineData("koan:tenant", "v1:id:")]
    [InlineData("koan:tenant", "v999:private-payload")]
    public async Task An_unknown_malformed_or_unsupported_bag_dead_letters_before_the_handler(
        string axisKey,
        string payload)
    {
        TenantGatedProbe.Reset();
        await using var host = await JobsHarness.StartSqliteAsync();
        var j = new TenantGatedProbe { Resource = "x" };
        var id = j.Id;
        await j.Job.Submit();

        // Simulate a peer module absent on this node, malformed data, or a future carrier version. Every refusal must
        // happen before work-item load/handler invocation and persist only the registry's safe failure message.
        var ledger = host.Services.GetRequiredService<IJobLedger>();
        var rec = await host.JobFor<TenantGatedProbe>(id);
        rec!.AmbientCarrier = new Dictionary<string, string> { [axisKey] = payload };
        await ledger.Update(rec, default);

        await host.Drain();

        TenantGatedProbe.Executions.Should().Be(0);                                  // never ran fail-open
        var settled = await host.JobFor<TenantGatedProbe>(id);
        settled!.Status.Should().Be(JobStatus.Dead);
        settled.DeadReason.Should().Be(DeadReason.CarrierRestoreFailed.ToString());
        settled.LastError.Should().NotContain(payload);
    }

    [Fact]
    public async Task Concurrently_running_jobs_for_different_tenants_do_not_cross_contaminate()
    {
        // The strongest isolation proof: two jobs for DIFFERENT tenants held at a barrier so they are genuinely
        // in-flight at the same time, each re-reading the ambient tenant AFTER an await (a suspension point) — so
        // a leak across the AsyncLocal restore scopes would be observable. Neither must see the other's tenant.
        TenantBarrierJob.Reset();
        await using var host = await JobsHarness.StartSqliteAsync(o => o.WorkerConcurrency = 2);

        using (Tenant.Use("acme")) await new TenantBarrierJob { Slot = "a" }.Job.Submit();
        using (Tenant.Use("globex")) await new TenantBarrierJob { Slot = "b" }.Job.Submit();

        var drain = host.Drain();
        (await TenantBarrierJob.Arrived!.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();
        (await TenantBarrierJob.Arrived!.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();   // both in-flight
        TenantBarrierJob.Release!.Release(2);                                                       // let both finish
        await drain;

        TenantBarrierJob.Seen["a"].Should().Be("acme");
        TenantBarrierJob.Seen["b"].Should().Be("globex");
    }

    [Fact]
    public async Task Two_tenants_same_idempotent_work_do_not_coalesce_and_each_runs_in_its_own_ambient()
    {
        // The cross-tenant coalesce hole: a tenant-blind [JobIdempotent] key would let globex's submit collapse
        // onto acme's queued job and run once in acme's ambient. The captured ambient is folded into the coalesce
        // identity, so the two are distinct work and each runs in its own tenant.
        TenantIdempotentJob.Reset();
        await using var host = await JobsHarness.StartSqliteAsync();

        string idA, idB;
        using (Tenant.Use("acme")) { var j = new TenantIdempotentJob { Key = "k1" }; idA = j.Id; await j.Job.Submit(); }
        using (Tenant.Use("globex")) { var j = new TenantIdempotentJob { Key = "k1" }; idB = j.Id; await j.Job.Submit(); }
        idA.Should().NotBe(idB);

        await host.Drain();

        TenantIdempotentJob.Observed.Should().BeEquivalentTo(new[] { "acme", "globex" });
    }

    [Fact]
    public async Task A_drain_running_inside_a_tenant_scope_does_not_leak_into_an_unscoped_job()
    {
        // The null-bag inherit hole: an unscoped job's restore must EXPLICITLY clear the axis, not inherit the
        // drain thread's ambient (e.g. an inline drain inside a caller's Tenant.Use scope). The work-item is exempt
        // so it can be submitted unscoped under Closed posture.
        ExemptObservingJob.Reset();
        await using var host = await JobsHarness.StartSqliteAsync();

        await new ExemptObservingJob().Job.Submit();          // unscoped → null bag

        using (Tenant.Use("acme")) await host.Drain();        // the drain thread carries acme

        ExemptObservingJob.ObservedSet.Should().BeTrue();
        ExemptObservingJob.Observed.Should().BeNull();        // suppressed, NOT inherited from the drain thread
    }

    [Fact]
    public async Task A_retried_tenant_job_re_restores_the_tenant_on_the_retry_attempt()
    {
        // The 'different node / restart' claim made concrete for retry: the bag persists on the ledger row, so a
        // re-claimed attempt re-restores the captured tenant.
        TenantRetryJob.Reset();
        await using var host = await JobsHarness.StartSqliteAsync();

        using (Tenant.Use("acme")) await new TenantRetryJob().Job.Submit(TenantRetryJob.Work);
        await host.Drain();                                   // attempt 1 throws → requeued for retry
        host.Advance(TimeSpan.FromMinutes(5));
        await host.Drain();                                   // attempt 2 succeeds

        TenantRetryJob.ObservedPerAttempt.Count.Should().BeGreaterThanOrEqualTo(2);
        TenantRetryJob.ObservedPerAttempt.Should().OnlyContain(t => t == "acme");
    }
}

/// <summary>A tenant-scoped work-item (NOT exempt): records the ambient tenant it observes, and writes itself — so
/// the spec proves the handler + its work-item load/save run in the rehydrated tenant.</summary>
public sealed class TenantObservingJob : Entity<TenantObservingJob>, IKoanJob<TenantObservingJob>
{
    public string? Note { get; set; }
    public static string? ObservedTenant;

    public static Task Execute(TenantObservingJob job, JobContext ctx, CancellationToken ct)
    {
        ObservedTenant = Tenant.Current?.Id;
        job.Note = "ran";
        return Task.CompletedTask;
    }

    public static void Reset() => ObservedTenant = null;
}

/// <summary>A tenant-scoped two-stage chain: records the observed tenant on each stage to prove the bag propagates
/// to the chain successor the orchestrator (not the coordinator) appends.</summary>
[JobChain("s1", "s2")]
public sealed class TenantChainJob : Entity<TenantChainJob>, IKoanJob<TenantChainJob>
{
    public static readonly List<string?> Observed = new();

    public static Task Execute(TenantChainJob job, JobContext ctx, CancellationToken ct)
    {
        lock (Observed) Observed.Add(Tenant.Current?.Id);
        return Task.CompletedTask;
    }

    public static void Reset() { lock (Observed) Observed.Clear(); }
}

/// <summary>A tenant-scoped job that parks at a barrier so two instances for different tenants run concurrently,
/// then re-reads its ambient tenant after the await to prove the restore scopes do not cross-contaminate.</summary>
public sealed class TenantBarrierJob : Entity<TenantBarrierJob>, IKoanJob<TenantBarrierJob>
{
    public string Slot { get; set; } = "";
    public static readonly ConcurrentDictionary<string, string?> Seen = new();
    public static SemaphoreSlim? Arrived;
    public static SemaphoreSlim? Release;

    public static async Task Execute(TenantBarrierJob job, JobContext ctx, CancellationToken ct)
    {
        Arrived!.Release();                                   // announce arrival
        await Release!.WaitAsync(TimeSpan.FromSeconds(15), ct);   // hold until both are in-flight (suspension point)
        Seen[job.Slot] = Tenant.Current?.Id;                  // re-read AFTER the await — must still be our own tenant
    }

    public static void Reset()
    {
        Seen.Clear();
        Arrived = new SemaphoreSlim(0);
        Release = new SemaphoreSlim(0);
    }
}

/// <summary>A tenant-scoped idempotent job: records the ambient it ran in, to prove two tenants' same-key submits
/// do not coalesce and each runs in its own tenant.</summary>
[JobIdempotent(nameof(Key))]
public sealed class TenantIdempotentJob : Entity<TenantIdempotentJob>, IKoanJob<TenantIdempotentJob>
{
    public string Key { get; set; } = "";
    public static readonly ConcurrentBag<string?> Observed = new();

    public static Task Execute(TenantIdempotentJob job, JobContext ctx, CancellationToken ct)
    {
        Observed.Add(Tenant.Current?.Id);
        return Task.CompletedTask;
    }

    public static void Reset() => Observed.Clear();
}

/// <summary>An ambient-exempt observing job — can be submitted unscoped under Closed posture; records the ambient
/// it observes, to prove an unscoped job does not inherit the drain thread's tenant.</summary>
public sealed class ExemptObservingJob : Entity<ExemptObservingJob>, IKoanJob<ExemptObservingJob>, IAmbientExempt
{
    public static string? Observed;
    public static bool ObservedSet;

    public static Task Execute(ExemptObservingJob job, JobContext ctx, CancellationToken ct)
    {
        Observed = Tenant.Current?.Id;
        ObservedSet = true;
        return Task.CompletedTask;
    }

    public static void Reset() { Observed = null; ObservedSet = false; }
}

/// <summary>A tenant-scoped job that fails its first attempt and succeeds on retry, recording the ambient on each
/// attempt — proving the carrier re-restores the captured tenant on a re-claimed attempt.</summary>
[JobAction(Work, MaxAttempts = 3)]
public sealed class TenantRetryJob : Entity<TenantRetryJob>, IKoanJob<TenantRetryJob>
{
    public const string Work = "work";
    public static readonly ConcurrentBag<string?> ObservedPerAttempt = new();

    public static Task Execute(TenantRetryJob job, JobContext ctx, CancellationToken ct)
    {
        ObservedPerAttempt.Add(Tenant.Current?.Id);
        if (ctx.State.Attempt < 2) throw new InvalidOperationException("transient");
        return Task.CompletedTask;
    }

    public static void Reset() => ObservedPerAttempt.Clear();
}
