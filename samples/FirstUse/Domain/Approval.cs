using Koan.Data.Abstractions;
using Koan.Data.Core.Model;
using Koan.FirstUse.Infrastructure;
using Koan.Mcp;
using Koan.Web.Authorization;

namespace Koan.FirstUse.Domain;

[DataAdapter("sqlite")]
[McpEntity(
    Name = FirstUseConstants.Agent.EntityName,
    Description = "A business request awaiting a decision",
    Exposure = McpExposureMode.Tools)]
[Access(read: Access.Anyone, write: Access.Anyone, remove: FirstUseConstants.Agent.LocalOrigin)]
public sealed class Approval : Entity<Approval>
{
    public string Subject { get; set; } = "";
    public ApprovalState State { get; set; } = ApprovalState.Pending;
}
