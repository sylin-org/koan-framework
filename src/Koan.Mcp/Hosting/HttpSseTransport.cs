using System;
using System.IO;
using System.Threading.Tasks;
using Koan.Core;
using Koan.Mcp.Options;
using Koan.Web.Sse;
using Koan.Web.Sse.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Hosting;

/// <summary>
/// AI-0037 (Ph3b) — the DEPRECATED legacy HTTP+SSE transport, now a thin SHIM over the unified MCP core
/// (<see cref="McpSessionManager"/> / <see cref="McpSession"/> / <see cref="McpSseStream"/> /
/// <see cref="McpRpcDispatcher"/>). It reproduces the 2024-11-05 2-endpoint wire — <c>GET /sse</c> opens the
/// stream and emits <c>connected</c>+<c>endpoint</c> frames; <c>POST /rpc</c> acks then dispatches and the response
/// rides the GET stream — with no parallel session model, frame type, or background pump of its own (those were
/// deleted). The exact legacy bytes (the <c>X-Mcp-Session</c> header, the event names, and the ABSENCE of an
/// <c>id:</c> line on each frame) are pinned by the golden <c>LegacyHttpSseWireSpec</c>.
/// </summary>
public sealed class HttpSseTransport
{
    private readonly McpSessionManager _sessions;
    private readonly McpServer _server;
    private readonly McpRpcDispatcher _dispatcher;
    private readonly IOptionsMonitor<McpServerOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<HttpSseTransport> _logger;

    public HttpSseTransport(
        McpSessionManager sessions,
        McpServer server,
        McpRpcDispatcher dispatcher,
        IOptionsMonitor<McpServerOptions> options,
        TimeProvider timeProvider,
        ILogger<HttpSseTransport> logger)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ---- GET {baseRoute}/sse : open the legacy server-push stream ---------------------------------------------------

    public async Task AcceptStream(HttpContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));

        var options = _options.CurrentValue;
        if (!options.EnableLegacySseTransport)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (!options.RequireAuthentication && (KoanEnv.IsProduction || KoanEnv.InContainer))
        {
            _logger.LogWarning(
                "SECURITY WARNING: legacy MCP HTTP+SSE transport running without authentication in {Environment}.",
                KoanEnv.EnvironmentName);
        }

        if (KoanEnv.IsProduction && !KoanEnv.InContainer && !context.Request.IsHttps)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "https_required",
                message = "HTTP+SSE transport requires HTTPS in non-containerized production environments."
            }, cancellationToken: context.RequestAborted);
            return;
        }

        // Defence-in-depth: the route-group filter (McpEdgeAuth) already authenticated when RequireAuthentication.
        if (options.RequireAuthentication && context.User?.Identity?.IsAuthenticated != true)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "unauthorized" }, cancellationToken: context.RequestAborted);
            return;
        }

        if (_server.GetRegistrationsForHttpSse().Count == 0)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new { error = "no_entities" }, cancellationToken: context.RequestAborted);
            return;
        }

        var session = _sessions.Create(context);
        if (session is null)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsJsonAsync(new { error = "max_connections_exceeded" }, cancellationToken: context.RequestAborted);
            return;
        }

        try
        {
            context.Response.Headers[HttpSseHeaders.SessionId] = session.Id;

            var getStream = session.TryOpenGetStream();
            if (getStream is null)
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                return;
            }

            var baseRoute = ResolveBaseRoute(options);
            var endpointUrl = $"{context.Request.PathBase}{baseRoute}/rpc?sessionId={session.Id}";

            // The legacy handshake frames — hand-built so they carry the legacy event names and NO id: line (the
            // unified EnqueueMessage would stamp an id: the 2024-11-05 wire never emitted).
            getStream.EnqueueRaw(new SseEnvelope("connected", new JObject
            {
                ["sessionId"] = session.Id,
                ["timestamp"] = _timeProvider.GetUtcNow().ToString("O"),
            }.ToString(Formatting.None)));
            getStream.EnqueueRaw(new SseEnvelope("endpoint", endpointUrl));

            var result = SseResults.StreamEnvelopes(getStream.Read(context.RequestAborted));
            await result.ExecuteAsync(context);
        }
        catch (OperationCanceledException)
        {
            // client disconnected — normal.
        }
        finally
        {
            // Legacy lifetime: the session dies with its GET stream (the legacy CTS was request-linked). Terminating
            // here also completes the GET stream so a late POST's enqueue is a harmless no-op.
            _sessions.Terminate(session.Id);
        }
    }

    // ---- POST {baseRoute}/rpc : submit a JSON-RPC request; the response rides the GET stream -----------------------

    public async Task<IResult> SubmitRequest(HttpContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));

        var options = _options.CurrentValue;
        if (!options.EnableLegacySseTransport)
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

        // SEC-0006 — the session id is not a bearer capability: an RPC submitter must be the SAME subject that
        // established the session on the GET /sse handshake (the dispatch runs as session.User).
        if (options.RequireAuthentication && !McpEdgeAuth.SamePrincipal(context.User, session.User))
        {
            _logger.LogWarning("Rejected legacy MCP RPC submit: caller principal does not own session {SessionId}.", sessionId);
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (!session.TryGetStream(McpSseStream.GetStreamId, out var getStream))
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); // the GET /sse stream is not open
        }

        JToken? payload;
        try
        {
            using var reader = new StreamReader(context.Request.Body);
            using var jsonReader = new JsonTextReader(reader);
            payload = await JToken.ReadFromAsync(jsonReader, context.RequestAborted);
        }
        catch (Exception ex) when (ex is JsonReaderException or IOException)
        {
            _logger.LogWarning(ex, "Invalid JSON payload submitted to the legacy MCP RPC endpoint.");
            return Results.BadRequest(new { error = "invalid_json" });
        }

        if (payload is null)
        {
            return Results.BadRequest(new { error = "invalid_json" });
        }

        if (!JsonRpcEnvelope.TryParse(payload, out var envelope, out var message))
        {
            return Results.BadRequest(new { error = "invalid_jsonrpc", message });
        }

        session.Touch();

        // ack, then the response — both hand-built (no id: line). AI-0037 §4: the deprecated shim no longer
        // guarantees submission-order responses under concurrent POSTs (inline dispatch, completion order); JSON-RPC
        // ids correlate. The single serial pump that gave incidental ordering was deleted with the parallel session.
        getStream.EnqueueRaw(new SseEnvelope("ack", new JObject
        {
            ["id"] = envelope.Id is null ? JValue.CreateNull() : envelope.Id.DeepClone(),
        }.ToString(Formatting.None)));

        var handler = _server.CreateHandler();
        var response = await _dispatcher.DispatchAsync(envelope, session.User, handler, context.RequestAborted);
        if (response is not null)
        {
            getStream.EnqueueRaw(new SseEnvelope("message", response.ToString(Formatting.None)));
        }

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

    private static string ResolveBaseRoute(McpServerOptions options)
    {
        var route = string.IsNullOrWhiteSpace(options.HttpSseRoute) ? "/mcp" : options.HttpSseRoute.TrimEnd('/');
        return string.IsNullOrEmpty(route) ? "/mcp" : route;
    }
}
