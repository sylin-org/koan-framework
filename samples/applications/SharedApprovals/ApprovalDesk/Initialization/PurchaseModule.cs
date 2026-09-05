using ApprovalDesk.Domain;
using ApprovalDesk.Infrastructure;
using Example.Approvals.Foundation.Domain;
using Example.Approvals.Foundation.Initialization;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalDesk.Initialization;

public sealed class PurchaseModule : ApprovalPolicyModule<PurchaseRequest>
{
    public override void Register(IServiceCollection services)
    {
        base.Register(services);
        PurchaseRequest.Lifecycle.BeforeUpsert(context =>
        {
            var request = context.Current;
            if (string.IsNullOrWhiteSpace(request.Supplier) || string.IsNullOrWhiteSpace(request.CostCenter))
                return context.Cancel("Supply the supplier and cost center.", PurchaseConstants.InvalidPurchase);
            if (context.Prior?.State == ApprovalState.Approved &&
                (request.Supplier != context.Prior.Supplier || request.CostCenter != context.Prior.CostCenter))
                return context.Cancel("An approved purchase's supplier and cost center are final.", PurchaseConstants.OrderFinal);
            if (request.OrderNumber is not null && context.Prior?.State != ApprovalState.Approved)
                return context.Cancel("Approve the purchase before placing its order.", PurchaseConstants.ApprovalRequired);
            if (context.Prior?.OrderNumber is not null && request.OrderNumber != context.Prior.OrderNumber)
                return context.Cancel("A placed order's reference is final.", PurchaseConstants.OrderFinal);
            return context.Proceed();
        });
    }
}
