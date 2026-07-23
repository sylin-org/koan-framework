namespace Koan.Identity.Erasure;

/// <summary>An ordered, non-mutating preview of every registered identity-erasure owner.</summary>
public sealed record IdentityErasurePlan(
    DateTimeOffset CreatedAt,
    IReadOnlyList<IdentityErasureOwnerPlan> Owners)
{
    /// <summary>True only when every participating owner is ready.</summary>
    public bool CanComplete => Owners.Count > 0 && Owners.All(static owner => owner.Ready);
}
