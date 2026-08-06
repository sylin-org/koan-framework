using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace Koan.Identity.Erasure;

/// <summary>
/// Non-identifying integrity-checked record of one erasure attempt. It intentionally contains no subject lookup;
/// the caller retains the opaque receipt id. The content hash detects mutation but is not a signature or external
/// attestation.
/// </summary>
public sealed class IdentityErasureReceipt : Entity<IdentityErasureReceipt>, IAmbientExempt
{
    /// <summary>Public erasure policy version applied by this attempt.</summary>
    public string PolicyVersion { get; set; } = "";

    /// <summary>When owner execution began.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>When every owner had either succeeded or reported failure.</summary>
    public DateTimeOffset CompletedAt { get; set; }

    /// <summary>True only when every participating owner succeeded.</summary>
    public bool Complete { get; set; }

    /// <summary>Ordered owner outcomes. Values must remain non-identifying.</summary>
    public List<IdentityErasureOwnerResult> Owners { get; set; } = new();

    /// <summary>SHA-256 over this receipt's policy, times, completion state, and ordered owner results.</summary>
    public string Hash { get; set; } = "";

    /// <summary>Recompute the receipt content hash.</summary>
    public string ComputeHash() => IdentityErasureReceiptHash.Compute(this);

    /// <summary>True when the stored hash matches the current receipt fields.</summary>
    public bool HasValidHash() => Hash.Length > 0 && string.Equals(Hash, ComputeHash(), StringComparison.Ordinal);
}
