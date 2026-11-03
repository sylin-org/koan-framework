using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Koan.Core;
using Koan.Mcp.Options;
using Koan.Web.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Hosting;

/// <summary>
/// AI-0037, SEC-0039 — the MCP Streamable HTTP transport, with no session. A single endpoint at <c>{baseRoute}</c>:
/// <list type="bullet">
/// <item><b>POST</b> — one client→server JSON-RPC message, standing alone: a notification is acked <c>202</c> with no body,
/// a request is answered as one <c>application/json</c> object. A <c>Mcp-Session-Id</c> a client still sends means nothing.</item>
/// <item><b>GET</b> — a browser is handed to the <see cref="IMcpConsoleRenderer"/> when one is registered; anything else is
/// <c>405</c>, the specification's answer from an endpoint that offers no stream.</item>
/// </list>
/// Every request dispatches through the shared <see cref="McpRpcDispatcher"/> as the principal its own credential and
/// connection name (SEC-0016). A tool call is already a fresh request, so nothing is kept between requests.
/// </summary>
public sealed class StreamableHttpTransport
{
    private const string ProtocolVersionHeader = "MCP-Protocol-Version";

    private readonly McpServer _server;
    private readonly McpRpcDispatcher _dispatcher;
    private readonly IOptionsMonitor<McpServerOptions> _options;
    private readonly ILogger<StreamableHttpTransport> _logger;

    public StreamableHttpTransport(
        McpServer server,
        McpRpcDispatcher dispatcher,
        IOptionsMonitor<McpServerOptions> options,
        ILogger<StreamableHttpTransport> logger)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// SEC-0016, SEC-0039: the principal one HTTP request is authorized as: whoever the application's authentication found
    /// on it, or nobody, stamped with the origin tier of its own connection. SEC-0004 origin: a
    /// remote (HTTP) caller is never <c>local</c>; it is <c>internal</c> only from a declared trusted network.
    /// </summary>
    public static ClaimsPrincipal RequestPrincipal(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var originOptions = context.RequestServices.GetService<IOptions<OriginOptions>>()?.Value ?? OriginOptions.Empty;
        return OriginStamp.Apply(context.User ?? new ClaimsPrincipal(), OriginResolver.FromHttpContext(context, originOptions));
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

        // initialize is exempt from the protocol-version header: it is where the version is negotiated, in the JSON body.
        if (!string.Equals(envelope.Method, "initialize", StringComparison.Ordinal) && !ValidateProtocolVersion(context, out var protocolError))
        {
            await WriteStatus(context, StatusCodes.Status400BadRequest, "unsupported_protocol_version", protocolError);
            return;
        }

        // A JSON-RPC notification (no id) yields no response → ack with 202 and no body (spec).
        if (envelope.Id is null)
        {
            context.Response.StatusCode = StatusCodes.Status202Accepted;
            return;
        }

        var response = await _dispatcher.DispatchAsync(envelope, RequestPrincipal(context), _server.CreateHandler(), context.RequestAborted);
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(response?.ToString(Formatting.None) ?? string.Empty, context.RequestAborted);
    }

    // ---- GET {baseRoute} : the console (browser), and otherwise no stream ------------------------------------------

    /// <summary>
    /// The core owns the bare <c>GET {baseRoute}</c> (AI-0037 D-C). A browser (<c>text/html</c> / <c>?format=html</c>) is
    /// delegated to the registered <see cref="IMcpConsoleRenderer"/> (anonymous — the WEB-0072 discoverable human face).
    /// Anything else is told there is no stream to open.
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
        var wantsConsole = string.Equals(format, "html", StringComparison.OrdinalIgnoreCase)
            || (!string.Equals(format, "json", StringComparison.OrdinalIgnoreCase) && AcceptsHtml(context.Request));
        if (wantsConsole && context.RequestServices.GetService<IMcpConsoleRenderer>() is { } renderer)
        {
            await renderer.RenderConsoleAsync(context, ResolveBaseRoute(options));
            return;
        }

        if (!options.EnableStreamableHttpTransport)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        context.Response.Headers.Allow = "POST";
        await WriteStatus(context, StatusCodes.Status405MethodNotAllowed, "no_stream", "This server keeps no session and pushes nothing: POST each request on its own.");
    }

    // ---- shared plumbing -------------------------------------------------------------------------------------------

    /// <summary>Enabled + HTTPS-in-prod + Origin (DNS-rebinding) checks. Writes the failure response and returns false
    /// when a check fails.</summary>
    private async Task<bool> PreflightAsync(HttpContext context, McpServerOptions options)
    {
        if (!options.EnableStreamableHttpTransport)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return false;
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
        if (McpRpcHandler.SupportedProtocolVersions.Contains(header)) return true;
        error = $"Unsupported MCP-Protocol-Version '{header}'.";
        return false;
    }

    private static bool AcceptsJsonAndEventStream(HttpRequest request)
    {
        var accept = request.Headers.Accept;
        if (accept.Count == 0) return false; // the spec requires the client to advertise both
        return AcceptIncludes(accept, "application/json") && AcceptIncludes(accept, "text/event-stream");
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
        var route = string.IsNullOrWhiteSpace(options.HttpRoute) ? "/mcp" : options.HttpRoute.TrimEnd('/');
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
            using var jsonReader = new JsonTextReader(reader) { DateParseHandling = DateParseHandling.None };
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
}
