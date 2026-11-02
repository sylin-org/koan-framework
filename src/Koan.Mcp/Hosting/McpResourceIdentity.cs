using System;
using System.Linq;
using Koan.Mcp.Infrastructure;
using Koan.Mcp.Options;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Mcp.Hosting;

/// <summary>
/// SEC-0006 D2 — the MCP HTTP/SSE edge as an OAuth 2.1 <b>resource server</b>. Derives the canonical resource
/// id from the application's base URL (<see cref="KoanWebBaseUrl"/>: <c>Koan:Web:ApplicationUrl</c> when set, else the
/// URL the application accepted, else the live request's scheme, host and path base — WEB-0078, WEB-0081), read per
/// request, and the RFC 9728 discovery hints (the
/// protected-resource metadata URL the <c>WWW-Authenticate</c> challenge points at) clients use to find the
/// Authorization Server. The RFC 8707 audience check itself is Trust's <c>KoanBearerResource</c> (SEC-0025): a token
/// minted for another resource is authentic but carries the wrong audience, so the edge rejects it.
/// </summary>
public static class McpResourceIdentity
{
    /// <summary>
    /// The canonical resource id for this MCP edge. Prefers the configured fixed identifier
    /// (<see cref="Koan.Mcp.Options.McpServerOptions.ResourceUri"/>, an explicit override), then the application's
    /// base URL plus the MCP route — host-spoof-proof when <c>Koan:Web:ApplicationUrl</c> is set or the application
    /// accepted a URL, the live request host otherwise (the Development default).
    /// </summary>
    public static string Resolve(HttpContext ctx, string baseRoute, string? configuredResource = null)
        => !string.IsNullOrWhiteSpace(configuredResource)
            ? configuredResource.TrimEnd('/')
            : Base(ctx) + baseRoute;

    /// <summary>
    /// SEC-0037 — the names a token's audience may have here: this edge's, and with <see cref="McpServerOptions.AcceptApplicationBearer"/>
    /// the resource the application's ordinary routes accept, <c>Koan:Web:Auth:ResourceServer:Resource</c> when set, else the
    /// base URL. This package does not reference Koan.Web.Auth, so it reads the key the resource edge binds rather than its
    /// own idea of the application. A base URL only a request's <c>Host</c> could give names nothing outside Development
    /// (<see cref="KoanWebBaseUrl.Audience"/>), though <see cref="Resolve"/> still answers with it for discovery.
    /// </summary>
    internal static string[] Accepted(HttpContext ctx, string baseRoute, McpServerOptions options)
    {
        var services = ctx.RequestServices;
        var application = services.GetService<KoanWebBaseUrl>()?.Audience(ctx);
        var own = !string.IsNullOrWhiteSpace(options.ResourceUri)
            ? options.ResourceUri.TrimEnd('/')
            : application is null ? null : application + baseRoute;
        if (options.AcceptApplicationBearer && services.GetService<IConfiguration>()?[ConfigurationConstants.ApplicationResource] is { Length: > 0 } configured)
            application = configured;
        return [.. new[] { own, options.AcceptApplicationBearer ? application : null }.OfType<string>()];
    }

    /// <summary>The Authorization Server base (the application's base URL) — where RFC 8414 metadata + the AS endpoints live.</summary>
    public static string AuthorizationServer(HttpContext ctx) => Base(ctx);

    /// <summary>The RFC 9728 protected-resource metadata URL for this edge.</summary>
    public static string MetadataUrl(HttpContext ctx, string baseRoute)
        => Base(ctx) + "/.well-known/oauth-protected-resource" + baseRoute;

    private static string Base(HttpContext ctx)
        => ctx.RequestServices?.GetService<KoanWebBaseUrl>()?.Resolve(ctx)
           ?? $"{ctx.Request.Scheme}://{ctx.Request.Host}{ctx.Request.PathBase}";
}
