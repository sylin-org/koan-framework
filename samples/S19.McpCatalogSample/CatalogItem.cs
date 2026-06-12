using Koan.Mcp;
using Koan.Data.Core.Model;

namespace S19.McpCatalogSample;

[McpEntity(Name = "Catalog", Description = "Flagship product catalog", AllowMutations = false)]
public sealed class CatalogItem : Entity<CatalogItem>
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
}
