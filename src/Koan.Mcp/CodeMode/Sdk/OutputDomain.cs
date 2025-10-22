using System.Collections.Generic;
using Koan.Mcp.CodeMode.Execution;

namespace Koan.Mcp.CodeMode.Sdk;

/// <summary>
/// SDK.Out.* - Output and logging functions exposed to JavaScript.
/// </summary>
public sealed class OutputDomain
{
    private string? _answer;
    private readonly List<LogEntry> _logs = new();

    /// <summary>
    /// SDK.Out.answer(text) - Set final answer to return to user.
    /// </summary>
    public void answer(string text)
    {
        _answer = text;
    }

    /// <summary>
    /// SDK.Out.info(message) - Log informational message.
    /// </summary>
    public void info(string message)
    {
        _logs.Add(new LogEntry { Level = "info", Message = message });
    }

    /// <summary>
    /// SDK.Out.warn(message) - Log warning message.
    /// </summary>
    public void warn(string message)
    {
        _logs.Add(new LogEntry { Level = "warn", Message = message });
    }

    /// <summary>
    /// Get the final answer text (internal use).
    /// </summary>
    internal string? GetAnswer() => _answer;

    /// <summary>
    /// Get all log entries (internal use).
    /// </summary>
    internal List<LogEntry> GetLogs() => _logs;
}
