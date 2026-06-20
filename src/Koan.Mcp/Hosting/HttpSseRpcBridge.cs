using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Hosting;

/// <summary>
/// Legacy HTTP+SSE per-session pump (AI-0013). Reads submitted JSON-RPC envelopes off a channel, dispatches each
/// through the shared <see cref="McpRpcDispatcher"/> (AI-0037 — one dispatch core for every transport), and
/// enqueues the response onto the session's SSE outbound stream. The dispatch + SEC-0004 gating logic no longer
/// lives here; this is purely the legacy transport's submit→pump→enqueue plumbing.
/// </summary>
public sealed class HttpSseRpcBridge : IAsyncDisposable
{
    private readonly McpRpcDispatcher _dispatcher;
    private readonly HttpSseSession _session;
    private readonly ILogger<HttpSseRpcBridge> _logger;
    private readonly Channel<JsonRpcEnvelope> _requests;
    private readonly CancellationTokenSource _cts;
    private readonly Task _processingTask;
    private readonly McpRpcHandler _handler;

    public HttpSseRpcBridge(
        McpServer server,
        McpRpcDispatcher dispatcher,
        HttpSseSession session,
        ILogger<HttpSseRpcBridge> logger)
    {
        if (server is null) throw new ArgumentNullException(nameof(server));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _requests = Channel.CreateUnbounded<JsonRpcEnvelope>(new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false
        });
        _cts = CancellationTokenSource.CreateLinkedTokenSource(session.Cancellation.Token);
        _handler = server.CreateHandler();
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
        // The remote edge dispatches AS the authenticated session principal (anonymous → a concrete empty
        // principal, never null = STDIO local-trust). The response (if any) rides the session's SSE channel.
        var response = await _dispatcher.DispatchAsync(envelope, _session.User ?? new ClaimsPrincipal(), _handler, cancellationToken);
        if (response is not null)
        {
            _session.Enqueue(ServerSentEvent.FromJsonRpc(response));
        }
    }

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

public sealed record JsonRpcEnvelope(string Jsonrpc, string Method, JToken? Params, JToken? Id)
{
    /// <summary>
    /// Parse a JSON-RPC request object into an envelope. Shared by every transport (AI-0037 — one parse path) so a
    /// malformed body is rejected identically on the legacy SSE <c>/rpc</c> and the Streamable HTTP endpoint.
    /// </summary>
    public static bool TryParse(JToken node, out JsonRpcEnvelope envelope, out string? error)
    {
        envelope = default!;
        error = null;

        if (node is not JObject obj)
        {
            error = "Payload must be a JSON object.";
            return false;
        }

        var methodNode = obj["method"];
        if (methodNode?.Type != JTokenType.String || methodNode.Value<string>() is not { Length: > 0 } method)
        {
            error = "Missing method.";
            return false;
        }

        var jsonRpc = obj["jsonrpc"]?.Value<string>() ?? "2.0";
        var parameters = obj.TryGetValue("params", out var paramsNode) ? paramsNode : null;
        var id = obj.TryGetValue("id", out var idNode) ? idNode : null;

        envelope = new JsonRpcEnvelope(jsonRpc, method, parameters, id);
        return true;
    }
}
