using Microsoft.AspNetCore.Routing;
using Koan.Mcp.Extensions;
using Koan.Web.Hosting;

namespace Koan.Mcp.Initialization;

/// <summary>
/// WEB-0069 — maps the MCP HTTP/SSE endpoints inside Koan's single <c>UseEndpoints</c> block, replacing the
/// reflection hack that previously lived in <c>KoanWebStartupFilter</c>. <c>MapKoanMcpEndpoints</c> self-gates on
/// <c>EnableHttpSseTransport</c>, so this is safe to register unconditionally.
/// </summary>
internal sealed class McpEndpointContributor : IKoanEndpointContributor
{
    public void Map(IEndpointRouteBuilder endpoints) => endpoints.MapKoanMcpEndpoints();
}
