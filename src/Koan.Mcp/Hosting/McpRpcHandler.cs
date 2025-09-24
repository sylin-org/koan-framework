using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Koan.Mcp.Execution;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace Koan.Mcp.Hosting;

public sealed class McpRpcHandler
{
    private readonly McpEntityRegistry _registry;
    private readonly EndpointToolExecutor _executor;
    private readonly ILogger<McpRpcHandler> _logger;

    public McpRpcHandler(McpEntityRegistry registry, EndpointToolExecutor executor, ILogger<McpRpcHandler> logger)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [JsonRpcMethod("tools/list")]
    public Task<ToolsListResponse> ListToolsAsync(CancellationToken cancellationToken)
    {
        var tools = _registry.Registrations
            .SelectMany(registration => registration.Tools.Select(tool => ToolDescriptor.From(registration, tool)))
            .ToArray();

        var response = new ToolsListResponse
        {
            Tools = tools,
            Next = null
        };

        return Task.FromResult(response);
    }

    [JsonRpcMethod("tools/call")]
    public async Task<ToolsCallResult> CallToolAsync(ToolsCallParams parameters, CancellationToken cancellationToken)
    {
        if (parameters is null) throw new ArgumentNullException(nameof(parameters));
        _logger.LogDebug("MCP tools/call for {Tool} invoked.", parameters.Name);

        var result = await _executor.ExecuteAsync(parameters.Name, parameters.Arguments, cancellationToken).ConfigureAwait(false);
        return ToolsCallResult.FromExecution(parameters.Name, result);
    }

    [JsonRpcMethod("ping")]
    public Task<string> PingAsync() => Task.FromResult("pong");

    public sealed class ToolsCallParams
    {
        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("arguments")]
        public JsonObject? Arguments { get; init; }
    }

    public sealed class ToolsListResponse
    {
        [JsonPropertyName("tools")]
        public IReadOnlyList<ToolDescriptor> Tools { get; init; } = Array.Empty<ToolDescriptor>();

        [JsonPropertyName("next")]
        public object? Next { get; init; }
    }

    public sealed class ToolDescriptor
    {
        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("input_schema")]
        public required JsonObject InputSchema { get; init; }

        [JsonPropertyName("metadata")]
        public required JsonObject Metadata { get; init; }

        public static ToolDescriptor From(McpEntityRegistration registration, McpToolDefinition tool)
        {
            var metadata = new JsonObject
            {
                ["entity"] = registration.DisplayName,
                ["operation"] = tool.Operation.ToString(),
                ["returnsCollection"] = tool.ReturnsCollection,
                ["isMutation"] = tool.IsMutation,
                ["requiredScopes"] = new JsonArray(tool.RequiredScopes.Select(scope => (JsonNode)scope).ToArray())
            };

            return new ToolDescriptor
            {
                Name = tool.Name,
                Description = tool.Description,
                InputSchema = (JsonObject)tool.InputSchema.DeepClone(),
                Metadata = metadata
            };
        }
    }

    public sealed class ToolsCallResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("result")]
        public JsonNode? Result { get; init; }

        [JsonPropertyName("short_circuit")]
        public JsonNode? ShortCircuit { get; init; }

        [JsonPropertyName("headers")]
        public JsonObject Headers { get; init; } = new();

        [JsonPropertyName("warnings")]
        public JsonArray Warnings { get; init; } = new();

        [JsonPropertyName("diagnostics")]
        public JsonObject Diagnostics { get; init; } = new();

        [JsonPropertyName("error_code")]
        public string? ErrorCode { get; init; }

        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; init; }

        public static ToolsCallResult FromExecution(string toolName, McpToolExecutionResult execution)
        {
            if (execution.Success)
            {
                return new ToolsCallResult
                {
                    Success = true,
                    Result = execution.Payload,
                    ShortCircuit = execution.ShortCircuit,
                    Headers = ToJsonObject(execution.Headers),
                    Warnings = ToJsonArray(execution.Warnings),
                    Diagnostics = execution.Diagnostics
                };
            }

            var diagnostics = execution.Diagnostics.DeepClone().AsObject();
            diagnostics["tool"] = toolName;
            diagnostics["error"] = execution.ErrorCode ?? "execution_error";

            return new ToolsCallResult
            {
                Success = false,
                Headers = ToJsonObject(execution.Headers),
                Warnings = ToJsonArray(execution.Warnings),
                Diagnostics = diagnostics,
                ErrorCode = execution.ErrorCode ?? "execution_error",
                ErrorMessage = execution.ErrorMessage
            };
        }

        private static JsonObject ToJsonObject(IReadOnlyDictionary<string, string> headers)
        {
            var obj = new JsonObject();
            foreach (var kv in headers)
            {
                obj[kv.Key] = kv.Value;
            }
            return obj;
        }

        private static JsonArray ToJsonArray(IReadOnlyList<string> warnings)
        {
            var arr = new JsonArray();
            foreach (var warning in warnings)
            {
                arr.Add(warning);
            }
            return arr;
        }
    }
}
