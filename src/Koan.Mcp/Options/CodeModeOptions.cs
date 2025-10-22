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

    /// <summary>
    /// Maximum allowed SDK entity operation calls per execution (0 = unlimited).
    /// Default: 0 (unlimited) – set small value in constrained environments.
    /// </summary>
    public int MaxSdkCalls { get; set; } = 0;

    /// <summary>
    /// Maximum allowed log entries produced via SDK.Out.* (0 = unlimited).
    /// </summary>
    public int MaxLogEntries { get; set; } = 0;

    /// <summary>
    /// Enforce that an entry function (explicit or run()) must produce an answer via SDK.Out.answer.
    /// If true and no answer is set, execution is treated as failure.
    /// </summary>
    public bool RequireAnswer { get; set; } = false;

    /// <summary>
    /// Enable structured audit logging (Info level) for each execution (start, success/failure, diagnostics summary).
    /// </summary>
    public bool EnableAuditLogging { get; set; } = true;
}
