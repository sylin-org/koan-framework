using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Koan.Mcp.Execution;
using Koan.Mcp.Options;
using Koan.Mcp.Resources;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Explorer.Hosting;

/// <summary>
/// WEB-0072 — mounts the Explorer surface on the bare MCP route (currently unclaimed): a content-negotiated
/// HTML console at <c>GET {baseRoute}</c>, the per-caller surface at <c>GET {baseRoute}/map.json</c>, the SPA
/// assets at <c>GET {baseRoute}/explorer/{**asset}</c>, and the in-process try-it at <c>POST {baseRoute}/explorer/call</c>.
/// Self-gates on <see cref="McpExplorerOptions.Enabled"/>. Works standalone — it calls the in-process executor,
/// it does NOT require the HTTP/SSE transport to be enabled.
/// </summary>
internal sealed class McpExplorerEndpointContributor : IKoanEndpointContributor
{
    // Map after the MCP edge for determinism. No path collision exists (bare {baseRoute} + /map.json + /explorer/*
    // vs the edge's /sse, /rpc, /capabilities, /health) — Order is documentation, not a dependency.
    public int Order => 100;

    public void Map(IEndpointRouteBuilder endpoints)
    {
        var services = endpoints.ServiceProvider;
        var explorer = services.GetRequiredService<IOptionsMonitor<McpExplorerOptions>>().CurrentValue;
        if (!explorer.Enabled) return;

        var mcp = services.GetRequiredService<IOptionsMonitor<McpServerOptions>>().CurrentValue;
        var baseRoute = string.IsNullOrWhiteSpace(mcp.HttpSseRoute) ? "/mcp" : mcp.HttpSseRoute.TrimEnd('/');
        if (string.IsNullOrEmpty(baseRoute)) baseRoute = "/mcp";

        endpoints.MapGet(baseRoute, ctx => ServeConsole(ctx, baseRoute)).WithName("KoanMcpExplorerConsole").ExcludeFromDescription();
        endpoints.MapGet($"{baseRoute}/map.json", ServeMap).WithName("KoanMcpExplorerMap").ExcludeFromDescription();
        endpoints.MapGet($"{baseRoute}/access-map.json", ServeAccessMap).WithName("KoanMcpExplorerAccessMap").ExcludeFromDescription();
        endpoints.MapGet($"{baseRoute}/explorer/{{**asset}}", ServeAsset).WithName("KoanMcpExplorerAssets").ExcludeFromDescription();
        endpoints.MapPost($"{baseRoute}/explorer/call", ExecuteTool).WithName("KoanMcpExplorerCall").ExcludeFromDescription();
    }

    // GET {baseRoute} — WEB-0072 D2: serve HTML only to a browser-style Accept (or explicit ?format=html);
    // never intercept an MCP client (which advertises text/event-stream / application/json). 404 otherwise,
    // preserving the bare path's current behavior.
    private static async Task ServeConsole(HttpContext context, string baseRoute)
    {
        var format = context.Request.Query["format"].ToString();
        var wantHtml = string.Equals(format, "html", StringComparison.OrdinalIgnoreCase)
            || (!string.Equals(format, "json", StringComparison.OrdinalIgnoreCase)
                && AcceptsHtml(context.Request.Headers.Accept.ToString()));

        if (!wantHtml)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (!ExplorerAssetProvider.TryGet("index.html", out var html, out _))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        html = html.Replace("{{base}}", baseRoute);
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.Headers["Vary"] = "Accept";
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(html);
    }

    private static bool AcceptsHtml(string accept)
    {
        if (string.IsNullOrEmpty(accept)) return false;
        var tokens = accept
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Split(';')[0].Trim().ToLowerInvariant())
            .ToHashSet();
        return tokens.Contains("text/html")
            && !tokens.Contains("text/event-stream")
            && !tokens.Contains("application/json");
    }

    // GET {baseRoute}/map.json — the per-caller surface (anonymous-safe by construction; mirrors tools/list + doors).
    private static async Task ServeMap(HttpContext context)
    {
        var projector = context.RequestServices.GetRequiredService<McpSurfaceProjector>();
        var surface = projector.Project(context.User);
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.Headers["Vary"] = "Accept";
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(surface.ToString(Formatting.None));
    }

    // GET {baseRoute}/access-map.json — WEB-0072 D5: the PRIVILEGED god-view (every requirement, walls included).
    // Fail-closed: served in Development, or to a caller holding the configured admin role/scope; otherwise 404
    // (so the endpoint's very existence is not disclosed to an unprivileged caller).
    private static async Task ServeAccessMap(HttpContext context)
    {
        var env = context.RequestServices.GetRequiredService<IHostEnvironment>();
        var options = context.RequestServices.GetRequiredService<IOptionsMonitor<McpExplorerOptions>>().CurrentValue;
        if (!AccessMapGate.Allowed(env.IsDevelopment(), context.User, options))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var projector = context.RequestServices.GetRequiredService<AccessMapProjector>();
        var map = projector.Project();
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(map.ToString(Formatting.None));
    }

    private static async Task ServeAsset(HttpContext context)
    {
        var asset = context.Request.RouteValues["asset"]?.ToString();
        if (!ExplorerAssetProvider.TryGet(asset, out var content, out var contentType))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.ContentType = contentType;
        await context.Response.WriteAsync(content);
    }

    // POST {baseRoute}/explorer/call — WEB-0072 D3/D4: try-it executes IN-PROCESS as the caller (no token, no
    // proxy). Describe is anonymous-readable; execute requires a session.
    private static async Task ExecuteTool(HttpContext context)
    {
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            await WriteJson(context, StatusCodes.Status401Unauthorized, new JObject
            {
                ["error"] = "authentication_required",
                ["message"] = "Sign in to invoke a tool.",
            });
            return;
        }

        ExecuteRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<ExecuteRequest>(context.RequestAborted);
        }
        catch
        {
            request = null;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Name))
        {
            await WriteJson(context, StatusCodes.Status400BadRequest, new JObject
            {
                ["error"] = "invalid_request",
                ["message"] = "A tool 'name' is required.",
            });
            return;
        }

        JObject? args = null;
        if (request.Arguments is JsonElement el && el.ValueKind == JsonValueKind.Object)
        {
            args = JObject.Parse(el.GetRawText());
        }

        var executor = context.RequestServices.GetRequiredService<EndpointToolExecutor>();
        var result = await executor.Execute(request.Name!, args, context.RequestAborted, context.User);

        await WriteJson(context, StatusCodes.Status200OK, new JObject
        {
            ["success"] = result.Success,
            ["payload"] = result.Payload?.DeepClone(),
            ["shortCircuit"] = result.ShortCircuit?.DeepClone(),
            ["warnings"] = new JArray(result.Warnings),
            ["errorCode"] = result.ErrorCode,
            ["errorMessage"] = result.ErrorMessage,
        });
    }

    private static Task WriteJson(HttpContext context, int statusCode, JObject body)
    {
        context.Response.StatusCode = statusCode;
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsync(body.ToString(Formatting.None));
    }

    private sealed class ExecuteRequest
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("arguments")]
        public JsonElement? Arguments { get; set; }
    }
}
