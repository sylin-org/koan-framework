using Koan.Web.Auth.Server.Options;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Web.Auth.Server.Protocol;

/// <summary>
/// Resolves the Authorization Server's public origin, where its endpoints and the application's pages are reached.
/// Prefers the configured <see cref="AuthServerOptions.Issuer"/> (an explicit override), then the application's
/// canonical base URL (<c>Koan:Web:ApplicationUrl</c>, WEB-0078 — authoritative and host-spoof-proof behind a proxy,
/// path included — else the URL the application accepted, WEB-0081), and falls back to the live request host (the
/// Development default) — so the endpoint URLs the AS advertises match the host the client actually reached. The
/// issuer identifier is <see cref="AuthServerIssuer"/>'s.
/// </summary>
internal static class RequestHost
{
    /// <summary>The AS's public origin (the application's base URL).</summary>
    public static string Root(HttpContext ctx)
    {
        var configured = ctx.RequestServices.GetService<IOptions<AuthServerOptions>>()?.Value.Issuer;
        if (!string.IsNullOrWhiteSpace(configured)) return configured.TrimEnd('/');

        var baseUrl = ctx.RequestServices.GetService<KoanWebBaseUrl>();
        return baseUrl is not null
            ? baseUrl.Resolve(ctx)
            : $"{ctx.Request.Scheme}://{ctx.Request.Host}{ctx.Request.PathBase}";
    }

    public static string Url(HttpContext ctx, string path) => Root(ctx) + path;
}
