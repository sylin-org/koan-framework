using Microsoft.AspNetCore.Routing;
using Koan.Web.Hosting;

namespace Koan.Mcp.Initialization;

/// <summary>
/// WEB-0069 — maps the MCP HTTP/SSE endpoints inside Koan's single <c>UseEndpoints</c> block, replacing the
/// reflection hack that previously lived in <c>KoanWebStartupFilter</c>. MCP endpoint mapping self-gates on
/// the explicit MCP HTTP transport switches, so this is safe to register unconditionally.
/// </summary>
internal sealed class McpEndpointContributor : IKoanEndpointContributor
{
    public void Map(IEndpointRouteBuilder endpoints) => endpoints.MapMcpEndpoints();
}
