using Koan.Web.Auth.Server.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Web.Auth.Server.Protocol;

/// <summary>
/// Resolves the Authorization Server's public origin. Prefers the configured
/// <see cref="AuthServerOptions.Issuer"/> (authoritative, host-spoof-proof behind a proxy) and falls back to the
/// live request host (the Development default) — so the issuer + endpoint URLs the AS advertises match the host
/// the client actually reached.
/// </summary>
internal static class RequestHost
{
    /// <summary>The AS issuer / origin (host root).</summary>
    public static string Root(HttpContext ctx)
    {
        var configured = ctx.RequestServices.GetService<IOptions<AuthServerOptions>>()?.Value.Issuer;
        return !string.IsNullOrWhiteSpace(configured)
            ? configured.TrimEnd('/')
            : $"{ctx.Request.Scheme}://{ctx.Request.Host}{ctx.Request.PathBase}";
    }

    public static string Url(HttpContext ctx, string path) => Root(ctx) + path;
}
