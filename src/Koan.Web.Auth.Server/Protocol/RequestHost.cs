using Microsoft.AspNetCore.Http;

namespace Koan.Web.Auth.Server.Protocol;

/// <summary>
/// Resolves the Authorization Server's public origin from the live request (forwarded-aware via
/// <see cref="HttpRequest.Scheme"/>/<see cref="HttpRequest.Host"/>), so the issuer + endpoint URLs it advertises
/// match the host the client actually reached — the correct posture behind a proxy.
/// </summary>
internal static class RequestHost
{
    /// <summary>The AS issuer / origin (host root): <c>{scheme}://{host}{pathbase}</c>.</summary>
    public static string Root(HttpContext ctx)
        => $"{ctx.Request.Scheme}://{ctx.Request.Host}{ctx.Request.PathBase}";

    public static string Url(HttpContext ctx, string path) => Root(ctx) + path;
}
