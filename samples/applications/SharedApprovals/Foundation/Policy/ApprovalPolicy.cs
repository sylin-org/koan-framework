using Example.Approvals.Foundation.Domain;
using Example.Approvals.Foundation.Infrastructure;
using Koan.Data.Core.Lifecycle;

namespace Example.Approvals.Foundation.Policy;

public sealed class ApprovalPolicy(ApprovalPolicyOptions options)
{
    public EntityLifecycleResult BeforeUpsert<TEntity>(EntityLifecycleContext<TEntity> context)
        where TEntity : ApprovalRequest<TEntity>, new()
    {
        var current = context.Current;
        var prior = context.Prior;
        if (string.IsNullOrWhiteSpace(current.Subject) || current.Amount <= 0 || !Enum.IsDefined(current.State))
            return context.Cancel("Supply a subject, a positive amount, and a valid approval state.",
                ApprovalConstants.Codes.InvalidRequest);

        if (prior?.State == ApprovalState.Approved)
        {
            if (current.State != prior.State || current.Amount != prior.Amount || current.Subject != prior.Subject)
                return context.Cancel("An approved request's subject, amount, and approval state are final.",
                    ApprovalConstants.Codes.AlreadyApproved);
            return context.Proceed(); // Consumers may finish their own post-approval workflow.
        }

        if (current.State == ApprovalState.Approved)
        {
            if (prior is null)
                return context.Cancel("Submit the request before approving it.", ApprovalConstants.Codes.SubmitFirst);
            if (current.Amount > options.MaximumApprovalAmount)
                return context.Cancel(
                    $"This request exceeds the {options.Currency} {options.MaximumApprovalAmount:0.00} approval limit.",
                    ApprovalConstants.Codes.OverLimit);
        }
        return context.Proceed();
    }

    public EntityLifecycleResult BeforeRemove<TEntity>(EntityLifecycleContext<TEntity> context)
        where TEntity : ApprovalRequest<TEntity>, new() =>
        context.Current.State == ApprovalState.Approved
            ? context.Cancel("Keep approved requests as the record of the decision.", ApprovalConstants.Codes.AlreadyApproved)
            : context.Proceed();
}
