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
        string? errorMessage,
        JObject? problem = null)
    {
        Success = success;
        Problem = problem;
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

    /// <summary>The refusal's own problem document, where the endpoint answered one, carried as the edge carries every refusal's.</summary>
    public JObject? Problem { get; }

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
            [],
            diagnostics ?? new JObject(),
            errorCode,
            errorMessage);
    }

    /// <summary>
    /// SEC-0010: the shared endpoint refused the call, by an authorization denial or a short circuit carrying an
    /// error status. A failure that keeps the rendered short circuit and the response metadata, so the caller sees why.
    /// </summary>
    public static McpToolExecutionResult Refused(
        string errorCode,
        string errorMessage,
        JToken? shortCircuit,
        IReadOnlyDictionary<string, string> headers,
        IReadOnlyList<string> warnings,
        JObject diagnostics,
        JObject? problem = null)
    {
        return new McpToolExecutionResult(
            false,
            null,
            shortCircuit,
            headers,
            warnings,
            diagnostics,
            errorCode,
            errorMessage,
            problem);
    }
}
