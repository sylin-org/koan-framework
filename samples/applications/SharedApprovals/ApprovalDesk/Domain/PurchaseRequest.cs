using System.ComponentModel.DataAnnotations;
using ApprovalDesk.Infrastructure;
using Example.Approvals.Foundation.Domain;
using Example.Approvals.Foundation.Infrastructure;
using Koan.Mcp;
using Koan.Web.Authorization;

namespace ApprovalDesk.Domain;

[McpEntity(Name = PurchaseConstants.AgentEntityName, Description = "A supplier purchase awaiting an approval and order", Exposure = McpExposureMode.Tools)]
[Access(read: Access.Anyone, write: Access.Anyone, remove: ApprovalConstants.LocalOrigin)]
public sealed class PurchaseRequest : ApprovalRequest<PurchaseRequest>
{
    [Required]
    public string Supplier { get; set; } = "";
    [Required]
    public string CostCenter { get; set; } = "";
    public string? OrderNumber { get; set; }
}
