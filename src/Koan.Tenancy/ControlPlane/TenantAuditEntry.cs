using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core;
using Koan.Data.Core.Model;

namespace Koan.Tenancy;

/// <summary>
/// The append-only control-plane audit trail (ARCH-0099 / ARCH-0104) — a dogfooded <c>[HostScoped]</c>
/// <see cref="Entity{T}"/> that records every operator mutation of the tenant fleet: <c>actor / action /
/// tenant / summary / at</c>. It is the "explicit + audited cross-tenant" guardrail made structural — an
/// operator action that is not written here did not happen. Lives in the root/host scope alongside the other
/// control-plane rows, so it is never tenant-scoped.
/// </summary>
[HostScoped]
public sealed class TenantAuditEntry : Entity<TenantAuditEntry>
{
    /// <summary>Who performed the action — the operator principal (subject/name), or <c>system</c> for automated ops.</summary>
    public string Actor { get; set; } = "";

    /// <summary>The action verb, e.g. <c>tenant.created</c> / <c>tenant.suspended</c> / <c>membership.revoked</c> / <c>tenant.erase.requested</c>.</summary>
    public string Action { get; set; } = "";

    /// <summary>The tenant the action targeted (a <see cref="TenantRecord"/> id); null for a fleet-wide action.
    /// Indexed — the per-tenant audit view filters on it (pushed down, not scanned).</summary>
    [Index]
    public string? TenantId { get; set; }

    /// <summary>A short human-readable summary of what changed (never secrets).</summary>
    public string Summary { get; set; } = "";

    /// <summary>When the action occurred (set once, on creation).</summary>
    [Timestamp]
    public DateTimeOffset At { get; set; }

    /// <summary>
    /// Record an audit entry (one append-only row). The convenience path every lifecycle action funnels through so
    /// the "audited by construction" guardrail cannot be forgotten.
    /// </summary>
    public static Task<TenantAuditEntry> Record(string actor, string action, string? tenantId, string summary, CancellationToken ct = default)
        => new TenantAuditEntry
        {
            Actor = string.IsNullOrWhiteSpace(actor) ? "unknown" : actor,
            Action = action,
            TenantId = tenantId,
            Summary = summary,
        }.Save(ct);
}
