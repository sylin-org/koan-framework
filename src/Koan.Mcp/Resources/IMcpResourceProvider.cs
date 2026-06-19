using System.Collections.Generic;
using System.Security.Claims;

namespace Koan.Mcp.Resources;

/// <summary>
/// P1.2 — the seam for contributing MCP introspection resources. Implementations are discovered through
/// DI (register as <see cref="IMcpResourceProvider"/>); the framework ships a built-in entity catalog and
/// AN8 adds <c>koan://self</c> over this same seam. Every method takes the caller's
/// <see cref="ClaimsPrincipal"/> so a provider can PROJECT its resources per grant — listing and content
/// reflect only what THIS caller may see (a null principal = local-trust / STDIO, full projection).
/// </summary>
public interface IMcpResourceProvider
{
    /// <summary>The resources this provider exposes to the given caller (already grant-projected).</summary>
    IEnumerable<McpResourceDescriptor> List(ClaimsPrincipal? user);

    /// <summary>The contents of <paramref name="uri"/> for the caller, or <c>null</c> if this provider does
    /// not own the uri (or the caller may not read it).</summary>
    McpResourceContents? Read(string uri, ClaimsPrincipal? user);
}
