using Koan.Mcp.CustomTools;
using Koan.Mcp.Execution;
using Koan.Mcp.Options;

namespace Koan.Mcp.Resources;

/// <summary>
/// Produces the caller-visible custom-tool surface shared by protocol listing and introspection.
/// </summary>
internal static class CustomToolProjection
{
    public static async Task<IReadOnlyList<McpCustomTool>> Visible(
        McpCustomToolRegistry registry,
        McpServerOptions options,
        McpVerbVisibility visibility,
        CancellationToken ct = default)
    {
        var visible = new List<McpCustomTool>();
        foreach (var tool in registry.Tools.OrderBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase))
            if (await IsVisible(tool, options, visibility, ct).ConfigureAwait(false)) visible.Add(tool);
        return visible;
    }

    /// <summary>A verb on a config-disabled operational toolset is absent for everyone; otherwise the one gate answers, with no
    /// resource at hand, so a verb only a resource can decide stays listed (ARCH-0141).</summary>
    public static async Task<bool> IsVisible(McpCustomTool tool, McpServerOptions options, McpVerbVisibility visibility, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(options);
        return (tool.OperationalToolsetKey is not { Length: > 0 } key || options.IsOperationalToolsetEnabled(key))
               && await visibility.Allows(tool, ct).ConfigureAwait(false);
    }
}
