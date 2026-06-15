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
    private readonly Koan.Mcp.CustomTools.McpCustomToolRegistry? _customTools;
    private readonly Koan.Mcp.CustomTools.McpCustomToolInvoker? _customInvoker;

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

        // Custom [McpTool] verbs are optional (registered by AddKoanMcp).
        _customTools = services.GetService<Koan.Mcp.CustomTools.McpCustomToolRegistry>();
        _customInvoker = services.GetService<Koan.Mcp.CustomTools.McpCustomToolInvoker>();
    }

    [JsonRpcMethod("tools/list")]
    public Task<ToolsListResponse> ListTools(CancellationToken cancellationToken)
    {
        var exposureMode = ResolveExposureMode();
        var toolsList = new List<ToolDescriptor>();

        // Add code execution tool if enabled. CodeModeOptions.Enabled is the kill switch honored by
        // the capabilities/SDK endpoints (EndpointRouteBuilderExtensions) — list and invoke must agree
        // with it, or a disabled-but-Full host advertises a tool the RPC path then refuses.
        if ((exposureMode == McpExposureMode.Code || exposureMode == McpExposureMode.Full) && IsCodeModeEnabled())
        {
            var codeModeTool = CreateCodeExecutionTool();
            toolsList.Add(codeModeTool);
            // Syntax validation tool (lightweight) – does not execute code, only parses
            var validateTool = CreateCodeValidationTool();
            toolsList.Add(validateTool);
        }

        // Add entity tools + custom [McpTool] verbs if enabled
        if (exposureMode == McpExposureMode.Tools || exposureMode == McpExposureMode.Full)
        {
            var entityTools = _registry.Registrations
                .SelectMany(registration => registration.Tools.Select(tool => ToolDescriptor.From(registration, tool)));
            toolsList.AddRange(entityTools);

            if (_customTools is not null)
            {
                toolsList.AddRange(_customTools.Tools.Select(ToolDescriptor.FromCustom));
            }
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
    public async Task<CallToolResult> CallTool(ToolsCallParams parameters, CancellationToken cancellationToken)
    {
        if (parameters is null) throw new ArgumentNullException(nameof(parameters));
        _logger.LogDebug("MCP tools/call for {Tool} invoked.", parameters.Name);

        // Handle code execution tool
        if (parameters.Name == "koan.code.execute")
        {
            var res = await ExecuteCode(parameters.Arguments, cancellationToken);
            return ToCallToolResult(res);
        }
        // Handle code validation tool
        if (parameters.Name == "koan.code.validate")
        {
            var res = ExecuteCodeValidation(parameters.Arguments);
            return ToCallToolResult(res);
        }

        // Handle custom [McpTool] verbs
        if (_customTools is not null && _customInvoker is not null && _customTools.TryGet(parameters.Name, out var customTool))
        {
            try
            {
                var token = await _customInvoker.Invoke(customTool, parameters.Arguments, _services, cancellationToken);
                return BuildCustomResult(token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Custom MCP tool '{Tool}' failed.", parameters.Name);
                return new CallToolResult
                {
                    IsError = true,
                    Content = new List<McpContent> { new McpContent { Type = "text", Text = ex.Message } }
                };
            }
        }

        // Handle traditional entity tools
        var result = await _executor.Execute(parameters.Name, parameters.Arguments, cancellationToken);
        return ToCallToolResult(parameters.Name, result);
    }

    private static CallToolResult BuildCustomResult(JToken result)
    {
        var text = result.Type == JTokenType.String
            ? result.Value<string>() ?? string.Empty
            : result.ToString(Newtonsoft.Json.Formatting.None);

        return new CallToolResult
        {
            Content = new List<McpContent> { new McpContent { Type = "text", Text = text } }
        };
    }

    [JsonRpcMethod("ping")]
    public Task<string> Ping() => Task.FromResult("pong");

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

    // CodeModeOptions.Enabled is the functional kill switch for code mode (AI-0014). It defaults to
    // true, and a misconfigured/partial host without the options registered fails closed. This is the
    // discovery-side gate (tools/list); the invoke-side gate is TryDenyCodeExecution, which also
    // enforces the resolved exposure mode — keep the two in sync when adding a new condition.
    private bool IsCodeModeEnabled()
        => _codeExecutor != null
           && (_services.GetService<IOptions<CodeModeOptions>>()?.Value.Enabled ?? false);

    // Defense-in-depth gate for the RPC invoke-by-name path. Hiding koan.code.execute /
    // koan.code.validate from tools/list is not a security control on its own — a client can still
    // call them by name. Both the disabled kill switch and the active exposure mode are enforced here
    // so the invoke path cannot run sandboxed JavaScript that discovery would never have surfaced.
    private bool TryDenyCodeExecution(out ToolsCallResult denial)
    {
        var enabled = _services.GetService<IOptions<CodeModeOptions>>()?.Value.Enabled ?? false;
        if (!enabled)
        {
            denial = CodeModeDisabled("Code mode is disabled (Koan:Mcp:CodeMode:Enabled=false).");
            return true;
        }

        var exposureMode = ResolveExposureMode();
        if (exposureMode != McpExposureMode.Code && exposureMode != McpExposureMode.Full)
        {
            denial = CodeModeDisabled($"Code execution is not exposed under exposure mode '{exposureMode}'.");
            return true;
        }

        if (_codeExecutor == null)
        {
            denial = CodeModeDisabled("Code execution is not available (ICodeExecutor not registered).");
            return true;
        }

        denial = null!;
        return false;
    }

    private static ToolsCallResult CodeModeDisabled(string message) => new()
    {
        Success = false,
        ErrorCode = CodeModeErrorCodes.CodeModeDisabled,
        ErrorMessage = message
    };

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
        if (TryDenyCodeExecution(out var denial)) return denial;
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
        var code = codeNode?.Value<string>() ?? "";
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

    private async Task<ToolsCallResult> ExecuteCode(JObject? arguments, CancellationToken cancellationToken)
    {
        if (TryDenyCodeExecution(out var denial)) return denial;

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

            // Execute code via unified executor (bindings are created internally in implementation; we keep existing for side-effects if needed in future).
            // TryDenyCodeExecution above guarantees _codeExecutor is non-null on this path.
            var result = await _codeExecutor!.Execute(request, cancellationToken);

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
        [Newtonsoft.Json.JsonProperty("tools")]
        public IReadOnlyList<ToolDescriptor> Tools { get; init; } = [];

        [JsonPropertyName("next")]
        [Newtonsoft.Json.JsonProperty("next")]
        public object? Next { get; init; }
    }

    public sealed class ToolDescriptor
    {
        [JsonPropertyName("name")]
        [Newtonsoft.Json.JsonProperty("name")]
        public required string Name { get; init; }

        [JsonPropertyName("description")]
        [Newtonsoft.Json.JsonProperty("description")]
        public string? Description { get; init; }

        [JsonPropertyName("input_schema")]
        [Newtonsoft.Json.JsonProperty("input_schema")]
        public required JObject InputSchema { get; init; }

        [JsonPropertyName("metadata")]
        [Newtonsoft.Json.JsonProperty("metadata")]
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

        public static ToolDescriptor FromCustom(Koan.Mcp.CustomTools.McpCustomTool tool)
        {
            var metadata = new JObject
            {
                ["custom"] = true,
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

    private static CallToolResult ToCallToolResult(ToolsCallResult result)
    {
        if (result.Success)
        {
            string text = "";
            JObject? meta = null;

            if (result.Result is JObject obj && obj.TryGetValue("text", out var textNode))
            {
                text = textNode.Value<string>() ?? "";
                meta = new JObject
                {
                    ["logs"] = obj["logs"],
                    ["diagnostics"] = obj["diagnostics"],
                    ["headers"] = result.Headers,
                    ["warnings"] = result.Warnings
                };
            }
            else
            {
                text = result.Result?.ToString(Newtonsoft.Json.Formatting.None) ?? "";
                meta = new JObject
                {
                    ["headers"] = result.Headers,
                    ["warnings"] = result.Warnings,
                    ["diagnostics"] = result.Diagnostics
                };
            }

            return new CallToolResult
            {
                Content = new List<McpContent>
                {
                    new McpContent
                    {
                        Type = "text",
                        Text = text
                    }
                },
                Meta = meta
            };
        }
        else
        {
            return new CallToolResult
            {
                IsError = true,
                Content = new List<McpContent>
                {
                    new McpContent
                    {
                        Type = "text",
                        Text = result.ErrorMessage ?? "Execution failed"
                    }
                },
                Meta = new JObject
                {
                    ["errorCode"] = result.ErrorCode,
                    ["headers"] = result.Headers,
                    ["warnings"] = result.Warnings,
                    ["diagnostics"] = result.Diagnostics
                }
            };
        }
    }

    private static CallToolResult ToCallToolResult(string toolName, McpToolExecutionResult execution)
    {
        if (execution.Success)
        {
            return new CallToolResult
            {
                Content = new List<McpContent>
                {
                    new McpContent
                    {
                        Type = "text",
                        Text = execution.Payload?.ToString(Newtonsoft.Json.Formatting.None) ?? ""
                    }
                },
                Meta = new JObject
                {
                    ["shortCircuit"] = execution.ShortCircuit,
                    ["headers"] = ToJsonObject(execution.Headers),
                    ["warnings"] = ToJsonArray(execution.Warnings),
                    ["diagnostics"] = execution.Diagnostics
                }
            };
        }
        else
        {
            var diagnostics = (execution.Diagnostics.DeepClone() as JObject) ?? new JObject();
            diagnostics["tool"] = toolName;
            diagnostics["error"] = execution.ErrorCode ?? CodeMode.Execution.CodeModeErrorCodes.ExecutionError;

            return new CallToolResult
            {
                IsError = true,
                Content = new List<McpContent>
                {
                    new McpContent
                    {
                        Type = "text",
                        Text = execution.ErrorMessage ?? "Tool execution failed"
                    }
                },
                Meta = new JObject
                {
                    ["errorCode"] = execution.ErrorCode ?? CodeMode.Execution.CodeModeErrorCodes.ExecutionError,
                    ["headers"] = ToJsonObject(execution.Headers),
                    ["warnings"] = ToJsonArray(execution.Warnings),
                    ["diagnostics"] = diagnostics
                }
            };
        }
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

    public sealed class CallToolResult
    {
        [JsonPropertyName("content")]
        [Newtonsoft.Json.JsonProperty("content")]
        public List<McpContent> Content { get; init; } = new();

        [JsonPropertyName("isError")]
        [Newtonsoft.Json.JsonProperty("isError")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool IsError { get; set; }

        [JsonPropertyName("meta")]
        [Newtonsoft.Json.JsonProperty("meta")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JObject? Meta { get; set; }
    }

    public sealed class McpContent
    {
        [JsonPropertyName("type")]
        [Newtonsoft.Json.JsonProperty("type")]
        public string Type { get; set; } = "text";

        [JsonPropertyName("text")]
        [Newtonsoft.Json.JsonProperty("text")]
        public string Text { get; set; } = "";
    }
}
