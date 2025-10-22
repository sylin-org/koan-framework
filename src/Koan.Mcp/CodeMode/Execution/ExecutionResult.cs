using System.Collections.Generic;

namespace Koan.Mcp.CodeMode.Execution;

/// <summary>
/// Result of code execution including output, logs, metrics, and errors.
/// </summary>
public sealed class ExecutionResult
{
    /// <summary>
    /// True if execution completed without errors.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Final output text set via SDK.Out.answer().
    /// </summary>
    public string? Output { get; init; }

    /// <summary>
    /// Log entries captured via SDK.Out.info() and SDK.Out.warn().
    /// </summary>
    public List<LogEntry> Logs { get; init; } = new();

    /// <summary>
    /// Execution performance metrics.
    /// </summary>
    public ExecutionMetrics Metrics { get; init; } = new();

    /// <summary>
    /// Error code if execution failed.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Error message if execution failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Source line number where error occurred (if available).
    /// </summary>
    public int? ErrorLine { get; init; }

    /// <summary>
    /// Source column number where error occurred (if available).
    /// </summary>
    public int? ErrorColumn { get; init; }

    /// <summary>
    /// Create a successful execution result.
    /// </summary>
    public static ExecutionResult FromOutput(string? output, List<LogEntry> logs, ExecutionMetrics metrics)
    {
        return new ExecutionResult
        {
            Success = true,
            Output = output,
            Logs = logs,
            Metrics = metrics
        };
    }

    /// <summary>
    /// Create a failed execution result.
    /// </summary>
    public static ExecutionResult FromError(string errorCode, string errorMessage, int? line = null, int? column = null)
    {
        return new ExecutionResult
        {
            Success = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            ErrorLine = line,
            ErrorColumn = column
        };
    }
}

/// <summary>
/// Log entry captured during code execution.
/// </summary>
public sealed class LogEntry
{
    /// <summary>
    /// Log level: info, warn, error.
    /// </summary>
    public string Level { get; init; } = "info";

    /// <summary>
    /// Log message text.
    /// </summary>
    public string Message { get; init; } = "";
}

/// <summary>
/// Performance metrics for code execution.
/// </summary>
public sealed class ExecutionMetrics
{
    /// <summary>
    /// Total execution time in milliseconds.
    /// </summary>
    public double ExecutionMs { get; init; }

    /// <summary>
    /// Peak memory usage in megabytes.
    /// </summary>
    public double MemoryMb { get; init; }

    /// <summary>
    /// Number of entity operation calls made.
    /// </summary>
    public int EntityCalls { get; init; }
}
