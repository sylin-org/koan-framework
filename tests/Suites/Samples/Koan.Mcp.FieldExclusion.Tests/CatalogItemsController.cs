using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Mcp.FieldExclusion.Tests;

/// <summary>
/// Plain REST surface used to seed and verify entities out-of-band. REST does not apply
/// <see cref="McpIgnoreAttribute"/> (it is MCP-local), so this is how a test populates an
/// output-excluded field and confirms an input-excluded field was blocked by MCP.
/// </summary>
[Route("api/[controller]")]
public sealed class CatalogItemsController : EntityController<CatalogItem>
{
}
