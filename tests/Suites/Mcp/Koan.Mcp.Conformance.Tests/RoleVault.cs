using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Mcp;
using Koan.Web.Authorization;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>
/// SEC-0010: an entity whose read is a role tier. A caller without the role must learn that the call was
/// forbidden, never which role would admit it (SEC-0005 keeps role gates silent).
/// </summary>
[McpEntity(Name = "rolevault", Exposure = McpExposureMode.Full)]
[Access(read: "is:admin")]
[StorageName("conformance_rolevaults")]
public sealed class RoleVault : Entity<RoleVault>
{
    public string Note { get; set; } = "";
}
