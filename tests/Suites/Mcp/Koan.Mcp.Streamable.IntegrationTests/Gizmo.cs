using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Mcp;

namespace Koan.Mcp.Streamable.IntegrationTests;

/// <summary>
/// A trivial entity exposed over MCP so the Streamable HTTP transport has real tools to list/call end-to-end.
/// <c>Tools</c> exposure keeps <c>tools/list</c> entity-tool-shaped (not the code-mode single tool), which is what
/// the round-trip spec asserts on.
/// </summary>
[McpEntity(Name = "gizmo", Description = "A gizmo", Exposure = McpExposureMode.Tools)]
[StorageName("streamable_gizmos")]
public sealed class Gizmo : Entity<Gizmo>
{
    public string Name { get; set; } = "";
    public int Quantity { get; set; }
}
