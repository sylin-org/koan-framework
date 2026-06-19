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
        HttpSseSession session,
        ILogger<HttpSseRpcBridge> logger)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _customTools = customTools ?? throw new ArgumentNullException(nameof(customTools));
        _options = options ?? throw new ArgumentNullException(nameof(options));
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
                case "tools/list":
                    await HandleToolsList(envelope, cancellationToken);
                    break;
                case "tools/call":
                    await HandleToolsCall(envelope, cancellationToken);
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

        var permitted = isEntityTool ? HasAccess(registration, tool) : HasAccessCustom(customTool!);
        if (!permitted)
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

        var result = await _handler.CallTool(callParams, cancellationToken);
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

    private bool IsToolVisible(McpRpcHandler.ToolDescriptor tool)
    {
        if (_registry.TryGetTool(tool.Name, out var registration, out var definition))
        {
            return HasAccess(registration, definition);
        }

        if (_customTools.TryGet(tool.Name, out var custom))
        {
            return HasAccessCustom(custom);
        }

        return false;
    }

    // AN3: enforcement is the shared McpToolAccessPolicy, not a per-transport copy. The HTTP/SSE edge is
    // the remote transport, so it consults the policy with the authenticated session principal for both
    // tools/list (filter) and tools/call (deny).
    private bool HasAccessCustom(Koan.Mcp.CustomTools.McpCustomTool tool)
        => McpToolAccessPolicy.IsCustomToolPermitted(_session.User, tool, _options.CurrentValue);

    private bool HasAccess(McpEntityRegistration registration, McpToolDefinition tool)
        => McpToolAccessPolicy.IsEntityToolPermitted(_session.User, registration, tool, _options.CurrentValue);

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
