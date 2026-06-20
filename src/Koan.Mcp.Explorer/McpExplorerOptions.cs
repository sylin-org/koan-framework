using Koan.Core;

namespace Koan.Mcp.Explorer;

/// <summary>
/// WEB-0072 — options for the MCP Explorer console, bound from <c>Koan:Mcp:Explorer</c>.
/// </summary>
public sealed class McpExplorerOptions
{
    private bool? _enabled;

    /// <summary>
    /// Whether the console + <c>{baseRoute}/map.json</c> + try-it endpoint are mounted. Dev-default-on
    /// (the Swagger-UI posture): on in Development, off in Production / containers unless explicitly enabled.
    /// </summary>
    public bool Enabled
    {
        get => _enabled ?? !(KoanEnv.IsProduction || KoanEnv.InContainer);
        set => _enabled = value;
    }
}
