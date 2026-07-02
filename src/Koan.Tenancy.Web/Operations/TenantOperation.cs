using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Jobs;

namespace Koan.Tenancy.Web.Operations;

/// <summary>The outcome of a <see cref="TenantOperation"/> as the operations feed sees it (distinct from the
/// underlying <c>Koan.Jobs</c> ledger status).</summary>
public enum TenantOperationStatus
{
    /// <summary>Submitted, not yet run.</summary>
    Pending = 0,

    /// <summary>Ran to completion (see the removed-row counts).</summary>
    Completed = 1,

    /// <summary>The last attempt threw (see <see cref="TenantOperation.Error"/>); the job may still retry.</summary>
    Failed = 2,
}

/// <summary>
/// A durable, resumable, audited tenant lifecycle operation (ARCH-0099 lifecycle-ops-are-jobs / ARCH-0104) — a
/// <c>[HostScoped]</c> <see cref="Entity{T}"/> that is also an <see cref="IKoanJob{T}"/>, so it rides the
/// <c>Koan.Jobs</c> ledger for free retry/idempotency/audit. It survives the erase it performs (the operation
/// record is the durable proof), and it is the operations feed the console projects.
///
/// <para>v1 implements <see cref="Erase"/> — a <b>control-plane-only</b> erase (memberships + invites + the
/// record; the tenant's product data fan-out erase and the signed erasure certificate are the deferred full saga,
/// ARCH-0099 P5b/P8). Re-running erase is idempotent (already-deleted rows are simply absent), so a retry after a
/// partial failure converges.</para>
/// </summary>
[HostScoped]
[JobAction(TenantOperation.Erase, Timeout = "00:05:00", MaxAttempts = 3)]
public sealed class TenantOperation : Entity<TenantOperation>, IKoanJob<TenantOperation>
{
    /// <summary>The control-plane erase action.</summary>
    public const string Erase = nameof(Erase);

    /// <summary>The tenant this operation targets (a <see cref="TenantRecord"/> id).</summary>
    public string TenantId { get; set; } = "";

    /// <summary>The kind of operation (v1: always <see cref="Erase"/>).</summary>
    public string Action { get; set; } = Erase;

    /// <summary>The operator who requested it (audit attribution).</summary>
    public string RequestedBy { get; set; } = "";

    /// <summary>The feed-facing outcome.</summary>
    public TenantOperationStatus Status { get; set; } = TenantOperationStatus.Pending;

    /// <summary>How many memberships the erase removed.</summary>
    public int RemovedMemberships { get; set; }

    /// <summary>How many invites the erase removed.</summary>
    public int RemovedInvites { get; set; }

    /// <summary>Whether the tenant record itself was removed.</summary>
    public bool RemovedTenant { get; set; }

    /// <summary>The last error message when <see cref="Status"/> is <see cref="TenantOperationStatus.Failed"/>.</summary>
    public string? Error { get; set; }

    /// <summary>When the operation was requested (set once, on creation).</summary>
    [Timestamp]
    public DateTimeOffset RequestedAt { get; set; }

    /// <summary>When the operation finished (success or terminal failure); null while pending.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>The job handler — one action (Erase). Runs on the background worker under the Koan.Jobs runtime.</summary>
    public static async Task Execute(TenantOperation op, JobContext ctx, CancellationToken ct)
    {
        switch (ctx.Action)
        {
            case Erase:
                try
                {
                    await EraseControlPlane(op, ct).ConfigureAwait(false);
                    // op is mutated (Completed + counts); the orchestrator save-if-changed persists this reference.
                }
                catch (Exception ex)
                {
                    op.Status = TenantOperationStatus.Failed;
                    op.Error = ex.Message;
                    await op.Save(ct).ConfigureAwait(false); // persist the failure snapshot for the feed
                    await TenantAuditEntry.Record(op.RequestedBy, "tenant.erase.failed", op.TenantId, ex.Message, ct)
                        .ConfigureAwait(false);
                    throw; // let the orchestrator mark the job failed + apply the retry policy (erase is idempotent)
                }
                break;
        }
    }

    /// <summary>
    /// The testable erase core — deletes the tenant's control-plane rows (memberships, invites, the record),
    /// records the counts onto <paramref name="op"/>, and writes the <c>tenant.erased</c> audit entry. Idempotent:
    /// re-running against an already-erased tenant removes nothing and still completes. Mutates and returns
    /// <paramref name="op"/> (the caller persists it — the job via save-if-changed).
    /// </summary>
    public static async Task<TenantOperation> EraseControlPlane(TenantOperation op, CancellationToken ct = default)
    {
        var members = (await Membership.Query(m => m.TenantId == op.TenantId, ct).ConfigureAwait(false)).ToList();
        foreach (var m in members) await m.Remove(ct).ConfigureAwait(false);

        var invites = (await Invite.Query(i => i.TenantId == op.TenantId, ct).ConfigureAwait(false)).ToList();
        foreach (var i in invites) await i.Remove(ct).ConfigureAwait(false);

        var record = await TenantRecord.Get(op.TenantId, ct).ConfigureAwait(false);
        if (record is not null) await record.Remove(ct).ConfigureAwait(false);

        op.RemovedMemberships = members.Count;
        op.RemovedInvites = invites.Count;
        op.RemovedTenant = record is not null;
        op.Status = TenantOperationStatus.Completed;
        op.Error = null;
        op.CompletedAt = DateTimeOffset.UtcNow;

        var detail = $"control-plane erase removed {members.Count} membership(s), {invites.Count} invite(s)"
                     + (record is not null ? ", and the tenant record" : " (tenant record already absent)");
        await TenantAuditEntry.Record(op.RequestedBy, "tenant.erased", op.TenantId, detail, ct).ConfigureAwait(false);

        return op;
    }
}
