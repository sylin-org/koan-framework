using System.Security.Claims;
using Koan.Mcp.CustomTools;
using Koan.Mcp.Execution;
using Koan.Mcp.Options;

namespace Koan.Mcp.Resources;

/// <summary>
/// Produces the caller-visible custom-tool surface shared by protocol listing and introspection.
/// </summary>
internal static class CustomToolProjection
{
    public static IReadOnlyList<McpCustomTool> Visible(
        McpCustomToolRegistry registry,
        McpServerOptions options,
        ClaimsPrincipal? user) =>
        registry.Tools
            .Where(tool => IsVisible(tool, options, user))
            .OrderBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static bool IsVisible(
        McpCustomTool tool,
        McpServerOptions options,
        ClaimsPrincipal? user)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(options);

        // A null principal is the established trusted local-STDIO surface. It bypasses remote
        // authentication/scope filters, but config-disabled operational toolsets remain absent.
        if (user is null)
        {
            return tool.OperationalToolsetKey is not { Length: > 0 } key
                || options.IsOperationalToolsetEnabled(key);
        }

        return McpToolAccessPolicy.IsCustomToolPermitted(user, tool, options);
    }
}
