using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core;
using Koan.Data.Core.Model;

namespace Koan.Tenancy;

/// <summary>
/// A control-plane audit entry — a dogfooded <c>[HostScoped]</c> <see cref="Entity{T}"/> written by every supported
/// operator mutation: <c>actor / action / tenant / summary / at</c>. It is an application-owned integrity record,
/// not an append-only or externally attested ledger. It lives in host scope and is never tenant-scoped.
/// </summary>
[HostScoped]
public sealed class TenantAuditEntry : Entity<TenantAuditEntry>
{
    /// <summary>Who performed the action — the operator principal (subject/name), or <c>system</c> for automated ops.</summary>
    public string Actor { get; set; } = "";

    /// <summary>The action verb, e.g. <c>tenant.created</c>, <c>tenant.renamed</c>, or <c>membership.revoked</c>.</summary>
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
    /// Record one audit entry. The supported administration chokepoint funnels every mutation through this method.
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
