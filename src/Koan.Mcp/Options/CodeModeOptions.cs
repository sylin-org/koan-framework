using Koan.Mcp.CodeMode.Execution;

namespace Koan.Mcp.Options;

/// <summary>
/// Configuration for MCP code mode execution.
/// </summary>
public sealed class CodeModeOptions
{
    /// <summary>
    /// Enable code mode functionality.
    /// Default: true (enabled).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// JavaScript runtime engine.
    /// Default: "Jint" (only supported runtime currently).
    /// </summary>
    public string Runtime { get; set; } = "Jint";

    /// <summary>
    /// Sandbox execution limits (CPU, memory, recursion).
    /// </summary>
    public SandboxOptions Sandbox { get; set; } = new();

    /// <summary>
    /// Publish SDK definitions endpoint at GET /mcp/sdk/definitions.
    /// Default: true.
    /// </summary>
    public bool PublishSdkEndpoint { get; set; } = true;
}
