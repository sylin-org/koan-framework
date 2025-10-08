using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Koan.Mcp.CodeMode.Execution;
using Koan.Mcp.CodeExecution;
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
    private readonly Koan.Mcp.CodeExecution.ICodeExecutor? _codeExecutor;

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
    _codeExecutor = services.GetService<Koan.Mcp.CodeExecution.ICodeExecutor>();
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
            // Syntax validation tool (lightweight) – does not execute code, only parses
            var validateTool = CreateCodeValidationTool();
            toolsList.Add(validateTool);
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
        // Handle code validation tool
        if (parameters.Name == "koan.code.validate")
        {
            return ExecuteCodeValidation(parameters.Arguments);
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

        // Default: Auto mode. For now we TREAT Auto as Full until client capability
        // detection is implemented (handshake inspection). This ensures legacy MCP
        // clients still see both code + entity tools instead of an empty set.
        // TODO(S16): Implement handshake capability detection and choose Code or Tools
        // dynamically; then return McpExposureMode.Auto here and handle mapping in caller.
        return McpExposureMode.Full;
    }

    private ToolDescriptor CreateCodeExecutionTool()
    {
        var inputSchema = new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["code"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "JavaScript code to execute. Has access to SDK.Entities.* and SDK.Out.* namespaces."
                },
                ["language"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray { "javascript" },
                    ["description"] = "Language of the code (currently only 'javascript' is supported).",
                    ["default"] = "javascript"
                },
                ["entryFunction"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "Optional entry function to invoke after evaluating code (defaults to run())."
                },
                ["set"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "Optional entity projection set for default operations."
                },
                ["correlationId"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "Optional correlation identifier used for tracing/auditing."
                }
            },
            ["required"] = new JArray { "code" }
        };

        var metadata = new JObject
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

    private ToolDescriptor CreateCodeValidationTool()
    {
        var inputSchema = new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["code"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "JavaScript source to validate for basic syntax issues. No execution occurs."
                },
                ["language"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray { "javascript" },
                    ["default"] = "javascript"
                }
            },
            ["required"] = new JArray { "code" }
        };
        var metadata = new JObject
        {
            ["codeMode"] = true,
            ["validation"] = true,
            ["runtime"] = "Jint"
        };
        return new ToolDescriptor
        {
            Name = "koan.code.validate",
            Description = "Validate JavaScript code for syntax errors before execution.",
            InputSchema = inputSchema,
            Metadata = metadata
        };
    }

    private ToolsCallResult ExecuteCodeValidation(JObject? arguments)
    {
        if (_codeExecutor is not JintCodeExecutor jint)
        {
            return new ToolsCallResult
            {
                Success = false,
                ErrorCode = CodeMode.Execution.CodeModeErrorCodes.ExecutionError,
                ErrorMessage = "Validation unavailable: code executor not present"
            };
        }
        if (arguments == null || !arguments.TryGetValue("code", StringComparison.OrdinalIgnoreCase, out var codeNode))
        {
            return new ToolsCallResult
            {
                Success = true,
                Result = new JObject { ["valid"] = false, ["error"] = "Missing required 'code' parameter" }
            };
        }
        var code = codeNode?.Value<string>() ?? string.Empty;
        if (jint.ValidateSyntax(code, out var error))
        {
            return new ToolsCallResult
            {
                Success = true,
                Result = new JObject { ["valid"] = true }
            };
        }
        return new ToolsCallResult
        {
            Success = true,
            Result = new JObject { ["valid"] = false, ["error"] = error ?? "Unknown validation error" }
        };
    }

    private async Task<ToolsCallResult> ExecuteCodeAsync(JObject? arguments, CancellationToken cancellationToken)
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
        if (arguments == null || !arguments.TryGetValue("code", StringComparison.OrdinalIgnoreCase, out var codeNode))
        {
            return new ToolsCallResult
            {
                Success = false,
                ErrorCode = "missing_code",
                ErrorMessage = "Missing required 'code' parameter"
            };
        }
        var code = codeNode?.Value<string>();
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

            // Build execution request from arguments
            var request = BuildExecutionRequest(arguments!, code);

            // Execute code via unified executor (bindings are created internally in implementation; we keep existing for side-effects if needed in future)
            var result = await _codeExecutor.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);

            if (result.Success)
            {
                var resultPayload = new JObject
                {
                    ["text"] = result.TextResponse,
                    ["logs"] = new JArray(result.Logs.Select(l => JValue.CreateString(l)) ),
                    ["diagnostics"] = new JObject
                    {
                        ["sdkCalls"] = result.Diagnostics.SdkCalls,
                        ["cpuMs"] = result.Diagnostics.CpuMs,
                        ["memoryBytes"] = result.Diagnostics.MemoryBytes,
                        ["scriptLength"] = result.Diagnostics.ScriptLength,
                        ["startedUtc"] = result.Diagnostics.StartedUtc,
                        ["completedUtc"] = result.Diagnostics.CompletedUtc
                    }
                };

                return new ToolsCallResult
                {
                    Success = true,
                    Result = resultPayload
                };
            }
            else
            {
                var diagnostics = new JObject
                {
                    ["errorCode"] = result.Error?.Type,
                    ["message"] = result.Error?.Message,
                    ["sdkCalls"] = result.Diagnostics.SdkCalls,
                    ["cpuMs"] = result.Diagnostics.CpuMs,
                    ["scriptLength"] = result.Diagnostics.ScriptLength
                };

                return new ToolsCallResult
                {
                    Success = false,
                    ErrorCode = result.Error?.Type ?? "execution_failed",
                    ErrorMessage = result.Error?.Message ?? "Code execution failed",
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

    private static CodeExecutionRequest BuildExecutionRequest(JObject arguments, string code)
    {
        string? TryGetString(string name)
            => arguments.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var node) ? node?.Value<string>() : null;

        return new CodeExecutionRequest(
            Source: code,
            Language: TryGetString("language"),
            EntryFunction: TryGetString("entryFunction"),
            UserId: null,
            Set: TryGetString("set"),
            CorrelationId: TryGetString("correlationId"));
    }

    public sealed class ToolsCallParams
    {
        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("arguments")]
    public JObject? Arguments { get; init; }
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
    public required JObject InputSchema { get; init; }

        [JsonPropertyName("metadata")]
    public required JObject Metadata { get; init; }

        public static ToolDescriptor From(McpEntityRegistration registration, McpToolDefinition tool)
        {
            var metadata = new JObject
            {
                ["entity"] = registration.DisplayName,
                ["operation"] = tool.Operation.ToString(),
                ["returnsCollection"] = tool.ReturnsCollection,
                ["isMutation"] = tool.IsMutation,
                ["requiredScopes"] = new JArray(tool.RequiredScopes.Select(scope => JValue.CreateString(scope)))
            };

            return new ToolDescriptor
            {
                Name = tool.Name,
                Description = tool.Description,
                InputSchema = tool.InputSchema,
                Metadata = metadata
            };
        }
    }

    public sealed class ToolsCallResult
    {
        public bool Success { get; init; }
        public JToken? Result { get; init; }
        public JToken? ShortCircuit { get; init; }
        public JObject Headers { get; init; } = new();
        public JArray Warnings { get; init; } = new();
        public JObject Diagnostics { get; init; } = new();
        public string? ErrorCode { get; init; }
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
            var diagnostics = (execution.Diagnostics.DeepClone() as JObject) ?? new JObject();
            diagnostics["tool"] = toolName;
            diagnostics["error"] = execution.ErrorCode ?? CodeMode.Execution.CodeModeErrorCodes.ExecutionError;

            return new ToolsCallResult
            {
                Success = false,
                Headers = ToJsonObject(execution.Headers),
                Warnings = ToJsonArray(execution.Warnings),
                Diagnostics = diagnostics,
                ErrorCode = execution.ErrorCode ?? CodeMode.Execution.CodeModeErrorCodes.ExecutionError,
                ErrorMessage = execution.ErrorMessage
            };
        }

        private static JObject ToJsonObject(IReadOnlyDictionary<string, string> headers)
        {
            var obj = new JObject();
            foreach (var kv in headers)
            {
                obj[kv.Key] = kv.Value;
            }
            return obj;
        }

        private static JArray ToJsonArray(IReadOnlyList<string> warnings)
        {
            var arr = new JArray();
            foreach (var warning in warnings)
            {
                arr.Add(warning);
            }
            return arr;
        }
    }
}
