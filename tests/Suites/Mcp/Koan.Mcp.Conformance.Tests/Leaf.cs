using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Mcp;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>WEB-0076: an open entity for the MCP paging specs.</summary>
[McpEntity(Name = "leaf", Exposure = McpExposureMode.Full)]
[StorageName("conformance_leaves")]
public sealed class Leaf : Entity<Leaf>
{
    public string Name { get; set; } = "";
}
