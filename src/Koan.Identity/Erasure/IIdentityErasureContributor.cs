using Koan.Core;

namespace Koan.Identity.Erasure;

/// <summary>
/// One semantic owner participating in a durable person's privacy-safe lifecycle erasure. Implementations must be
/// idempotent and must not report success until their owned data has reached the stated disposition.
/// </summary>
[KoanDiscoverable]
public interface IIdentityErasureContributor
{
    /// <summary>Stable, human-readable owner name used in plans and receipts.</summary>
    string Owner { get; }

    /// <summary>Execution order. Access-closing owners run early; retained-evidence sanitizers run late.</summary>
    int Order { get; }

    /// <summary>Describe owned work and blockers without mutation.</summary>
    Task<IdentityErasureOwnerPlan> PreviewAsync(string identityId, CancellationToken ct = default);

    /// <summary>Perform this owner's idempotent erasure work.</summary>
    Task<IdentityErasureOwnerResult> EraseAsync(string identityId, CancellationToken ct = default);
}
