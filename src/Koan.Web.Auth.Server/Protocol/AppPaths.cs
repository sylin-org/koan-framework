using Koan.Web.Auth.Infrastructure;
using Koan.Web.Auth.Server.Infrastructure;
using Koan.Web.Auth.Server.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Web.Auth.Server.Protocol;

/// <summary>
/// Resolves the app-owned consent/terminal page paths. Prefers the AS option
/// (<c>Koan:Web:Auth:Server:ConsentPath</c>/<c>DonePath</c>), falls back to the MCP-namespaced key the app may
/// have been told to set (<c>Koan:Mcp:Auth:*</c>), then the option default.
/// <para>
/// SEC-0027: every path the server writes for the browser — a redirect, a link, a cookie's <c>Path</c> — is
/// application-relative and gets the application's path base exactly once (<see cref="ApplicationPaths"/>), whether the
/// server writes it as a path or inside an absolute URL built on the public origin.
/// </para>
/// </summary>
internal static class AppPaths
{
    public static string Consent(HttpContext ctx, AuthServerOptions options)
        => Resolve(ctx, "ConsentPath", options.ConsentPath);

    public static string Done(HttpContext ctx, AuthServerOptions options)
        => Resolve(ctx, "DonePath", options.DonePath);

    /// <summary>An application-relative <paramref name="path"/> under the application's path base, exactly once.</summary>
    public static string Under(HttpContext ctx, string path) => ApplicationPaths.Under(ctx, path);

    /// <summary>The consent seam's <paramref name="route"/> (approve or deny) for <paramref name="id"/>, under the path base.</summary>
    public static string Request(HttpContext ctx, string route, string id)
        => Under(ctx, route.Replace(AuthServerRoutes.RequestId, Uri.EscapeDataString(id), StringComparison.Ordinal));

    /// <summary>The <c>Path</c> of the browser-binding cookie: the server's <c>/oauth</c> routes under the path base.</summary>
    public static string BindingCookie(HttpContext ctx) => Under(ctx, AuthServerRoutes.OAuthBase);

    private static string Resolve(HttpContext ctx, string key, string fallback)
    {
        var cfg = ctx.RequestServices.GetService<IConfiguration>();
        return cfg?["Koan:Web:Auth:Server:" + key] ?? cfg?["Koan:Mcp:Auth:" + key] ?? fallback;
    }
}
