namespace Koan.Identity.Erasure;

/// <summary>One semantic owner's non-mutating erasure preview.</summary>
public sealed record IdentityErasureOwnerPlan(
    string Owner,
    int Order,
    bool Ready,
    int EstimatedItems,
    string Summary,
    string? Correction = null);
