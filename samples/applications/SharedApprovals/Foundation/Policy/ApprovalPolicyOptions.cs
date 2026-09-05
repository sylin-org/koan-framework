using Example.Approvals.Foundation.Infrastructure;

namespace Example.Approvals.Foundation.Policy;

/// <summary>The organization's policy, shipped and updated with its foundation package.</summary>
public sealed class ApprovalPolicyOptions
{
    public decimal MaximumApprovalAmount { get; } = 500m;
    public string Currency { get; } = ApprovalConstants.Currency;
}
