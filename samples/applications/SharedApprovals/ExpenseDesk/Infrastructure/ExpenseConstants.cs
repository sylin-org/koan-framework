namespace ExpenseDesk.Infrastructure;

public static class ExpenseConstants
{
    public const string AgentEntityName = "expense-claim";
    public const string Route = "api/expenses";
    public const string ReimburseRoute = "{id}/reimburse";
    public const string InvalidClaim = "expense.invalid";
    public const string ApprovalRequired = "expense.approval-required";
    public const string ReceiptFinal = "expense.receipt-final";
}
