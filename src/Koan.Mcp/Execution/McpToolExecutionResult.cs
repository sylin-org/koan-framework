using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Execution;

public sealed class McpToolExecutionResult
{
    private McpToolExecutionResult(
        bool success,
        JToken? payload,
        JToken? shortCircuit,
        IReadOnlyDictionary<string, string> headers,
        IReadOnlyList<string> warnings,
        JObject diagnostics,
        string? errorCode,
        string? errorMessage)
    {
        Success = success;
        Payload = payload;
        ShortCircuit = shortCircuit;
        Headers = headers;
        Warnings = warnings;
        Diagnostics = diagnostics;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public bool Success { get; }

    public JToken? Payload { get; }

    public JToken? ShortCircuit { get; }

    public IReadOnlyDictionary<string, string> Headers { get; }

    public IReadOnlyList<string> Warnings { get; }

    public JObject Diagnostics { get; }

    public string? ErrorCode { get; }

    public string? ErrorMessage { get; }

    public static McpToolExecutionResult SuccessResult(
        JToken? payload,
        JToken? shortCircuit,
        IReadOnlyDictionary<string, string> headers,
        IReadOnlyList<string> warnings,
        JObject diagnostics)
    {
        return new McpToolExecutionResult(
            true,
            payload,
            shortCircuit,
            headers,
            warnings,
            diagnostics,
            null,
            null);
    }

    public static McpToolExecutionResult Failure(
        string errorCode,
        string errorMessage,
        JObject? diagnostics = null)
    {
        return new McpToolExecutionResult(
            false,
            null,
            null,
            new Dictionary<string, string>(),
            Array.Empty<string>(),
            diagnostics ?? new JObject(),
            errorCode,
            errorMessage);
    }
}
