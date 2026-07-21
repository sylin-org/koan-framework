using System.Threading.Tasks;
using Koan.Mcp.Hosting;
using Microsoft.AspNetCore.Http;

namespace Koan.Mcp.Explorer.Hosting;

/// <summary>
/// WEB-0072 / AI-0037 D-C — the Explorer's implementation of the core's <see cref="IMcpConsoleRenderer"/> seam.
/// The CORE owns <c>GET {baseRoute}</c> and does the content negotiation (an MCP client gets the SSE stream; a
/// browser is delegated here); this renderer only serves the console HTML. It no longer maps its own route, so the
/// bare route has exactly one owner (no Explorer-vs-transport collision).
/// </summary>
internal sealed class McpConsoleRenderer : IMcpConsoleRenderer
{
    public async Task RenderConsoleAsync(HttpContext context, string baseRoute)
    {
        if (!ExplorerAssetProvider.TryGet("index.html", out var html, out _))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        html = html.Replace("{{base}}", baseRoute);
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.Headers["Vary"] = "Accept";
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(html, context.RequestAborted);
    }
}
