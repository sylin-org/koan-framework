using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
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

    private static readonly string[] ScopeClaimTypes =
    {
        "scope",
        "scp",
        "http://schemas.microsoft.com/identity/claims/scope"
    };

    private readonly McpServer _server;
    private readonly McpEntityRegistry _registry;
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
        IOptionsMonitor<McpServerOptions> options,
        HttpSseSession session,
        ILogger<HttpSseRpcBridge> logger)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
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
        _processingTask = Task.Run(ProcessAsync);
    }

    public ValueTask SubmitAsync(JsonRpcEnvelope request, CancellationToken cancellationToken)
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

    private async Task ProcessAsync()
    {
        try
        {
            await foreach (var envelope in _requests.Reader.ReadAllAsync(_cts.Token))
            {
                await DispatchAsync(envelope, _cts.Token);
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

    private async Task DispatchAsync(JsonRpcEnvelope envelope, CancellationToken cancellationToken)
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
                    await HandleToolsListAsync(envelope, cancellationToken);
                    break;
                case "tools/call":
                    await HandleToolsCallAsync(envelope, cancellationToken);
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

    private async Task HandleToolsListAsync(JsonRpcEnvelope envelope, CancellationToken cancellationToken)
    {
        var response = await _handler.ListToolsAsync(cancellationToken);
        var filtered = response.Tools
            .Where(tool => _registry.TryGetTool(tool.Name, out var registration, out var definition) && HasAccess(registration, definition))
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

    private async Task HandleToolsCallAsync(JsonRpcEnvelope envelope, CancellationToken cancellationToken)
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

        if (!_registry.TryGetTool(toolName, out var registration, out var tool))
        {
            _session.Enqueue(ServerSentEvent.FromJsonRpc(CreateError(envelope.Id, -32601, $"Tool '{toolName}' is not registered.")));
            return;
        }

        if (!HasAccess(registration, tool))
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

        var result = await _handler.CallToolAsync(callParams, cancellationToken);
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

    private bool HasAccess(McpEntityRegistration registration, McpToolDefinition tool)
    {
        var options = _options.CurrentValue;
        var requiresAuth = registration.RequireAuthentication ?? options.RequireAuthentication;
        var user = _session.User;

        if (requiresAuth)
        {
            if (user?.Identity?.IsAuthenticated != true)
            {
                return false;
            }
        }

        if (tool.RequiredScopes.Count > 0)
        {
            if (!UserHasScopes(tool.RequiredScopes))
            {
                return false;
            }
        }

        return true;
    }

    private bool UserHasScopes(IReadOnlyList<string> requiredScopes)
    {
        if (requiredScopes.Count == 0)
        {
            return true;
        }

        var user = _session.User;
        if (user is null)
        {
            return false;
        }

        var scopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var claimType in ScopeClaimTypes)
        {
            foreach (var claim in user.FindAll(claimType))
            {
                if (string.IsNullOrWhiteSpace(claim.Value))
                {
                    continue;
                }

                var values = claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var value in values)
                {
                    scopes.Add(value);
                }
            }
        }

        if (scopes.Count == 0)
        {
            return false;
        }

        return requiredScopes.All(scope => scopes.Contains(scope));
    }

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
