using System.Security.Claims;
using Koan.Mcp.Execution;

namespace Koan.Mcp.Explorer.Hosting;

/// <summary>
/// WEB-0072 D5 — the privileged access-map gate as a pure decision (so it is unit-testable in isolation). The
/// god-view (every requirement, walls included) is served in Development, or to a caller holding the configured
/// admin role/scope; <b>fail-closed otherwise</b> — it never leaks the un-redacted privilege map.
/// </summary>
public static class AccessMapGate
{
    public static bool Allowed(bool isDevelopment, ClaimsPrincipal? user, McpExplorerOptions options)
    {
        if (isDevelopment) return true;
        if (user?.Identity?.IsAuthenticated != true) return false;
        if (!string.IsNullOrWhiteSpace(options.AdminRole) && user.IsInRole(options.AdminRole!)) return true;
        if (!string.IsNullOrWhiteSpace(options.AdminScope) && McpToolAccessPolicy.UserHasScopes(user, new[] { options.AdminScope! })) return true;
        return false;
    }
}
