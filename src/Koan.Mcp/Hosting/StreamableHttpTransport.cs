using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core;
using Koan.Mcp.Options;
using Koan.Web.Sse;
using Koan.Web.Sse.Formatting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Hosting;

/// <summary>
/// AI-0037 — the MCP Streamable HTTP transport (spec 2025-06-18). A single endpoint at <c>{baseRoute}</c> serves:
/// <list type="bullet">
/// <item><b>POST</b> — one client→server JSON-RPC message; a notification is acked <c>202</c> with no body, a
/// request is answered EITHER as a per-request <c>text/event-stream</c> (default, carries the response then closes)
/// OR a single <c>application/json</c> object (opt-in).</item>
/// <item><b>GET</b> — opens the session's single standalone server-push SSE stream (one per session → <c>409</c>),
/// resumable via <c>Last-Event-ID</c> which replays that stream's buffered tail.</item>
/// <item><b>DELETE</b> — terminates the session.</item>
/// </list>
/// Every request dispatches through the shared <see cref="McpRpcDispatcher"/> (one dispatch+gating core for all
/// transports — "one projection or it drifts", SEC-0004) and is scoped to an <see cref="McpSession"/> minted on
/// <c>initialize</c>. The session id is NOT a bearer capability: a non-initialize request must come from the same
/// principal that established the session (<see cref="McpEdgeAuth.SamePrincipal"/>). Replaces the deprecated
/// 2024-11-05 HTTP+SSE pair (<see cref="HttpSseTransport"/>).
/// </summary>
public sealed class StreamableHttpTransport
{
    // The protocol versions this edge accepts on the MCP-Protocol-Version header (current + the two prior revisions
    // a conformant client may still send). An absent header means "assume 2025-03-26" per the spec's back-compat rule.
    private static readonly IReadOnlySet<string> SupportedProtocolVersions =
        new HashSet<string>(StringComparer.Ordinal) { "2025-06-18", "2025-03-26", "2024-11-05" };

    private const string SessionIdHeader = "Mcp-Session-Id";
    private const string ProtocolVersionHeader = "MCP-Protocol-Version";
    private const string LastEventIdHeader = "Last-Event-ID";

    private readonly McpSessionManager _sessions;
    private readonly McpServer _server;
    private readonly McpRpcDispatcher _dispatcher;
    private readonly IOptionsMonitor<McpServerOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StreamableHttpTransport> _logger;

    public StreamableHttpTransport(
        McpSessionManager sessions,
        McpServer server,
        McpRpcDispatcher dispatcher,
        IOptionsMonitor<McpServerOptions> options,
        TimeProvider timeProvider,
        ILogger<StreamableHttpTransport> logger)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ---- POST {baseRoute} : a single client→server JSON-RPC message ------------------------------------------------

