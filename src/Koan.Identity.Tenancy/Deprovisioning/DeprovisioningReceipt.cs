using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

namespace Koan.Identity.Tenancy.Deprovisioning;

/// <summary>The kind of deprovisioning a <see cref="DeprovisioningReceipt"/> records.</summary>
public enum DeprovisioningKind
{
    /// <summary>A seat was removed from one tenant; the person persists (and may remain in other tenants).</summary>
    SeatRemoval = 0,
    /// <summary>The whole person was deactivated; they can no longer act anywhere.</summary>
    Deactivation = 1,
}

/// <summary>
/// SEC-0007 P4 — the per-user VERIFIABLE receipt of a deprovisioning: an append-only <c>Entity&lt;&gt;</c> recording
/// exactly which surfaces were closed (sessions revoked, memberships removed, status set) plus a content
/// <see cref="Hash"/> so the receipt cannot be silently altered. <b>"Atomic" means complete-or-fail-closed</b>:
/// the <i>enforcement</i> is the request-path <c>SessionGuard</c> (deactivation) + the fail-closed tenant axis
/// (seat removal) — never a write-only flag — and this receipt is the durable <i>proof</i>, not the enforcement.
/// <c>IAmbientExempt</c>: a receipt is a global artifact, not tenant-owned (so it survives the tenant it concerns).
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

    /// <summary>How many memberships were removed (seat removal).</summary>
    public int MembershipsRemoved { get; set; }

    /// <summary>The status the person was set to, if any (e.g. <c>Deactivated</c>); null when unchanged (idempotent re-run).</summary>
    public string? StatusSet { get; set; }

    /// <summary>The surfaces this deprovisioning closes — data/storage/cache via the fail-closed axis, sessions via revocation.</summary>
    public List<string> Surfaces { get; set; } = new();

    /// <summary>When the deprovisioning happened (set by the service before hashing, so the hash is deterministic).</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>The content hash over the receipt's own fields — recompute and compare to detect tampering.</summary>
    public string Hash { get; set; } = "";

    /// <summary>Recompute the content hash from this receipt's fields (verification == recompute-and-compare).</summary>
    public string ComputeHash()
        => DeprovisioningReceiptHash.Of(IdentityId, TenantId, Kind, SessionsRevoked, MembershipsRemoved, StatusSet, Surfaces, OccurredAt);

    /// <summary>True when the stored <see cref="Hash"/> matches a recomputation — the receipt is intact.</summary>
    public bool Verify() => Hash.Length > 0 && string.Equals(Hash, ComputeHash(), StringComparison.Ordinal);
}
