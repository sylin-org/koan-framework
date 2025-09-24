using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core;
using Koan.Mcp.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Mcp.Hosting;

public sealed class HttpSseTransport
{
    private readonly HttpSseSessionManager _sessions;
    private readonly McpServer _server;
    private readonly McpEntityRegistry _registry;
    private readonly IOptionsMonitor<McpServerOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<HttpSseTransport> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public HttpSseTransport(
        HttpSseSessionManager sessions,
        McpServer server,
        McpEntityRegistry registry,
        IOptionsMonitor<McpServerOptions> options,
        TimeProvider timeProvider,
        ILogger<HttpSseTransport> logger,
        ILoggerFactory loggerFactory)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    public async Task AcceptStreamAsync(HttpContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));

        var options = _options.CurrentValue;
        if (!options.EnableHttpSseTransport)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (!options.RequireAuthentication && (KoanEnv.IsProduction || KoanEnv.InContainer))
        {
            _logger.LogWarning(
                "SECURITY WARNING: MCP HTTP+SSE transport running without authentication in {Environment}.",
                KoanEnv.EnvironmentName);
        }

        if ((KoanEnv.IsProduction || KoanEnv.InContainer) && !context.Request.IsHttps)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "https_required",
                message = "HTTP+SSE transport requires HTTPS in production environments."
            }, cancellationToken: context.RequestAborted).ConfigureAwait(false);
            return;
        }

        if (options.RequireAuthentication && context.User?.Identity?.IsAuthenticated != true)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "unauthorized" }, cancellationToken: context.RequestAborted).ConfigureAwait(false);
            return;
        }

        var registrations = _server.GetRegistrationsForHttpSse();
        if (registrations.Count == 0)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new { error = "no_entities" }, cancellationToken: context.RequestAborted).ConfigureAwait(false);
            return;
        }

        if (!_sessions.TryOpenSession(context, out var session))
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsJsonAsync(new { error = "max_connections_exceeded" }, cancellationToken: context.RequestAborted).ConfigureAwait(false);
            return;
        }

        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.Connection = "keep-alive";
        context.Response.Headers["X-Accel-Buffering"] = "no";
        context.Response.Headers[HttpSseHeaders.SessionId] = session.Id;
        context.Response.ContentType = "text/event-stream";

        var connectedAt = _timeProvider.GetUtcNow();
        session.Enqueue(ServerSentEvent.Connected(session.Id, connectedAt));

        await using var bridge = new HttpSseRpcBridge(
            _server,
            _registry,
            _options,
            session,
            _loggerFactory.CreateLogger<HttpSseRpcBridge>());
        session.AttachBridge(bridge);

        try
        {
            await foreach (var message in session.OutboundMessages(context.RequestAborted).ConfigureAwait(false))
            {
                var formatted = message.ToWireFormat();
                await context.Response.WriteAsync(formatted, context.RequestAborted).ConfigureAwait(false);
                await context.Response.Body.FlushAsync(context.RequestAborted).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            session.Enqueue(ServerSentEvent.Completed(_timeProvider.GetUtcNow()));
            _sessions.CloseSession(session);
        }
    }

    public async Task<IResult> SubmitRequestAsync(HttpContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));

        var options = _options.CurrentValue;
        if (!options.EnableHttpSseTransport)
        {
            return Results.NotFound(new { error = "transport_disabled" });
        }

        var sessionId = ResolveSessionId(context.Request);
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return Results.BadRequest(new { error = "missing_session" });
        }

        if (!_sessions.TryGet(sessionId, out var session))
        {
            return Results.NotFound(new { error = "session_not_found" });
        }

        JsonNode? payload;
        try
        {
            payload = await JsonNode.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON payload submitted to MCP HTTP+SSE RPC endpoint.");
            return Results.BadRequest(new { error = "invalid_json" });
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "I/O failure while reading MCP HTTP+SSE RPC payload.");
            return Results.BadRequest(new { error = "invalid_json" });
        }

        if (payload is null)
        {
            return Results.BadRequest(new { error = "invalid_json" });
        }

        if (!TryCreateEnvelope(payload, out var envelope, out var message))
        {
            return Results.BadRequest(new { error = "invalid_jsonrpc", message });
        }

        if (!session.TryGetBridge(out var bridge))
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        session.Enqueue(ServerSentEvent.Acknowledged(envelope.Id));
        await bridge.SubmitAsync(envelope, context.RequestAborted).ConfigureAwait(false);
        return Results.Accepted();
    }

    private static string? ResolveSessionId(HttpRequest request)
    {
        if (request.Headers.TryGetValue(HttpSseHeaders.SessionId, out var header) && !string.IsNullOrWhiteSpace(header))
        {
            return header.ToString();
        }

        if (request.Query.TryGetValue("sessionId", out var query) && !string.IsNullOrWhiteSpace(query))
        {
            return query.ToString();
        }

        return null;
    }

    private static bool TryCreateEnvelope(JsonNode node, out JsonRpcEnvelope envelope, out string? error)
    {
        envelope = default!;
        error = null;

        if (node is not JsonObject obj)
        {
            error = "Payload must be a JSON object.";
            return false;
        }

        var methodNode = obj["method"];
        if (methodNode?.GetValue<string>() is not { Length: > 0 } method)
        {
            error = "Missing method.";
            return false;
        }

        var jsonRpc = obj["jsonrpc"]?.GetValue<string>() ?? "2.0";
        var parameters = obj.TryGetPropertyValue("params", out var paramsNode) ? paramsNode : null;
        var id = obj.TryGetPropertyValue("id", out var idNode) ? idNode : null;

        envelope = new JsonRpcEnvelope(jsonRpc, method, parameters, id);
        return true;
    }
}
