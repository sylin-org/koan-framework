using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Koan.Mcp.Hosting;

/// <summary>
/// AI-0037 / WEB-0072 — the seam by which a human MCP console (the Explorer) plugs into the core's ownership of
/// <c>GET {baseRoute}</c>. The core owns the route and content-negotiates: an MCP client (<c>text/event-stream</c>)
/// gets the Streamable HTTP stream; a browser (<c>text/html</c>) is delegated here. This keeps ONE owner of the bare
/// route (no Explorer-vs-transport collision) and ONE console-rendering path. Registered by the Explorer package;
/// absent when no console is installed (the core then 404s the HTML branch). The renderer is invoked only after the
/// core has decided the request is a console request, and only on the anonymous (un-bearer-gated) branch.
/// </summary>
public interface IMcpConsoleRenderer
{
    /// <summary>Render the human console for the MCP edge rooted at <paramref name="baseRoute"/>.</summary>
    Task RenderConsoleAsync(HttpContext context, string baseRoute);
}
