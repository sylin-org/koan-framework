using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Mcp;
using Koan.Web.Authorization;

namespace Koan.Mcp.Streamable.IntegrationTests;

/// <summary>SEC-0018: every verb needs a signed-in caller, so an anonymous caller's self-description omits it.</summary>
[McpEntity(Name = "strongroom", Exposure = McpExposureMode.Tools)]
[Access(all: "authenticated")]
[StorageName("streamable_strongrooms")]
public sealed class Strongroom : Entity<Strongroom>
{
    public string Item { get; set; } = "";
}
