namespace ApprovalDesk.Infrastructure;

public static class PurchaseConstants
{
    public const string AgentEntityName = "purchase-request";
    public const string Route = "api/purchases";
    public const string OrderRoute = "{id}/order";
    public const string InvalidPurchase = "purchase.invalid";
    public const string ApprovalRequired = "purchase.approval-required";
    public const string OrderFinal = "purchase.order-final";
    public const string OrderPrefix = "PO-";
}
