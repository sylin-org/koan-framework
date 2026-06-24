using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
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

    [Fact]
    public async Task A_bag_naming_an_unregistered_axis_dead_letters_and_never_runs()
    {
        TenantGatedProbe.Reset();
        await using var host = await JobsHarness.StartSqliteAsync();
        var j = new TenantGatedProbe { Resource = "x" };
        var id = j.Id;
        await j.Job.Submit();

        // Inject a carrier bag naming an axis with no registered carrier (simulating a peer module absent on this
        // node). Restore must fail closed — the job dead-letters, the handler never runs fail-open.
        var ledger = host.Services.GetRequiredService<IJobLedger>();
        var rec = await host.JobFor<TenantGatedProbe>(id);
        rec!.AmbientCarrier = new Dictionary<string, string> { ["koan:ghost"] = "v1:whatever" };
        await ledger.Update(rec, default);

        await host.Drain();

        TenantGatedProbe.Executions.Should().Be(0);                                  // never ran fail-open
        (await host.StatusOf<TenantGatedProbe>(id)).Should().BeOneOf(JobStatus.Failed, JobStatus.Dead);
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
