using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

namespace Koan.Identity.Tenancy.Deprovisioning;

/// <summary>The kind of deprovisioning a <see cref="DeprovisioningReceipt"/> records.</summary>
public enum DeprovisioningKind
{
    /// <summary>A seat was removed from one tenant; the person persists (and may remain in other tenants).</summary>
    SeatRemoval = 0,
    /// <summary>The person was deactivated, their Koan cookie sessions revoked, and their tenant seats removed.</summary>
    Deactivation = 1,
}

/// <summary>
/// Integrity-checked record of one completed deprovisioning workflow. It records the Entity writes performed and a
/// content <see cref="Hash"/> that detects later field changes. It is not append-only, a database transaction, a
/// signature, or proof that an external system still matches the recorded state. Enforcement remains in Identity's
/// session guard and this bridge's active-membership request check. <c>IAmbientExempt</c> keeps the operational record
/// host-scoped so it survives the tenant it concerns.
/// </summary>
public sealed class DeprovisioningReceipt : Entity<DeprovisioningReceipt>, IAmbientExempt
{
    /// <summary>The person who was deprovisioned.</summary>
    public string IdentityId { get; set; } = "";

    /// <summary>The tenant a seat was removed from, or null for a full person deactivation (global).</summary>
    public string? TenantId { get; set; }

    /// <summary>Seat removal vs. full deactivation.</summary>
    public DeprovisioningKind Kind { get; set; }

    /// <summary>How many active sessions were revoked.</summary>
    public int SessionsRevoked { get; set; }

    /// <summary>How many memberships were removed.</summary>
    public int MembershipsRemoved { get; set; }

    /// <summary>The status the person was set to, if any (e.g. <c>Deactivated</c>); null when unchanged (idempotent re-run).</summary>
    public string? StatusSet { get; set; }

    /// <summary>The exact Koan surfaces this workflow closes; values come from <see cref="DeprovisioningSurfaces"/>.</summary>
    public List<string> Surfaces { get; set; } = new();

    /// <summary>When the deprovisioning happened (set by the service before hashing, so the hash is deterministic).</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>The content hash over the receipt's own fields — recompute and compare to detect tampering.</summary>
    public string Hash { get; set; } = "";

    /// <summary>Recompute the content hash from this receipt's fields.</summary>
    public string ComputeHash()
        => DeprovisioningReceiptHash.Of(IdentityId, TenantId, Kind, SessionsRevoked, MembershipsRemoved, StatusSet, Surfaces, OccurredAt);

    /// <summary>True when the stored <see cref="Hash"/> matches a recomputation of this record's fields.</summary>
    public bool HasValidHash() => Hash.Length > 0 && string.Equals(Hash, ComputeHash(), StringComparison.Ordinal);
}
