using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Koan.Mcp.CodeMode.Execution;
using Koan.Mcp.CodeMode.Sdk;
using Koan.Mcp.Execution;
using Koan.Mcp.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StreamJsonRpc;

namespace Koan.Mcp.Hosting;

public sealed class McpRpcHandler
{
    private readonly McpEntityRegistry _registry;
    private readonly EndpointToolExecutor _executor;
    private readonly ILogger<McpRpcHandler> _logger;
    private readonly IServiceProvider _services;
    private readonly IOptions<McpServerOptions> _serverOptions;
    private readonly ICodeExecutor? _codeExecutor;

    public McpRpcHandler(
        McpEntityRegistry registry,
        EndpointToolExecutor executor,
        IServiceProvider services,
        IOptions<McpServerOptions> serverOptions,
        ILogger<McpRpcHandler> logger)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _serverOptions = serverOptions ?? throw new ArgumentNullException(nameof(serverOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Code executor is optional - may not be available if code mode is disabled
        _codeExecutor = services.GetService<ICodeExecutor>();
    }

    [JsonRpcMethod("tools/list")]
    public Task<ToolsListResponse> ListToolsAsync(CancellationToken cancellationToken)
    {
        var exposureMode = ResolveExposureMode();
        var toolsList = new List<ToolDescriptor>();

        // Add code execution tool if enabled
        if ((exposureMode == McpExposureMode.Code || exposureMode == McpExposureMode.Full) && _codeExecutor != null)
        {
            var codeModeTool = CreateCodeExecutionTool();
            toolsList.Add(codeModeTool);
        }

        // Add entity tools if enabled
        if (exposureMode == McpExposureMode.Tools || exposureMode == McpExposureMode.Full)
        {
            var entityTools = _registry.Registrations
                .SelectMany(registration => registration.Tools.Select(tool => ToolDescriptor.From(registration, tool)));
            toolsList.AddRange(entityTools);
        }

        var response = new ToolsListResponse
        {
            Tools = toolsList.ToArray(),
            Next = null
        };

        _logger.LogDebug(
            "Listing {Count} tools (exposure mode: {Mode})",
            toolsList.Count,
            exposureMode);

        return Task.FromResult(response);
    }

    [JsonRpcMethod("tools/call")]
    public async Task<ToolsCallResult> CallToolAsync(ToolsCallParams parameters, CancellationToken cancellationToken)
    {
        if (parameters is null) throw new ArgumentNullException(nameof(parameters));
        _logger.LogDebug("MCP tools/call for {Tool} invoked.", parameters.Name);

        // Handle code execution tool
        if (parameters.Name == "koan.code.execute")
        {
            return await ExecuteCodeAsync(parameters.Arguments, cancellationToken).ConfigureAwait(false);
        }

        // Handle traditional entity tools
        var result = await _executor.ExecuteAsync(parameters.Name, parameters.Arguments, cancellationToken).ConfigureAwait(false);
        return ToolsCallResult.FromExecution(parameters.Name, result);
    }

    [JsonRpcMethod("ping")]
    public Task<string> PingAsync() => Task.FromResult("pong");

    private McpExposureMode ResolveExposureMode()
    {
        // Check configuration
        var configuredMode = _serverOptions.Value.Exposure;
        if (configuredMode.HasValue && configuredMode.Value != McpExposureMode.Auto)
        {
            return configuredMode.Value;
        }

        // Check assembly attribute (scan all loaded assemblies)
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var assembly in assemblies)
        {
            var assemblyAttr = assembly.GetCustomAttributes(typeof(McpDefaultsAttribute), false)
                .OfType<McpDefaultsAttribute>()
                .FirstOrDefault();

            if (assemblyAttr?.ExposureMode.HasValue == true)
            {
                return assemblyAttr.ExposureMode.Value;
            }
        }

        // Default: Auto mode (falls back to Full for safety)
        // In production, could detect client capabilities from initialize handshake
        return McpExposureMode.Auto;
    }

    private ToolDescriptor CreateCodeExecutionTool()
    {
        var inputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["code"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "JavaScript code to execute. Has access to SDK.Entities.* and SDK.Out.* namespaces."
                },
                ["language"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray { "javascript" },
                    ["description"] = "Language of the code (currently only 'javascript' is supported).",
                    ["default"] = "javascript"
                }
            },
            ["required"] = new JsonArray { "code" }
        };

        var metadata = new JsonObject
        {
            ["codeMode"] = true,
            ["runtime"] = "Jint",
            ["sdkVersion"] = "1.0"
        };

        return new ToolDescriptor
        {
            Name = "koan.code.execute",
            Description = "Execute JavaScript code against Koan entity operations. Use SDK.Entities.* for entity operations and SDK.Out.answer() to return results.",
            InputSchema = inputSchema,
            Metadata = metadata
        };
    }

    private async Task<ToolsCallResult> ExecuteCodeAsync(JsonObject? arguments, CancellationToken cancellationToken)
    {
        if (_codeExecutor == null)
        {
            return new ToolsCallResult
            {
                Success = false,
                ErrorCode = "code_mode_disabled",
                ErrorMessage = "Code execution is not available (ICodeExecutor not registered)"
            };
        }

        // Extract code from arguments
        if (arguments == null || !arguments.TryGetPropertyValue("code", out var codeNode))
        {
            return new ToolsCallResult
            {
                Success = false,
                ErrorCode = "missing_code",
                ErrorMessage = "Missing required 'code' parameter"
            };
        }

        var code = codeNode?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(code))
        {
            return new ToolsCallResult
            {
                Success = false,
                ErrorCode = "empty_code",
                ErrorMessage = "Code parameter cannot be empty"
            };
        }

        try
        {
            // Create SDK bindings with scoped service provider
            using var scope = _services.CreateScope();
            var bindings = scope.ServiceProvider.GetRequiredService<KoanSdkBindings>();

            // Execute code
            var result = await _codeExecutor.ExecuteAsync(code, bindings, cancellationToken).ConfigureAwait(false);

            if (result.Success)
            {
                // Convert execution result to MCP tool result
                var resultPayload = new JsonObject
                {
                    ["output"] = result.Output,
                    ["metrics"] = new JsonObject
                    {
                        ["executionMs"] = result.Metrics.ExecutionMs,
                        ["memoryMb"] = result.Metrics.MemoryMb,
                        ["entityCalls"] = result.Metrics.EntityCalls
                    }
                };

                // Add logs if any
                if (result.Logs.Count > 0)
                {
                    resultPayload["logs"] = new JsonArray(
                        result.Logs.Select(log => (JsonNode)new JsonObject
                        {
                            ["level"] = log.Level,
                            ["message"] = log.Message
                        }).ToArray());
                }

                return new ToolsCallResult
                {
                    Success = true,
                    Result = resultPayload
                };
            }
            else
            {
                // Execution failed
                var diagnostics = new JsonObject
                {
                    ["errorCode"] = result.ErrorCode,
                    ["line"] = result.ErrorLine,
                    ["column"] = result.ErrorColumn
                };

                return new ToolsCallResult
                {
                    Success = false,
                    ErrorCode = result.ErrorCode ?? "execution_failed",
                    ErrorMessage = result.ErrorMessage ?? "Code execution failed",
                    Diagnostics = diagnostics
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during code execution");

            return new ToolsCallResult
            {
                Success = false,
                ErrorCode = "unexpected_error",
                ErrorMessage = $"Unexpected error: {ex.Message}"
            };
        }
    }

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