    public async Task HandlePost(HttpContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        var options = _options.CurrentValue;
        if (!await PreflightAsync(context, options)) return;

        // The spec requires a POST client to accept BOTH representations the server may choose between.
        if (!AcceptsJsonAndEventStream(context.Request))
        {
            await WriteStatus(context, StatusCodes.Status406NotAcceptable, "not_acceptable",
                "Accept must include both application/json and text/event-stream.");
            return;
        }

        // 2025-06-18 removed batching: the body is a single JSON-RPC message.
        var payload = await ReadJsonBody(context);
        if (payload is null)
        {
            await WriteStatus(context, StatusCodes.Status400BadRequest, "invalid_json", "Request body is not valid JSON.");
            return;
        }
        if (!JsonRpcEnvelope.TryParse(payload, out var envelope, out var parseError))
        {
            await WriteStatus(context, StatusCodes.Status400BadRequest, "invalid_jsonrpc", parseError);
            return;
        }

        var isInitialize = string.Equals(envelope.Method, "initialize", StringComparison.Ordinal);

        McpSession session;
        if (isInitialize)
        {
            // initialize is exempt from the protocol-version header (it is where the version is negotiated, in the
            // JSON body) and mints the session whose id the client echoes thereafter.
            var created = _sessions.Create(context);
            if (created is null)
            {
                await WriteStatus(context, StatusCodes.Status429TooManyRequests, "max_connections_exceeded",
                    "The maximum number of concurrent MCP sessions has been reached.");
                return;
            }
            session = created;
            context.Response.Headers[SessionIdHeader] = session.Id;
        }
        else
        {
            if (!ValidateProtocolVersion(context, out var protocolError))
            {
                await WriteStatus(context, StatusCodes.Status400BadRequest, "unsupported_protocol_version", protocolError);
                return;
            }
            var resolved = await ResolveSessionOrWrite(context, options, envelope.Id);
            if (resolved is null) return;
            session = resolved;
            session.Touch();
        }

        // A JSON-RPC notification (no id) yields no response → ack with 202 and no body (spec).
        if (envelope.Id is null)
        {
            context.Response.StatusCode = StatusCodes.Status202Accepted;
            return;
        }

        var principal = session.User;
        var handler = _server.CreateHandler();

        if (options.Transport.StreamableJsonResponse)
        {
            var response = await _dispatcher.DispatchAsync(envelope, principal, handler, context.RequestAborted);
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(response?.ToString(Formatting.None) ?? string.Empty, context.RequestAborted);
            return;
        }

        // Default: answer on a per-request SSE stream carrying the single response (event-id stamped for resumption),
        // then close. The streaming shape is what later lets a handler emit interim notifications before its result.
        var stream = session.OpenRequestStream();
        JObject? rpcResponse;
        try
        {
            rpcResponse = await _dispatcher.DispatchAsync(envelope, principal, handler, context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            session.CompleteRequestStream(stream);
            return;
        }

        if (rpcResponse is not null) stream.EnqueueMessage(rpcResponse);
        session.CompleteRequestStream(stream);

        var result = Sse.Stream(stream.Read(context.RequestAborted));
        await result.ExecuteAsync(context);
    }

    // ---- GET {baseRoute} : content-negotiated — the SSE stream (MCP client) OR the console (browser) -------------

    /// <summary>
    /// The core owns the bare <c>GET {baseRoute}</c> (AI-0037 D-C). It negotiates: an explicit
    /// <c>text/event-stream</c> request is the Streamable server-push stream (bearer-gated when auth is required);
    /// a browser (<c>text/html</c> / <c>?format=html</c>) is delegated to the registered
    /// <see cref="IMcpConsoleRenderer"/> (anonymous — the WEB-0072 discoverable human face). Mapped OUTSIDE the
    /// auth group precisely so the console branch is not bearer-gated.
    /// </summary>
    public async Task HandleGet(HttpContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        var options = _options.CurrentValue;

        if (!OriginAllowed(context, options))
        {
            await WriteStatus(context, StatusCodes.Status403Forbidden, "origin_not_allowed",
                "The request Origin is not permitted.");
            return;
        }

        var format = context.Request.Query["format"].ToString();
        var explicitEventStream = AcceptsEventStreamExplicit(context.Request);
        var wantsConsole = string.Equals(format, "html", StringComparison.OrdinalIgnoreCase)
            || (!string.Equals(format, "json", StringComparison.OrdinalIgnoreCase) && AcceptsHtml(context.Request));

        // STREAM branch — an MCP client explicitly requesting the SSE stream.
        if (explicitEventStream)
        {
            if (!options.StreamableHttpEnabled)
            {
                context.Response.Headers.Allow = "GET, POST, DELETE";
                await WriteStatus(context, StatusCodes.Status405MethodNotAllowed, "stream_not_offered",
                    "The Streamable HTTP transport is not enabled on this endpoint.");
                return;
            }
            var baseRoute = ResolveBaseRoute(options);
            if (options.RequireAuthentication
                && !await McpEdgeAuth.EnsureAuthorized(context, baseRoute, requireAuth: true, options.ResourceUri))
            {
                return; // EnsureAuthorized wrote the 401 + WWW-Authenticate
            }
            await StreamServerPushAsync(context, options);
            return;
        }

        // CONSOLE branch — a browser (or ?format=html). Anonymous; delegated to the renderer seam.
        if (wantsConsole)
        {
            var renderer = context.RequestServices.GetService<IMcpConsoleRenderer>();
            if (renderer is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            await renderer.RenderConsoleAsync(context, ResolveBaseRoute(options));
            return;
        }

        context.Response.StatusCode = StatusCodes.Status404NotFound;
    }

    /// <summary>The Streamable server-push SSE stream (the event-stream branch of <see cref="HandleGet"/>). Assumes
    /// Origin + auth + content-negotiation already passed.</summary>
    private async Task StreamServerPushAsync(HttpContext context, McpServerOptions options)
    {
        if (KoanEnv.IsProduction && !KoanEnv.InContainer && !context.Request.IsHttps)
        {
            await WriteStatus(context, StatusCodes.Status400BadRequest, "https_required",
                "Streamable HTTP transport requires HTTPS in non-containerized production environments.");
            return;
        }
        if (!ValidateProtocolVersion(context, out var protocolError))
        {
            await WriteStatus(context, StatusCodes.Status400BadRequest, "unsupported_protocol_version", protocolError);
            return;
        }

        var session = await ResolveSessionOrWrite(context, options, null);
        if (session is null) return;
        session.Touch();

        var lastEventId = context.Request.Headers[LastEventIdHeader].ToString();
        var resuming = !string.IsNullOrEmpty(lastEventId);

        McpSseStream getStream;
        IReadOnlyList<SseEnvelope> replay = Array.Empty<SseEnvelope>();
        if (resuming)
        {
            // Resumption replays ONLY the named stream's buffered tail (never cross-stream), then continues live on
            // the standalone GET stream. Replace any stale GET stream the dropped connection left registered.
            var streamId = McpSseStream.StreamIdOf(lastEventId);
            if (streamId is not null && session.TryGetStream(streamId, out var prior))
            {
                replay = prior.ReplayAfter(lastEventId);
            }
            session.CloseGetStream();
            getStream = session.TryOpenGetStream()
                ?? throw new InvalidOperationException("GET stream could not be opened after close.");
        }
        else
        {
            var opened = session.TryOpenGetStream();
            if (opened is null)
            {
                // The spec permits only one standalone server-push stream per session.
                await WriteStatus(context, StatusCodes.Status409Conflict, "stream_exists",
                    "A server-push stream is already open for this session.");
                return;
            }
            getStream = opened;
        }

        try
        {
            await WriteServerPushStream(context, replay, getStream, context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            // client disconnected — normal.
        }
        finally
        {
            session.CloseGetStream();
        }
    }

    // ---- DELETE {baseRoute} : terminate the session ----------------------------------------------------------------

    public async Task HandleDelete(HttpContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        var options = _options.CurrentValue;
        if (!await PreflightAsync(context, options)) return;

        var sid = context.Request.Headers[SessionIdHeader].ToString();
        if (string.IsNullOrWhiteSpace(sid))
        {
            await WriteStatus(context, StatusCodes.Status400BadRequest, "missing_session",
                "Mcp-Session-Id header is required.");
            return;
        }

        if (!_sessions.TryGet(sid, out var session))
        {
            await WriteStatus(context, StatusCodes.Status404NotFound, "session_not_found",
                "Session not found or already terminated.");
            return;
        }
        if (options.RequireAuthentication && !McpEdgeAuth.SamePrincipal(context.User, session.User))
        {
            _logger.LogWarning("Rejected MCP Streamable DELETE: caller does not own session {SessionId}.", sid);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        _sessions.Terminate(sid);
        context.Response.StatusCode = StatusCodes.Status200OK;
    }

    // ---- shared plumbing -------------------------------------------------------------------------------------------

    /// <summary>Enabled + HTTPS-in-prod + Origin (DNS-rebinding) checks common to every method. Writes the failure
    /// response and returns false when a check fails.</summary>
    private async Task<bool> PreflightAsync(HttpContext context, McpServerOptions options)
    {
        if (!options.StreamableHttpEnabled)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return false;
        }

        if (!options.RequireAuthentication && (KoanEnv.IsProduction || KoanEnv.InContainer))
        {
            _logger.LogWarning(
                "SECURITY WARNING: MCP Streamable HTTP transport running without authentication in {Environment}.",
                KoanEnv.EnvironmentName);
        }

        if (KoanEnv.IsProduction && !KoanEnv.InContainer && !context.Request.IsHttps)
        {
            await WriteStatus(context, StatusCodes.Status400BadRequest, "https_required",
                "Streamable HTTP transport requires HTTPS in non-containerized production environments.");
            return false;
        }

        if (!OriginAllowed(context, options))
        {
            await WriteStatus(context, StatusCodes.Status403Forbidden, "origin_not_allowed",
                "The request Origin is not permitted.");
            return false;
        }

        return true;
    }

    /// <summary>Resolve the session named by <c>Mcp-Session-Id</c>, enforcing same-principal ownership. Writes the
    /// JSON-RPC-shaped error (or a 403) and returns null on failure.</summary>
    private async Task<McpSession?> ResolveSessionOrWrite(HttpContext context, McpServerOptions options, JToken? rpcId)
    {
        var sid = context.Request.Headers[SessionIdHeader].ToString();
        if (string.IsNullOrWhiteSpace(sid))
        {
            // -32000 + 400 = "you must initialize first" (no session id on a non-initialize request).
            await WriteJsonRpcError(context, StatusCodes.Status400BadRequest, rpcId, -32000,
                "Mcp-Session-Id header is required.");
            return null;
        }
        if (!_sessions.TryGet(sid, out var session))
        {
            // -32001 + 404 = the re-init signal: the client re-POSTs initialize with NO session id.
            await WriteJsonRpcError(context, StatusCodes.Status404NotFound, rpcId, -32001,
                "Session not found or terminated.");
            return null;
        }
        // SEC-0006 — the session id is not a bearer capability: a non-initialize request must come from the same
        // subject that established the session. The downstream gate runs as session.User, so a different caller who
        // learns a session id must not be able to inject RPC that executes under the owner's principal.
        if (options.RequireAuthentication && !McpEdgeAuth.SamePrincipal(context.User, session.User))
        {
            _logger.LogWarning("Rejected MCP Streamable request: caller does not own session {SessionId}.", sid);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return null;
        }
        return session;
    }

    private static bool OriginAllowed(HttpContext context, McpServerOptions options)
    {
        // DNS-rebinding guard: a browser always sets Origin. When an allow-list is configured AND an Origin is
        // present it must match; a non-browser client (no Origin) is not the rebinding threat and is allowed. With no
        // allow-list configured we don't reject here (CORS still governs browser reads) — the existing edge posture.
        if (options.AllowedOrigins.Length == 0) return true;
        var origin = context.Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin)) return true;
        return options.AllowedOrigins.Any(o => string.Equals(o, origin, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ValidateProtocolVersion(HttpContext context, out string error)
    {
        error = string.Empty;
        var header = context.Request.Headers[ProtocolVersionHeader].ToString();
        if (string.IsNullOrEmpty(header)) return true; // absent → assume 2025-03-26 (spec back-compat)
        if (SupportedProtocolVersions.Contains(header)) return true;
        error = $"Unsupported MCP-Protocol-Version '{header}'.";
        return false;
    }

    private static bool AcceptsJsonAndEventStream(HttpRequest request)
    {
        var accept = request.Headers.Accept;
        if (accept.Count == 0) return false; // the spec requires the client to advertise both
        return AcceptIncludes(accept, "application/json") && AcceptIncludes(accept, "text/event-stream");
    }

    /// <summary>The bare GET is dual-purpose, so the stream branch requires an EXPLICIT <c>text/event-stream</c> in
    /// Accept (NOT the lenient empty→stream rule) — a browser sending <c>*/*</c> or no Accept must fall to the
    /// console branch, never the SSE stream.</summary>
    private static bool AcceptsEventStreamExplicit(HttpRequest request)
    {
        foreach (var raw in request.Headers.Accept)
        {
            if (string.IsNullOrEmpty(raw)) continue;
            foreach (var part in raw.Split(','))
            {
                if (string.Equals(part.Split(';')[0].Trim(), "text/event-stream", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    /// <summary>The console (HTML) negotiation, ported verbatim from the Explorer: <c>text/html</c> present AND both
    /// <c>text/event-stream</c> and <c>application/json</c> absent — i.e. a browser, never an MCP client.</summary>
    private static bool AcceptsHtml(HttpRequest request)
    {
        var accept = request.Headers.Accept.ToString();
        if (string.IsNullOrEmpty(accept)) return false;
        var tokens = accept
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Split(';')[0].Trim().ToLowerInvariant())
            .ToHashSet();
        return tokens.Contains("text/html")
            && !tokens.Contains("text/event-stream")
            && !tokens.Contains("application/json");
    }

    private static string ResolveBaseRoute(McpServerOptions options)
    {
        var route = string.IsNullOrWhiteSpace(options.HttpSseRoute) ? "/mcp" : options.HttpSseRoute.TrimEnd('/');
        return string.IsNullOrEmpty(route) ? "/mcp" : route;
    }

    private static bool AcceptIncludes(StringValues accept, string mediaType)
    {
        var slash = mediaType.IndexOf('/');
        var typeWildcard = slash > 0 ? string.Concat(mediaType.AsSpan(0, slash), "/*") : null;
        foreach (var raw in accept)
        {
            if (string.IsNullOrEmpty(raw)) continue;
            foreach (var part in raw.Split(','))
            {
                var token = part.Split(';')[0].Trim();
                if (token.Length == 0) continue;
                if (token == "*/*") return true;
                if (string.Equals(token, mediaType, StringComparison.OrdinalIgnoreCase)) return true;
                if (typeWildcard is not null && string.Equals(token, typeWildcard, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        return false;
    }

    private async Task<JToken?> ReadJsonBody(HttpContext context)
    {
        try
        {
            using var reader = new StreamReader(context.Request.Body);
            using var jsonReader = new JsonTextReader(reader);
            return await JToken.ReadFromAsync(jsonReader, context.RequestAborted);
        }
        catch (JsonReaderException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON submitted to the MCP Streamable HTTP endpoint.");
            return null;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "I/O failure reading the MCP Streamable HTTP POST body.");
            return null;
        }
    }

    private static Task WriteStatus(HttpContext context, int status, string error, string? message = null)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(
            message is null ? (object)new { error } : new { error, message },
            cancellationToken: context.RequestAborted);
    }

    private static Task WriteJsonRpcError(HttpContext context, int httpStatus, JToken? id, int code, string message)
    {
        context.Response.StatusCode = httpStatus;
        context.Response.ContentType = "application/json";
        var body = McpRpcDispatcher.CreateError(id, code, message);
        return context.Response.WriteAsync(body.ToString(Formatting.None), context.RequestAborted);
    }

    /// <summary>
    /// Write the standalone server-push SSE stream: the 200 headers are flushed immediately (with an SSE keep-alive
    /// comment) so the client sees the stream open even before any message arrives — a long-lived stream may push
    /// nothing for a while. Then any resumption replay, then the live frames. Unlike the per-request POST response
    /// (which always carries one data event and so flushes naturally), this stream must establish itself eagerly.
    /// </summary>
    private static async Task WriteServerPushStream(HttpContext context, IReadOnlyList<SseEnvelope> replay, McpSseStream live, CancellationToken cancellationToken)
    {
        var response = context.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Pragma = "no-cache";
        response.Headers.Connection = "keep-alive";
        response.Headers["X-Accel-Buffering"] = "no";

        await response.WriteAsync(": connected\n\n", cancellationToken); // SSE comment — establishes the stream, ignored by clients
        await response.Body.FlushAsync(cancellationToken);

        foreach (var envelope in replay)
        {
            await response.WriteAsync(SseFormatter.ToWireFormat(envelope), cancellationToken);
            await response.Body.FlushAsync(cancellationToken);
        }

        await foreach (var envelope in live.Read(cancellationToken).WithCancellation(cancellationToken))
        {
            await response.WriteAsync(SseFormatter.ToWireFormat(envelope), cancellationToken);
            await response.Body.FlushAsync(cancellationToken);
        }
    }
}
