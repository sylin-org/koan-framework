using System;
using System.Collections.Generic;
using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

namespace SnapVault.Models;

/// <summary>
/// The studio-to-client engagement bookend: tamper-evident, verifiable proof that a client's
/// gallery access was atomically removed. It CHAINS the shipped tenancy <c>DeprovisioningReceipt</c> (id + its content
/// hash) and records the domain purge (grants + proof selections). "Atomic" = complete-or-fail-closed: the guest's
/// PhotoAsset reads fail closed the instant the <c>GalleryGrant</c> rows are gone because the access axis re-reads grants
/// per request) — this cert is the PROOF, not the enforcement. <c>IAmbientExempt</c>: a global artifact that survives
/// the tenant/relationship it concerns. A SHA-256 content hash makes the certificate tamper-evident; a future
/// asymmetric signature can extend <see cref="SignatureAlgorithm"/>.
/// </summary>
public sealed class ClientErasureCertificate : Entity<ClientErasureCertificate>, IAmbientExempt
{
    /// <summary>The client (guest person id) whose access was erased.</summary>
    public string GuestIdentityId { get; set; } = "";

    /// <summary>The studio (a TenantRecord id) the client was removed from.</summary>
    public string StudioTenantId { get; set; } = "";

    /// <summary>How many <see cref="GalleryGrant"/>s were purged.</summary>
    public int GrantsRemoved { get; set; }

    /// <summary>How many <see cref="ProofSelection"/>s were purged.</summary>
    public int SelectionsRemoved { get; set; }

    /// <summary>How many still-pending invites to this client were revoked (so an "erased" client can't re-accept).</summary>
    public int InvitesRevoked { get; set; }

    /// <summary>The chained shipped <c>DeprovisioningReceipt</c> (the seat removal) — id.</summary>
    public string SeatReceiptId { get; set; } = "";

    /// <summary>The chained receipt's own content hash (chain of custody to the framework primitive).</summary>
    public string SeatReceiptHash { get; set; } = "";

    /// <summary>The surfaces this erasure closed.</summary>
    public List<string> Surfaces { get; set; } = new();

    /// <summary>The hash scheme — "sha256-content" (tamper-evident); a signed variant is a future non-breaking upgrade.</summary>
    public string SignatureAlgorithm { get; set; } = "sha256-content";

    /// <summary>When the erasure happened (set before hashing so the hash is deterministic).</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>The content hash over this cert's fields — recompute and compare to detect tampering.</summary>
    public string Hash { get; set; } = "";

    /// <summary>Recompute the content hash (SHA-256 of the joined fields; verification == recompute-and-compare).</summary>
    public string ComputeHash() => DeterministicId.From(
        "client-erasure-v1", GuestIdentityId, StudioTenantId,
        GrantsRemoved.ToString(), SelectionsRemoved.ToString(), InvitesRevoked.ToString(),
        SeatReceiptId, SeatReceiptHash, string.Join(",", Surfaces),
        SignatureAlgorithm, OccurredAt.ToUnixTimeMilliseconds().ToString());

    /// <summary>True when the stored <see cref="Hash"/> matches a recomputation — the certificate is intact.</summary>
    public bool Verify() => Hash.Length > 0 && string.Equals(Hash, ComputeHash(), StringComparison.Ordinal);
}
