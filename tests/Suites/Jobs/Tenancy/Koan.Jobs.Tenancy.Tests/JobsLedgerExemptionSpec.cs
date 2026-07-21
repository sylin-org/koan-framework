using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Abstractions;
using Koan.Data.Core.Model;
using Koan.Jobs;
using Koan.Jobs.TestKit;
using Koan.Tenancy;
using Xunit;

namespace Koan.Jobs.Tenancy.Tests;

/// <summary>
/// ARCH-0100 §6 / phase 3 — the durable job ledger is <b>ambient-exempt infrastructure</b>. Proven through a real
/// <c>AddKoan()</c> boot (ARCH-0079) with the <c>Koan.Tenancy</c> module live at the secure-by-default
/// <b>Closed</b> posture (Test env). The ledger entities, including Jobs' internal metric row, are written during
/// settle and read at claim with NO
/// ambient tenant on the worker thread — so if they were tenant-scoped, claiming would throw the fail-closed guard
/// and the cooperative gate would fail open cross-tenant. They must carry the generic <see cref="IAmbientExempt"/>
/// marker so the restored ambient never stamps them and claim-time reads are never tenant-filtered — without a
/// <c>Koan.Jobs → Koan.Tenancy</c> dependency.
/// </summary>
public sealed class JobsLedgerExemptionSpec
{
    [Fact]
    public void All_jobs_ledger_entities_are_recognized_exempt_by_the_tenancy_predicate()
    {
        // The single cached exemption predicate (used by both the guard and the __koan_tenant managed field) must
        // treat each ledger entity as exempt via the generic marker — covers the internal metric row, which the
        // lifecycle test below does not separately drive.
        var metricRow = typeof(JobRecord).Assembly.GetType("Koan.Jobs.JobMetric", throwOnError: true)!;
        TenantScopeMetadata.IsHostScopedType(typeof(JobRecord)).Should().BeTrue();
        metricRow.IsNotPublic.Should().BeTrue();
        TenantScopeMetadata.IsHostScopedType(metricRow).Should().BeTrue();
        TenantScopeMetadata.IsHostScopedType(typeof(JobGateRecord)).Should().BeTrue();
    }

    [Fact]
    public async Task A_tenant_scoped_durable_job_claims_gates_and_settles_with_no_ledger_leak()
    {
        TenantGatedProbe.Reset();
        TenantGatedProbe.BackOffOnce = true;

        // Real AddKoan boot: SQLite durable tier + Koan.Tenancy live (Closed posture in Test env), worker off.
        await using var host = await JobsHarness.StartSqliteAsync();

        var j = new TenantGatedProbe { Resource = "api" };
        var id = j.Id;

        // Submit INSIDE a tenant scope — the ledger row written here (under the acme ambient) must NOT be stamped.
        using (Tenant.Use("acme")) await j.Job.Submit();

        // Drain runs claim + execute + settle on the worker thread with NO ambient tenant. First pass backs off
        // (writes a JobGateRecord); without the exemption, ClaimNext/SetGate would throw the no-tenant guard.
        await host.Drain();
        host.Advance(TimeSpan.FromMinutes(6));   // let the gate expire
        await host.Drain();                      // second claim → completes

        TenantGatedProbe.Executions.Should().Be(2);
        (await host.StatusOf<TenantGatedProbe>(id)).Should().Be(JobStatus.Completed);

        // The ledger row is exempt: visible (not tenant-filtered) from a DIFFERENT tenant scope — proving it was
        // never stamped with acme and the claim-path reads are never filtered.
        using (Tenant.Use("globex")) (await host.JobFor<TenantGatedProbe>(id)).Should().NotBeNull();
    }
}

/// <summary>
/// A durable job whose work-item is itself <see cref="IAmbientExempt"/> — so this spec isolates the variable under
/// test to the three <i>ledger</i> entities, not the work-item rehydration. It backs off once via a
/// declared <c>[JobGate]</c> so the JobGateRecord write is exercised through the real path.
/// </summary>
[JobGate(nameof(Resource))]
public sealed class TenantGatedProbe : Entity<TenantGatedProbe>, IKoanJob<TenantGatedProbe>, IAmbientExempt
{
    public string Resource { get; set; } = "shared";
    public static int Executions;
    public static bool BackOffOnce;

    public static Task Execute(TenantGatedProbe job, JobContext ctx, CancellationToken ct)
    {
        Interlocked.Increment(ref Executions);
        if (BackOffOnce) { BackOffOnce = false; ctx.Backoff(TimeSpan.FromMinutes(5)); }
        return Task.CompletedTask;
    }

    public static void Reset() { Executions = 0; BackOffOnce = false; }
}
