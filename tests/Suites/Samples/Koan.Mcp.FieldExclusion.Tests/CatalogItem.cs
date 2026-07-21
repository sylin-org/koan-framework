using Koan.Data.Core.Model;
using Koan.Mcp;

namespace Koan.Mcp.FieldExclusion.Tests;

/// <summary>
/// Exercises every <see cref="McpIgnoreAttribute"/> direction on a single read/write entity:
/// <list type="bullet">
///   <item><see cref="InternalSecret"/> — Both: absent from schema and results, cannot be set.</item>
///   <item><see cref="ServerOwned"/> — Input: absent from schema and cannot be set, but returned in results.</item>
///   <item><see cref="WriteOnlyToken"/> — Output: present in schema and settable, but never returned.</item>
///   <item><see cref="Name"/> — no attribute: present everywhere.</item>
/// </list>
/// Exposure = Full so both Tools-mode and Code-Mode surfaces are generated.
/// </summary>
[McpEntity(Name = "CatalogItem", Description = "Catalog item with internal/PII fields", Exposure = McpExposureMode.Full)]
public sealed class CatalogItem : Entity<CatalogItem>
{
    public string Name { get; set; } = "";

    [McpIgnore]
    public string InternalSecret { get; set; } = "";

    [McpIgnore(McpFieldDirection.Input)]
    public string ServerOwned { get; set; } = "";

    [McpIgnore(McpFieldDirection.Output)]
    public string WriteOnlyToken { get; set; } = "";
}
