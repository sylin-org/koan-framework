using System;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Mcp.CodeExecution;

/// <summary>
/// Abstraction for executing code-mode scripts (JavaScript / TypeScript transpiled) against a constrained SDK surface.
/// Concrete implementations (e.g., Jint) provide sandboxing, quotas, and binding.
/// </summary>
public interface ICodeExecutor
{
    /// <summary>
    /// Execute provided source code and return structured result.
    /// Implementations must enforce CPU/memory/recursion/time budgets and never allow ambient CLR access.
    /// </summary>
    Task<CodeExecutionResult> ExecuteAsync(CodeExecutionRequest request, CancellationToken ct = default);
}

public sealed record CodeExecutionRequest(
    string Source,
    string? Language, // "js" | "ts" (future) – current implementation assumes js
    string? EntryFunction, // optional named entry; null = default exported run()
    string? UserId,
    string? Set,
    string? CorrelationId
);

public sealed record CodeExecutionResult(
    bool Success,
    object? Result,
    string? TextResponse,
    string[] Logs,
    CodeExecutionDiagnostics Diagnostics,
    CodeExecutionError? Error
);

public sealed record CodeExecutionDiagnostics(
    int SdkCalls,
    int CpuMs,
    long MemoryBytes,
    int ScriptLength,
    DateTime StartedUtc,
    DateTime CompletedUtc
);

public sealed record CodeExecutionError(string Type, string Message, string? Stack);

/// <summary>
/// No-op placeholder executor used until a real sandbox (Jint) is wired.
/// Returns an error informing callers that code mode runtime is not yet active.
/// </summary>
public sealed class NotImplementedCodeExecutor : ICodeExecutor
{
    public Task<CodeExecutionResult> ExecuteAsync(CodeExecutionRequest request, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return Task.FromResult(new CodeExecutionResult(
            Success: false,
            Result: null,
            TextResponse: null,
            Logs: Array.Empty<string>(),
            Diagnostics: new CodeExecutionDiagnostics(0, 0, 0, request.Source?.Length ?? 0, now, now),
            Error: new CodeExecutionError("CodeModeUnavailable", "Code execution runtime not yet configured.", null)
        ));
    }
}