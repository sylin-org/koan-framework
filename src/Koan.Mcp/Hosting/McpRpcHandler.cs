using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Koan.Mcp.CodeMode.Execution;
using Koan.Mcp.CodeExecution;
using Koan.Mcp.CodeMode.Sdk;
using Koan.Mcp.Execution;
using Koan.Mcp.Options;
using Koan.Mcp.Resources;
using Koan.Web.Endpoints;
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
    private readonly IReadOnlyList<Koan.Mcp.Resources.IMcpResourceProvider> _resourceProviders;

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

        // P1.2: introspection resource providers (the framework ships koan://entities; apps + AN8 add more).
        _resourceProviders = services.GetServices<Koan.Mcp.Resources.IMcpResourceProvider>().ToArray();
    }

    /// <summary>WEB-0072 — the MCP <c>initialize</c> handshake. Advertises the protocol version + capabilities and,
    /// crucially, the server identity (<c>serverInfo</c> from <c>[KoanApp]</c>) and <c>instructions</c> (the
    /// LLM-facing guidance) the client surfaces. Sourced declare-once from the application identity.</summary>
    [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
    public Task<InitializeResult> Initialize(InitializeParams? parameters, CancellationToken cancellationToken)
        => Task.FromResult(BuildInitializeResult(parameters));

    public const string DefaultProtocolVersion = "2025-06-18";

    /// <summary>The provider this handler resolves from; transports open per-listing scopes from it (SEC-0011).</summary>
    internal IServiceProvider Services => _services;

    internal InitializeResult BuildInitializeResult(InitializeParams? parameters)
    {
        var app = Koan.Core.KoanEnv.CurrentSnapshot.Application;
        var options = _serverOptions.Value;
        var instructions = !string.IsNullOrWhiteSpace(options.Instructions)
            ? options.Instructions
            : (string.IsNullOrWhiteSpace(app.Description) ? null : app.Description);

        return new InitializeResult
        {
            ProtocolVersion = string.IsNullOrWhiteSpace(parameters?.ProtocolVersion) ? DefaultProtocolVersion : parameters!.ProtocolVersion!,
            Capabilities = new JObject
            {
                ["tools"] = new JObject { ["listChanged"] = false },
                ["resources"] = new JObject { ["listChanged"] = false },
            },
            ServerInfo = new ServerInfo
            {
                Name = app.Name,
                Version = typeof(McpRpcHandler).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            },
            Instructions = instructions,
        };
    }

    /// <summary>
    /// SEC-0017: the one principal a raw JSON-RPC channel (STDIO) acts as, for listing, resources and calls alike: an
    /// anonymous caller stamped <c>origin:local</c>. The caller is nobody, yet the call demonstrably arrived from the
    /// local process, so an <c>[Access(... "origin:local")]</c> gate admits it while an <c>authenticated</c> gate does not.
    /// </summary>
    public static ClaimsPrincipal StdioPrincipal()
        => Koan.Web.Authorization.OriginStamp.Apply(new ClaimsPrincipal(), Koan.Web.Authorization.OriginTier.Local);

    // SEC-0017: what a raw JSON-RPC (STDIO) caller is listed: the exposed tools, with each kept only when the STDIO
    // principal may call it (SEC-0011, ARCH-0141).
    [JsonRpcMethod("tools/list")]
    public async Task<ToolsListResponse> ListTools(CancellationToken cancellationToken)
    {
        var exposed = await ListExposedTools(cancellationToken).ConfigureAwait(false);
        var gates = _services.GetRequiredService<Koan.Web.Authorization.IAccessGateCache>();
        using var visibility = McpVerbVisibility.For(_services.GetService<IServiceScopeFactory>(), gates, StdioPrincipal());
        var listed = new List<ToolDescriptor>(exposed.Tools.Count);
        foreach (var tool in exposed.Tools)
        {
            var callable = _registry.TryGetTool(tool.Name, out var registration, out var definition)
                ? await visibility.Allows(registration.EntityType, definition.Operation, cancellationToken).ConfigureAwait(false)
                : _customTools is null || !_customTools.TryGet(tool.Name, out var custom)
                  || await visibility.Allows(custom, cancellationToken).ConfigureAwait(false);
            if (callable) listed.Add(tool);
        }
        return new ToolsListResponse { Tools = listed.ToArray() };
    }

    /// <summary>The tools the exposure mode surfaces, before any per-caller authorization; each transport filters it
    /// for its own principal.</summary>
    internal Task<ToolsListResponse> ListExposedTools(CancellationToken cancellationToken)
    {
        var exposureMode = ResolveExposureMode();
        var toolsList = new List<ToolDescriptor>();

        // Add code execution tool if enabled. CodeModeOptions.Enabled is the kill switch honored by
        // the capabilities/SDK endpoints (EndpointRouteBuilderExtensions) — list and invoke must agree
        // with it, or a disabled-but-Full host advertises a tool the RPC path then refuses.
        if (McpExposure.ExposesCode(exposureMode) && IsCodeModeEnabled())
        {
            var codeModeTool = CreateCodeExecutionTool();
            toolsList.Add(codeModeTool);
            // Syntax validation tool (lightweight) – does not execute code, only parses
            var validateTool = CreateCodeValidationTool();
            toolsList.Add(validateTool);
        }

        // Add entity tools + custom [McpTool] verbs if enabled
        if (McpExposure.ExposesTools(exposureMode))
        {
            var entityTools = _registry.Registrations
                .SelectMany(registration => registration.Tools.Select(tool => ToolDescriptor.From(registration, tool)));
            toolsList.AddRange(entityTools);

            if (_customTools is not null)
            {
                // P3.2: a verb on a config-disabled operational toolset is absent from the surface (global gate —
                // applies on STDIO + remote, unlike the per-caller visibility filter). The grant is enforced separately.
                toolsList.AddRange(_customTools.Tools
                    .Where(tool => tool.OperationalToolsetKey is not { Length: > 0 } key || _serverOptions.Value.IsOperationalToolsetEnabled(key))
                    .OrderBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(ToolDescriptor.FromCustom));
            }
        }

        var response = new ToolsListResponse
        {
            Tools = toolsList.ToArray()
        };

        _logger.LogDebug(
            "Listing {Count} tools (exposure mode: {Mode})",
            toolsList.Count,
            exposureMode);

        return Task.FromResult(response);
    }

    // SEC-0004 Phase 3.3: the JSON-RPC entry point. STDIO (and any raw JSON-RPC dispatch) reaches the handler
    // here with NO caller identity, but the channel IS the local process owner — so it delegates with an
    // anonymous principal STAMPED origin:local (SEC-0004): the *caller* is nobody (unauthenticated), yet the call
    // demonstrably arrived over STDIO, so an [Access(... "origin:local")] gate admits it while a remote call does
    // not. The HTTP/SSE bridge calls CallToolFor with the session principal (stamped remote/internal) instead.
    [JsonRpcMethod("tools/call", UseSingleObjectParameterDeserialization = true)]
    public Task<CallToolResult> CallTool(ToolsCallParams parameters, CancellationToken cancellationToken)
        => CallToolFor(parameters, StdioPrincipal(), cancellationToken);

    /// <summary>
    /// SEC-0004 Phase 3.3 — execute a tool AS <paramref name="user"/> (null = anonymous). The principal threads
    /// to <c>EntityRequestContext.User</c> so the data-layer gate / constrain / projection see the real caller.
    /// Mirrors the resources path's <c>ListResourcesFor</c> / <c>ReadResourceFor</c> convention.
    /// </summary>
    public async Task<CallToolResult> CallToolFor(ToolsCallParams parameters, ClaimsPrincipal? user, CancellationToken cancellationToken)
    {
        if (parameters is null) throw new ArgumentNullException(nameof(parameters));
        _logger.LogDebug("MCP tools/call for {Tool} invoked.", parameters.Name);

        // Handle code execution tool
        if (parameters.Name == "koan_code_execute")
        {
            var res = await ExecuteCode(parameters.Arguments, user, cancellationToken);
            return ToCallToolResult(res);
        }
        // Handle code validation tool
        if (parameters.Name == "koan_code_validate")
        {
            var res = ExecuteCodeValidation(parameters.Arguments);
            return ToCallToolResult(res);
        }

        // SEC-0014: hiding a tool from tools/list is not a control on its own. Entity tools and custom verbs run only
        // under an exposure mode that lists them; code-mode SDK calls reach the executor directly and are unaffected.
        var exposure = ResolveExposureMode();
        if (!McpExposure.ExposesTools(exposure))
        {
            return new CallToolResult
            {
                IsError = true,
                Content = new List<McpContent> { new McpContent { Type = "text", Text = $"Tool '{parameters.Name}' is not exposed under exposure mode '{exposure}'." } },
                Meta = new JObject { ["errorCode"] = CodeMode.Execution.CodeModeErrorCodes.ExposureMode }
            };
        }

        // Handle custom [McpTool] verbs
        if (_customTools is not null && _customInvoker is not null && _customTools.TryGet(parameters.Name, out var customTool))
        {
            // P3.2: a config-disabled operational toolset's verb is treated as absent (the global gate; the @ops grant
            // is a separate in-verb check). Mirror the list filter so list + invoke agree.
            if (customTool.OperationalToolsetKey is { Length: > 0 } opsKey && !_serverOptions.Value.IsOperationalToolsetEnabled(opsKey))
            {
                return new CallToolResult
                {
                    IsError = true,
                    Content = new List<McpContent> { new McpContent { Type = "text", Text = $"Tool '{parameters.Name}' is not available — the operational toolset '{opsKey}' is disabled (Koan:Mcp:Operations:{opsKey})." } }
                };
            }

            // A route tool answers to its gate in the request, which a rehearsal never makes; a verb with no route, always here (ARCH-0141).
            var rehearsal = IsDryRunRequested(parameters.Arguments);
            if (customTool.Route is null || rehearsal)
            {
                var caller = Koan.Web.Endpoints.EntityRequestContextBuilder.NonHttpPrincipal(user);
                using var gate = McpVerbVisibility.For(_services.GetService<IServiceScopeFactory>(), _services.GetRequiredService<Koan.Web.Authorization.IAccessGateCache>(), caller);
                if (!await gate.Allows(customTool, cancellationToken).ConfigureAwait(false))
                    return Refused(caller.Identity?.IsAuthenticated == true ? "This isn't open to you." : "Sign in to do this.", "gated");
            }

            // AN11 (A10) — a custom verb's effects live in imperative code the framework cannot inspect. A
            // dry-run must NOT invoke it; it returns an honest PARTIAL rehearsal naming the limit (never a
            // silently/falsely-complete — or, worse, actually-executing — "dry-run").
            if (rehearsal)
            {
                return BuildCustomPartialRehearsal(customTool);
            }

            if (customTool.Route is not null)
            {
                return await _services.GetRequiredService<Koan.Mcp.CustomTools.RouteToolInvoker>().Invoke(customTool, parameters.Arguments, user, cancellationToken);
            }

            try
            {
                await using var scope = _services.CreateAsyncScope();
                var token = await _customInvoker.Invoke(customTool, parameters.Arguments, scope.ServiceProvider, user, cancellationToken);
                return BuildCustomResult(token);
            }
            catch (Koan.Data.Core.Commands.Refusal refusal)
            {
                return Refused(refusal.Reason.Said, refusal.Reason.Kind.ToString().ToLowerInvariant());
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

        // Handle traditional entity tools — thread the caller's principal to the data-layer gate (SEC-0004 §3.3).
        var result = await _executor.Execute(parameters.Name, parameters.Arguments, cancellationToken, user);
        return ToCallToolResult(parameters.Name, result);
    }

    private static CallToolResult Refused(string text, string kind) => new()
    {
        IsError = true,
        Content = new List<McpContent> { new McpContent { Type = "text", Text = text } },
        Meta = new JObject { ["errorCode"] = kind }
    };

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

    private static bool IsDryRunRequested(JObject? arguments)
    {
        if (arguments is null) return false;
        if (arguments.TryGetValue(Koan.Mcp.Execution.McpDryRun.ArgumentName, StringComparison.OrdinalIgnoreCase, out var value))
        {
            if (value.Type == JTokenType.Boolean) return value.Value<bool>();
            if (value.Type == JTokenType.String && bool.TryParse(value.Value<string>(), out var parsed)) return parsed;
        }
        return false;
    }

    // AN11 (A10) — the honest partial rehearsal for a custom verb: name the limit, surface any declared
    // effect hints (AN4), and DO NOT execute. Not an error — it is a truthful "did not run".
    private static CallToolResult BuildCustomPartialRehearsal(Koan.Mcp.CustomTools.McpCustomTool tool)
    {
        var declared = new JObject();
        if (tool.ReadOnly is bool readOnly) declared["readOnly"] = readOnly;
        if (tool.Destructive is bool destructive) declared["destructive"] = destructive;
        if (tool.Idempotent is bool idempotent) declared["idempotent"] = idempotent;

        var diagnostics = new JObject
        {
            ["dryRun"] = true,
            ["rehearsable"] = false,
            ["reason"] = "custom-verb-uninspectable",
            ["tool"] = tool.Name
        };
        if (declared.Count > 0) diagnostics["declaredEffects"] = declared;

        return new CallToolResult
        {
            Content = new List<McpContent>
            {
                new McpContent
                {
                    Type = "text",
                    Text = $"Not executed. Dry-run rehearsal is not available for custom verb '{tool.Name}': its effects are not framework-inspectable (AN11/A10). Call without dry_run to execute it for real."
                }
            },
            Meta = new JObject { ["diagnostics"] = diagnostics }
        };
    }

    [JsonRpcMethod("ping")]
    public Task<string> Ping() => Task.FromResult("pong");

    // P1.2: MCP resources. SEC-0017: the JSON-RPC entry points (STDIO) project for the STDIO principal, the one their
    // calls run as. The remote HTTP/SSE edge calls the *For overloads with its own request principal.
    [JsonRpcMethod("resources/list")]
    public Task<ResourcesListResponse> ListResources(CancellationToken cancellationToken)
        => Task.FromResult(ListResourcesFor(StdioPrincipal()));

    [JsonRpcMethod("resources/read", UseSingleObjectParameterDeserialization = true)]
    public Task<ReadResourceResult> ReadResource(ReadResourceParams parameters, CancellationToken cancellationToken)
        => Task.FromResult(ReadResourceFor(parameters?.Uri, StdioPrincipal()));

    public ResourcesListResponse ListResourcesFor(System.Security.Claims.ClaimsPrincipal? user)
    {
        var resources = _resourceProviders
            .SelectMany(provider => provider.List(user))
            .Select(r => new ResourceDescriptor
            {
                Uri = r.Uri,
                Name = r.Name,
                Description = r.Description,
                MimeType = r.MimeType
            })
            .ToArray();

        return new ResourcesListResponse { Resources = resources };
    }

    public ReadResourceResult ReadResourceFor(string? uri, System.Security.Claims.ClaimsPrincipal? user)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            throw new StreamJsonRpc.LocalRpcException("Missing required 'uri' parameter.") { ErrorCode = -32602 };
        }

        foreach (var provider in _resourceProviders)
        {
            var contents = provider.Read(uri, user);
            if (contents is not null)
            {
                return new ReadResourceResult
                {
                    Contents = new[]
                    {
                        new ResourceContents { Uri = contents.Uri, MimeType = contents.MimeType, Text = contents.Text }
                    }
                };
            }
        }

        // Unknown-or-walled uri returns no contents (existence is not revealed to a caller who can't read it).
        return new ReadResourceResult { Contents = Array.Empty<ResourceContents>() };
    }

    // SEC-0014: the shared exposure decision (Auto is treated as Full until handshake capability detection exists).
    private McpExposureMode ResolveExposureMode() => McpExposure.Resolve(_serverOptions.Value);

    // CodeModeOptions.Enabled is the functional kill switch for code mode (AI-0014). It defaults to
    // true, and a misconfigured/partial host without the options registered fails closed. This is the
    // discovery-side gate (tools/list); the invoke-side gate is TryDenyCodeExecution, which also
    // enforces the resolved exposure mode — keep the two in sync when adding a new condition.
    private bool IsCodeModeEnabled()
        => _codeExecutor != null
           && (_services.GetService<IOptions<CodeModeOptions>>()?.Value.Enabled ?? false);

    // Defense-in-depth gate for the RPC invoke-by-name path. Hiding koan_code_execute /
    // koan_code_validate from tools/list is not a security control on its own — a client can still
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
        if (!McpExposure.ExposesCode(exposureMode))
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
            Name = "koan_code_execute",
            Description = "Execute JavaScript code against Koan entity operations. Use SDK.Entities.* for entity operations and SDK.Out.answer() to return results.",
            InputSchema = inputSchema,
            // Code execution can mutate entities (SDK.Entities.*) — not read-only; effects depend on the script.
            Annotations = ToolDescriptor.BuildAnnotations("Execute code", readOnly: false),
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
            Name = "koan_code_validate",
            Description = "Validate JavaScript code for syntax errors before execution.",
            InputSchema = inputSchema,
            // Validation only parses — it never executes code or touches state.
            Annotations = ToolDescriptor.BuildAnnotations("Validate code", readOnly: true),
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

    private async Task<ToolsCallResult> ExecuteCode(JObject? arguments, ClaimsPrincipal? user, CancellationToken cancellationToken)
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

            // Build execution request from arguments — carry the caller's principal so the sandbox SDK's entity
            // operations run AS the caller (SEC-0004 §3.3), not anonymously.
            var request = BuildExecutionRequest(arguments!, code, user);

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

    private static CodeExecutionRequest BuildExecutionRequest(JObject arguments, string code, ClaimsPrincipal? user)
    {
        string? TryGetString(string name)
            => arguments.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var node) ? node?.Value<string>() : null;

        return new CodeExecutionRequest(
            Source: code,
            Language: TryGetString("language"),
            EntryFunction: TryGetString("entryFunction"),
            UserId: null,
            Set: TryGetString("set"),
            CorrelationId: TryGetString("correlationId"),
            Principal: user);
    }

    public sealed class ToolsCallParams
    {
        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("arguments")]
    public JObject? Arguments { get; init; }
    }

    // P1.2: MCP resources wire shapes (dual STJ + Newtonsoft attributes, matching ToolDescriptor).
    public sealed class ReadResourceParams
    {
        [JsonPropertyName("uri")]
        [Newtonsoft.Json.JsonProperty("uri")]
        public string? Uri { get; init; }
    }

    public sealed class InitializeParams
    {
        [JsonPropertyName("protocolVersion")]
        [Newtonsoft.Json.JsonProperty("protocolVersion")]
        public string? ProtocolVersion { get; init; }
    }

    public sealed class InitializeResult
    {
        [JsonPropertyName("protocolVersion")]
        [Newtonsoft.Json.JsonProperty("protocolVersion")]
        public required string ProtocolVersion { get; init; }

        [JsonPropertyName("capabilities")]
        [Newtonsoft.Json.JsonProperty("capabilities")]
        public required JObject Capabilities { get; init; }

        [JsonPropertyName("serverInfo")]
        [Newtonsoft.Json.JsonProperty("serverInfo")]
        public required ServerInfo ServerInfo { get; init; }

        [JsonPropertyName("instructions")]
        [System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [Newtonsoft.Json.JsonProperty("instructions", NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public string? Instructions { get; init; }
    }

    public sealed class ServerInfo
    {
        [JsonPropertyName("name")]
        [Newtonsoft.Json.JsonProperty("name")]
        public required string Name { get; init; }

        [JsonPropertyName("version")]
        [Newtonsoft.Json.JsonProperty("version")]
        public required string Version { get; init; }
    }

    public sealed class ResourcesListResponse
    {
        [JsonPropertyName("resources")]
        [Newtonsoft.Json.JsonProperty("resources")]
        public IReadOnlyList<ResourceDescriptor> Resources { get; init; } = [];
    }

    public sealed class ResourceDescriptor
    {
        [JsonPropertyName("uri")]
        [Newtonsoft.Json.JsonProperty("uri")]
        public required string Uri { get; init; }

        [JsonPropertyName("name")]
        [Newtonsoft.Json.JsonProperty("name")]
        public required string Name { get; init; }

        [JsonPropertyName("description")]
        [Newtonsoft.Json.JsonProperty("description", NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public string? Description { get; init; }

        [JsonPropertyName("mimeType")]
        [Newtonsoft.Json.JsonProperty("mimeType")]
        public required string MimeType { get; init; }
    }

    public sealed class ReadResourceResult
    {
        [JsonPropertyName("contents")]
        [Newtonsoft.Json.JsonProperty("contents")]
        public IReadOnlyList<ResourceContents> Contents { get; init; } = [];
    }

    public sealed class ResourceContents
    {
        [JsonPropertyName("uri")]
        [Newtonsoft.Json.JsonProperty("uri")]
        public required string Uri { get; init; }

        [JsonPropertyName("mimeType")]
        [Newtonsoft.Json.JsonProperty("mimeType")]
        public required string MimeType { get; init; }

        [JsonPropertyName("text")]
        [Newtonsoft.Json.JsonProperty("text")]
        public required string Text { get; init; }
    }

    public sealed class ToolsListResponse
    {
        [JsonPropertyName("tools")]
        [Newtonsoft.Json.JsonProperty("tools")]
        public IReadOnlyList<ToolDescriptor> Tools { get; init; } = [];
    }

    public sealed class ToolDescriptor
    {
        [JsonPropertyName("name")]
        [Newtonsoft.Json.JsonProperty("name")]
        public required string Name { get; init; }

        [JsonPropertyName("description")]
        [Newtonsoft.Json.JsonProperty("description")]
        public string? Description { get; init; }

        // AN1: the MCP spec names the schema `inputSchema` (camelCase), not `input_schema`. A non-spec
        // name is silently dropped by spec-compliant clients.
        [JsonPropertyName("inputSchema")]
        [Newtonsoft.Json.JsonProperty("inputSchema")]
        public required JObject InputSchema { get; init; }

        // AN1: outputSchema slot for AN-structured to fill later. Omitted entirely while empty — never
        // emitted as null or under a non-spec name.
        [JsonPropertyName("outputSchema")]
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        [Newtonsoft.Json.JsonProperty("outputSchema", NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public JObject? OutputSchema { get; init; }

        // AN1: the MCP spec carries hints (readOnly/destructive/idempotent + title) in a dedicated
        // `annotations` object (ToolAnnotations). AN4 fills the verb-derived hints; AN1 establishes the
        // correctly-named container so spec clients stop ignoring them.
        [JsonPropertyName("annotations")]
        [Newtonsoft.Json.JsonProperty("annotations")]
        public required JObject Annotations { get; init; }

        // Koan-internal richer metadata, kept alongside the spec fields. Spec clients ignore unknown
        // members; Koan-aware tooling can still read entity/operation/scope provenance from here.
        [JsonPropertyName("metadata")]
        [Newtonsoft.Json.JsonProperty("metadata")]
        public required JObject Metadata { get; init; }

        // AN1/AN4: the spec ToolAnnotations container — a human title plus the verb-derived hint booleans.
        // readOnlyHint is emitted whenever known; destructiveHint/idempotentHint are only meaningful when
        // NOT read-only (MCP spec), so they ride alongside a false readOnlyHint.
        internal static JObject BuildAnnotations(string? title, bool? readOnly = null, bool? destructive = null, bool? idempotent = null)
        {
            var annotations = new JObject();
            if (!string.IsNullOrWhiteSpace(title)) annotations["title"] = title;
            if (readOnly is bool ro)
            {
                annotations["readOnlyHint"] = ro;
                if (!ro)
                {
                    if (destructive is bool d) annotations["destructiveHint"] = d;
                    if (idempotent is bool i) annotations["idempotentHint"] = i;
                }
            }
            return annotations;
        }

        // AN4: mechanical hint derivation from the entity verb (the 12-op kind is the single source of truth).
        internal static (bool ReadOnly, bool Destructive, bool Idempotent) HintsForOperation(EntityEndpointOperationKind kind) => kind switch
        {
            EntityEndpointOperationKind.Collection or EntityEndpointOperationKind.Query
                or EntityEndpointOperationKind.GetNew or EntityEndpointOperationKind.GetById => (true, false, false),
            EntityEndpointOperationKind.Upsert or EntityEndpointOperationKind.UpsertMany => (false, false, true),
            EntityEndpointOperationKind.Delete or EntityEndpointOperationKind.DeleteMany
                or EntityEndpointOperationKind.DeleteByQuery or EntityEndpointOperationKind.DeleteAll => (false, true, false),
            _ => (false, false, false) // Patch / None: a mutation with no asserted destructive/idempotent guarantee
        };

        public static ToolDescriptor From(McpEntityRegistration registration, McpToolDefinition tool)
        {
            // SEC-0004 Phase 3.3b: tools advertise no flat scope list — their access authority is the data-layer
            // [Access] gate (enforced on call, projected per-row via Slice C's can:[]), not a transport-edge filter.
            var metadata = new JObject
            {
                ["entity"] = registration.DisplayName,
                ["operation"] = tool.Operation.ToString(),
                ["returnsCollection"] = tool.ReturnsCollection,
                ["isMutation"] = tool.IsMutation
            };

            var (readOnly, destructive, idempotent) = HintsForOperation(tool.Operation);

            return new ToolDescriptor
            {
                Name = tool.Name,
                Description = tool.Description,
                InputSchema = tool.InputSchema,
                Annotations = BuildAnnotations(tool.Description ?? tool.Name, readOnly, destructive, idempotent),
                Metadata = metadata
            };
        }

        public static ToolDescriptor FromCustom(Koan.Mcp.CustomTools.McpCustomTool tool)
        {
            var metadata = new JObject
            {
                ["custom"] = true,
                ["isMutation"] = tool.IsMutation
            };

            // AN4: a verb with no route gains NOTHING automatically — only the explicit [McpReadOnly]/[McpDestructive]/
            // [McpIdempotent] markers are emitted (the dangerous hand-written verbs are the ones to mark). A route also
            // says what its HTTP method promises.
            return new ToolDescriptor
            {
                Name = tool.Name,
                Description = tool.Description,
                InputSchema = tool.InputSchema,
                Annotations = BuildAnnotations(tool.Description ?? tool.Name, tool.ReadOnly, tool.Destructive, tool.Idempotent),
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
                ShortCircuit = execution.ShortCircuit,
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
                    ["shortCircuit"] = result.ShortCircuit,
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
                    ["shortCircuit"] = execution.ShortCircuit,
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

        [JsonPropertyName("_meta")]
        [Newtonsoft.Json.JsonProperty("_meta")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JObject? Meta { get; set; }

        // What a route tool answered, as data, where a client that reads the specification's field will find it.
        [JsonPropertyName("structuredContent")]
        [Newtonsoft.Json.JsonProperty("structuredContent", NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JToken? StructuredContent { get; set; }
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
