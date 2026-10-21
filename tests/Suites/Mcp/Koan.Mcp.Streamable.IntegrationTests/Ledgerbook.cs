using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Mcp;
using Koan.Web.Authorization;

namespace Koan.Mcp.Streamable.IntegrationTests;

/// <summary>SEC-0016: writing needs the <c>items:write</c> scope, so a token downgrade between requests is visible.</summary>
[McpEntity(Name = "ledgerbook", Exposure = McpExposureMode.Tools)]
[Access(write: "has:scope:items:write")]
[StorageName("streamable_ledgerbooks")]
public sealed class Ledgerbook : Entity<Ledgerbook>
{
    public string Entry { get; set; } = "";
}
