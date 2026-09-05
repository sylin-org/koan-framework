using System.ComponentModel.DataAnnotations;
using ExpenseDesk.Infrastructure;
using Example.Approvals.Foundation.Domain;
using Example.Approvals.Foundation.Infrastructure;
using Koan.Mcp;
using Koan.Web.Authorization;

namespace ExpenseDesk.Domain;

[McpEntity(Name = ExpenseConstants.AgentEntityName, Description = "An employee expense awaiting approval and reimbursement", Exposure = McpExposureMode.Tools)]
[Access(read: Access.Anyone, write: Access.Anyone, remove: ApprovalConstants.LocalOrigin)]
public sealed class ExpenseClaim : ApprovalRequest<ExpenseClaim>
{
    [Required]
    public string Employee { get; set; } = "";
    [Required]
    public string ReceiptNumber { get; set; } = "";
    public DateTimeOffset? ReimbursedAt { get; set; }
}
