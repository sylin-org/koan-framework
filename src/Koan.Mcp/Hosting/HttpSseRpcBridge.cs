using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Koan.Mcp.Execution;
using Koan.Mcp.Options;
using Koan.Mcp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Mcp.Hosting;

public sealed class HttpSseRpcBridge : IAsyncDisposable
{
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore
    };

    private readonly McpServer _server;
    private readonly McpEntityRegistry _registry;
    private readonly Koan.Mcp.CustomTools.McpCustomToolRegistry _customTools;
    private readonly IOptionsMonitor<McpServerOptions> _options;
    private readonly Koan.Web.Authorization.IAccessGateCache _gateCache;
    private readonly HttpSseSession _session;
    private readonly ILogger<HttpSseRpcBridge> _logger;
    private readonly Channel<JsonRpcEnvelope> _requests;
    private readonly CancellationTokenSource _cts;
    private readonly Task _processingTask;
    private readonly McpRpcHandler _handler;

    public HttpSseRpcBridge(
        McpServer server,
        McpEntityRegistry registry,
        Koan.Mcp.CustomTools.McpCustomToolRegistry customTools,
        IOptionsMonitor<McpServerOptions> options,
        Koan.Web.Authorization.IAccessGateCache gateCache,
        HttpSseSession session,
        ILogger<HttpSseRpcBridge> logger)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _customTools = customTools ?? throw new ArgumentNullException(nameof(customTools));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _gateCache = gateCache ?? throw new ArgumentNullException(nameof(gateCache));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _requests = Channel.CreateUnbounded<JsonRpcEnvelope>(new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false
        });
        _cts = CancellationTokenSource.CreateLinkedTokenSource(session.Cancellation.Token);
        _handler = _server.CreateHandler();
        _processingTask = Task.Run(Process);
    }

    public ValueTask Submit(JsonRpcEnvelope request, CancellationToken cancellationToken)
    {
        if (_cts.IsCancellationRequested)
        {
            return ValueTask.CompletedTask;
        }

        if (!_requests.Writer.TryWrite(request))
        {
            return new ValueTask(_requests.Writer.WriteAsync(request, cancellationToken).AsTask());
        }

        return ValueTask.CompletedTask;
    }

    private async Task Process()
    {
        try
        {
            await foreach (var envelope in _requests.Reader.ReadAllAsync(_cts.Token))
            {
                await Dispatch(envelope, _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception while processing HTTP+SSE JSON-RPC requests.");
        }
        finally
        {
            _requests.Writer.TryComplete();
        }
    }

    private async Task Dispatch(JsonRpcEnvelope envelope, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(envelope.Method))
        {
            _session.Enqueue(ServerSentEvent.FromJsonRpc(CreateError(envelope.Id, -32600, "Missing method.")));
            return;
        }

        try
        {
            switch (envelope.Method)
            {
                case "initialize":
                    await HandleInitialize(envelope, cancellationToken);
                    break;
                case "tools/list":
                    await HandleToolsList(envelope, cancellationToken);
                    break;
                case "tools/call":
                    await HandleToolsCall(envelope, cancellationToken);
                    break;
                case "resources/list":
                    HandleResourcesList(envelope);
                    break;
                case "resources/read":
                    HandleResourcesRead(envelope);
                    break;
                case "ping":
                    var pong = new JObject { ["jsonrpc"] = "2.0", ["id"] = CloneId(envelope.Id), ["result"] = "pong" };
                    _session.Enqueue(ServerSentEvent.FromJsonRpc(pong));
                    break;
                default:
                    _session.Enqueue(ServerSentEvent.FromJsonRpc(CreateError(envelope.Id, -32601, $"Method '{envelope.Method}' is not supported.")));
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            _session.Enqueue(ServerSentEvent.FromJsonRpc(CreateError(envelope.Id, -32603, "Operation cancelled.")));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling JSON-RPC request {Method}.", envelope.Method);
            _session.Enqueue(ServerSentEvent.FromJsonRpc(CreateError(envelope.Id, -32603, "Internal server error.")));
        }
    }

    private async Task HandleInitialize(JsonRpcEnvelope envelope, CancellationToken cancellationToken)
    {
        var parameters = envelope.Params is JObject obj
            ? obj.ToObject<McpRpcHandler.InitializeParams>(JsonSerializer.Create(SerializerSettings))
            : null;
        var response = await _handler.Initialize(parameters, cancellationToken);

        var node = JToken.FromObject(response, JsonSerializer.Create(SerializerSettings)) as JObject;
        if (node is null)
        {
            _session.Enqueue(ServerSentEvent.FromJsonRpc(CreateError(envelope.Id, -32603, "Failed to serialise response.")));
            return;
        }

        _session.Enqueue(ServerSentEvent.FromJsonRpc(new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = CloneId(envelope.Id),
            ["result"] = node
        }));
    }

    private async Task HandleToolsList(JsonRpcEnvelope envelope, CancellationToken cancellationToken)
    {
        var response = await _handler.ListTools(cancellationToken);
        var filtered = response.Tools
            .Where(IsToolVisible)
            .ToArray();

        if (filtered.Length != response.Tools.Count)
        {
            response = new McpRpcHandler.ToolsListResponse
            {
                Tools = filtered,
                Next = response.Next
            };
        }

        var node = JToken.FromObject(response, JsonSerializer.Create(SerializerSettings)) as JObject;
        if (node is null)
        {
            _session.Enqueue(ServerSentEvent.FromJsonRpc(CreateError(envelope.Id, -32603, "Failed to serialise response.")));
            return;
        }

        var result = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = CloneId(envelope.Id),
            ["result"] = node
        };
        _session.Enqueue(ServerSentEvent.FromJsonRpc(result));
    }

    private async Task HandleToolsCall(JsonRpcEnvelope envelope, CancellationToken cancellationToken)
    {
        if (envelope.Params is not JObject parameters)
        {
            _session.Enqueue(ServerSentEvent.FromJsonRpc(CreateError(envelope.Id, -32602, "Expected params object.")));
            return;
        }

        if (!parameters.TryGetValue("name", StringComparison.OrdinalIgnoreCase, out var nameNode) || nameNode?.Value<string>() is not { Length: > 0 } toolName)
        {
            _session.Enqueue(ServerSentEvent.FromJsonRpc(CreateError(envelope.Id, -32602, "Missing tool name.")));
            return;
        }

        var isEntityTool = _registry.TryGetTool(toolName, out var registration, out var tool);
        Koan.Mcp.CustomTools.McpCustomTool? customTool = null;
        var isCustomTool = !isEntityTool && _customTools.TryGet(toolName, out customTool);

        if (!isEntityTool && !isCustomTool)
        {
            _session.Enqueue(ServerSentEvent.FromJsonRpc(CreateError(envelope.Id, -32601, $"Tool '{toolName}' is not registered.")));
            return;
        }

        // SEC-0004 Phase 3.3b: an ENTITY tool's authority is the data-layer [Access] gate, enforced inside
        // CallToolFor with the threaded session principal — a denial rides back as meta.shortCircuit (the MCP
        // mirror of REST 403/401), NOT a transport-edge -32604. So the edge no longer pre-checks entity tools;
        // it still gates a CUSTOM verb here (custom verbs have no entity/no row, so no data-layer gate yet).
        if (isCustomTool && !HasAccessCustom(customTool!))
        {
            _session.Enqueue(ServerSentEvent.FromJsonRpc(CreateError(envelope.Id, -32604, "Forbidden.")));
            return;
        }

        JObject? arguments = null;
        if (parameters.TryGetValue("arguments", StringComparison.OrdinalIgnoreCase, out var argsNode) && argsNode is JObject obj)
        {
            arguments = obj;
        }

        var callParams = new McpRpcHandler.ToolsCallParams
        {
            Name = toolName,
            Arguments = arguments
        };

        // SEC-0004 Phase 3.3: the remote edge calls AS the authenticated session principal (anonymous → a concrete
        // empty principal, never null), so the data-layer gate / constrain / projection reflect this caller.
        var result = await _handler.CallToolFor(callParams, _session.User ?? new System.Security.Claims.ClaimsPrincipal(), cancellationToken);
        var node = JToken.FromObject(result, JsonSerializer.Create(SerializerSettings));
        if (node is null)
        {
            _session.Enqueue(ServerSentEvent.FromJsonRpc(CreateError(envelope.Id, -32603, "Failed to serialise response.")));
            return;
        }

        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = CloneId(envelope.Id),
            ["result"] = node
        };

        _session.Enqueue(ServerSentEvent.FromJsonRpc(response));
    }

    // P1.2: resources are PROJECTED PER GRANT inside the provider — the remote edge passes the session
    // principal so List/Read reflect only what this caller may see (no separate visibility filter needed).
    private void HandleResourcesList(JsonRpcEnvelope envelope)
    {
        // Remote edge: NEVER pass null (null = local-trust). An anonymous caller is a concrete empty
        // principal so the per-grant projection restricts privileged resources rather than opening them.
        var response = _handler.ListResourcesFor(_session.User ?? new System.Security.Claims.ClaimsPrincipal());
        EnqueueResult(envelope, JToken.FromObject(response, JsonSerializer.Create(SerializerSettings)));
    }

    private void HandleResourcesRead(JsonRpcEnvelope envelope)
    {
        if (envelope.Params is not JObject parameters
            || !parameters.TryGetValue("uri", StringComparison.OrdinalIgnoreCase, out var uriNode)
            || uriNode?.Value<string>() is not { Length: > 0 } uri)
        {
            _session.Enqueue(ServerSentEvent.FromJsonRpc(CreateError(envelope.Id, -32602, "Missing 'uri' parameter.")));
            return;
        }

        var result = _handler.ReadResourceFor(uri, _session.User ?? new System.Security.Claims.ClaimsPrincipal());
        EnqueueResult(envelope, JToken.FromObject(result, JsonSerializer.Create(SerializerSettings)));
    }

    private void EnqueueResult(JsonRpcEnvelope envelope, JToken? node)
    {
        if (node is null)
        {
            _session.Enqueue(ServerSentEvent.FromJsonRpc(CreateError(envelope.Id, -32603, "Failed to serialise response.")));
            return;
        }

        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = CloneId(envelope.Id),
            ["result"] = node
        };
        _session.Enqueue(ServerSentEvent.FromJsonRpc(response));
    }

    // SEC-0004 Phase 3.3b: tools/list must not advertise what the gate will deny. An ENTITY tool is visible iff
    // the SAME [Access] gate the data layer enforces coarsely allows the session principal (McpEntityGate). A
    // CUSTOM verb still consults McpToolAccessPolicy (its scope filter). The remote edge NEVER passes null
    // (null = STDIO local-trust): an anonymous remote caller is a concrete empty principal, so it is gated.
    private bool IsToolVisible(McpRpcHandler.ToolDescriptor tool)
    {
        if (_registry.TryGetTool(tool.Name, out var registration, out var definition))
        {
            return McpEntityGate.CoarseAllows(
                _gateCache, registration.EntityType, definition.Operation,
                _session.User ?? new System.Security.Claims.ClaimsPrincipal());
        }

        if (_customTools.TryGet(tool.Name, out var custom))
        {
            return HasAccessCustom(custom);
        }

        return false;
    }

    // A custom [McpTool] verb's edge authority: the shared McpToolAccessPolicy consulted with the session
    // principal for both tools/list (filter) and tools/call (deny). Entity tools use the gate (see IsToolVisible).
    private bool HasAccessCustom(Koan.Mcp.CustomTools.McpCustomTool tool)
        => McpToolAccessPolicy.IsCustomToolPermitted(_session.User, tool, _options.CurrentValue);

    private static JObject CreateError(JToken? id, int code, string message, JToken? data = null)
    {
        var error = new JObject
        {
            ["code"] = code,
            ["message"] = message
        };

        if (data is not null)
        {
            error["data"] = data;
        }

        return new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = CloneId(id),
            ["error"] = error
        };
    }

    private static JToken? CloneId(JToken? id)
        => id?.DeepClone();

    public async ValueTask DisposeAsync()
    {
        try
        {
            _cts.Cancel();
        }
        catch
        {
        }

        try
        {
            await _processingTask;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cts.Dispose();
        }
    }
}

public sealed record JsonRpcEnvelope(string Jsonrpc, string Method, JToken? Params, JToken? Id);
