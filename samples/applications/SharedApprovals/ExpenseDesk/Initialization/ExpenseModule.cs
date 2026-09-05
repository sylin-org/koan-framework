using Example.Approvals.Foundation.Domain;
using Example.Approvals.Foundation.Initialization;
using ExpenseDesk.Domain;
using ExpenseDesk.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ExpenseDesk.Initialization;

public sealed class ExpenseModule : ApprovalPolicyModule<ExpenseClaim>
{
    public override void Register(IServiceCollection services)
    {
        base.Register(services);
        ExpenseClaim.Lifecycle.BeforeUpsert(context =>
        {
            var claim = context.Current;
            if (string.IsNullOrWhiteSpace(claim.Employee) || string.IsNullOrWhiteSpace(claim.ReceiptNumber))
                return context.Cancel("Supply the employee and receipt number.", ExpenseConstants.InvalidClaim);
            if (context.Prior?.State == ApprovalState.Approved &&
                (claim.Employee != context.Prior.Employee || claim.ReceiptNumber != context.Prior.ReceiptNumber))
                return context.Cancel("An approved claim's employee and receipt number are final.", ExpenseConstants.ReceiptFinal);
            if (claim.ReimbursedAt is not null && context.Prior?.State != ApprovalState.Approved)
                return context.Cancel("Approve the expense before reimbursing it.", ExpenseConstants.ApprovalRequired);
            if (context.Prior?.ReimbursedAt is not null && claim.ReimbursedAt != context.Prior.ReimbursedAt)
                return context.Cancel("A reimbursement receipt is final.", ExpenseConstants.ReceiptFinal);
            return context.Proceed();
        });
    }
}
