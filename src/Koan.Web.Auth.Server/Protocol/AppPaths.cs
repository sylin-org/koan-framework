using Koan.Web.Auth.Server.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Web.Auth.Server.Protocol;

/// <summary>
/// Resolves the app-owned consent/terminal page paths. Prefers the AS option
/// (<c>Koan:Web:Auth:Server:ConsentPath</c>/<c>DonePath</c>), falls back to the MCP-namespaced key the app may
/// have been told to set (<c>Koan:Mcp:Auth:*</c>), then the option default.
/// </summary>
internal static class AppPaths
{
    public static string Consent(HttpContext ctx, AuthServerOptions options)
        => Resolve(ctx, "ConsentPath", options.ConsentPath);

    public static string Done(HttpContext ctx, AuthServerOptions options)
        => Resolve(ctx, "DonePath", options.DonePath);

    private static string Resolve(HttpContext ctx, string key, string fallback)
    {
        var cfg = ctx.RequestServices.GetService<IConfiguration>();
        return cfg?["Koan:Web:Auth:Server:" + key] ?? cfg?["Koan:Mcp:Auth:" + key] ?? fallback;
    }
}
