namespace Koan.Mcp.CodeMode.Execution;

/// <summary>
/// Centralized error code constants for Code Mode and MCP tool execution.
/// These codes are surfaced to clients (LLMs, tools) and documented in ADR AI-0014.
/// </summary>
public static class CodeModeErrorCodes
{
    // Input / validation
    public const string InvalidCode = "invalid_code";              // Submitted source empty or whitespace
    public const string CodeTooLong = "code_too_long";             // Source length exceeds SandboxOptions.MaxCodeLength
    public const string InvalidPayload = "invalid_payload";        // Tool invocation JSON malformed / translation failed

    // Quotas / policy
    public const string SdkCallsExceeded = "sdk_calls_exceeded";   // bindings.Metrics.GetTotalCalls() > CodeModeOptions.MaxSdkCalls
    public const string MissingAnswer = "missing_answer";          // RequireAnswer enabled but SDK.Out.answer not called

    // Execution runtime
    public const string JavaScriptError = "javascript_error";      // JavaScript runtime exception (syntax/runtime) with location
    public const string Timeout = "timeout";                       // Exceeded CpuMilliseconds (Jint timeout / cancellation)
    public const string ExecutionError = "execution_error";        // Generic unhandled executor or tool error

    // Tooling / registry
    public const string ToolNotFound = "tool_not_found";           // Requested tool name not registered
    public const string ServiceUnavailable = "service_unavailable";// IEntityEndpointService not available in DI scope
}
