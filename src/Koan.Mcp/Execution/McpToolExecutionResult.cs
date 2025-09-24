using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Koan.Mcp.Execution;

public sealed class McpToolExecutionResult
{
    private McpToolExecutionResult(
        bool success,
        JsonNode? payload,
        JsonNode? shortCircuit,
        IReadOnlyDictionary<string, string> headers,
        IReadOnlyList<string> warnings,
        JsonObject diagnostics,
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

    public JsonNode? Payload { get; }

    public JsonNode? ShortCircuit { get; }

    public IReadOnlyDictionary<string, string> Headers { get; }

    public IReadOnlyList<string> Warnings { get; }

    public JsonObject Diagnostics { get; }

    public string? ErrorCode { get; }

    public string? ErrorMessage { get; }

    public static McpToolExecutionResult SuccessResult(
        JsonNode? payload,
        JsonNode? shortCircuit,
        IReadOnlyDictionary<string, string> headers,
        IReadOnlyList<string> warnings,
        JsonObject diagnostics)
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
        JsonObject? diagnostics = null)
    {
        return new McpToolExecutionResult(
            false,
            null,
            null,
            new Dictionary<string, string>(),
            Array.Empty<string>(),
            diagnostics ?? new JsonObject(),
            errorCode,
            errorMessage);
    }
}
