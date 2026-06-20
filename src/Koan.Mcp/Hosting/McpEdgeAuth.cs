using System.Security.Claims;
using System.Threading.Tasks;
using Koan.Security.Trust.Inbound;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Koan.Mcp.Hosting;

/// <summary>
/// SEC-0006 D2/D3 — the MCP edge as an OAuth resource server. It authenticates the <c>Koan.bearer</c> scheme
/// <b>explicitly</b> (rather than via <c>RequireAuthorization</c>) so it owns its own challenge: a resource
/// server must emit an RFC 9728 <c>WWW-Authenticate</c> pointing at its protected-resource metadata, which the
/// shared bearer scheme cannot know. On success the bearer identity is placed in <see cref="HttpContext.User"/>
/// — from there the existing OriginStamp → session → SEC-0004/0005 chain runs unchanged (D3).
/// </summary>
internal static class McpEdgeAuth
{
    /// <summary>
    /// Authenticate + audience-check this request. Returns <c>true</c> to proceed; on failure it writes the
    /// 401 challenge itself and returns <c>false</c>. A no-op (returns true) when authentication is not required.
    /// </summary>
    public static async Task<bool> EnsureAuthorized(HttpContext context, string baseRoute, bool requireAuth, string? configuredResource)
    {
        if (!requireAuth) return true;

        // Fail loudly (not with a confusing raw exception) when the edge requires auth but the bearer scheme
        // was never registered — i.e. Koan.Mcp is used without Koan.Web.Auth, which installs AddKoanBearer().
        var schemes = context.RequestServices.GetService<IAuthenticationSchemeProvider>();
        if (schemes is null || await schemes.GetSchemeAsync(KoanBearerDefaults.AuthenticationScheme) is null)
        {
            context.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("Koan.Mcp")
                .LogError("MCP edge requires authentication but the '{Scheme}' bearer scheme is not registered. " +
                          "Reference Koan.Web.Auth (or Koan.Web.Auth.Server) so AddKoanBearer() runs.",
                    KoanBearerDefaults.AuthenticationScheme);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { error = "bearer_scheme_unavailable" }, cancellationToken: context.RequestAborted);
            return false;
        }

        // Explicitly run the trust-fabric bearer scheme (it is non-default — the default scheme is the cookie).
        var result = await context.AuthenticateAsync(KoanBearerDefaults.AuthenticationScheme);
        if (!result.Succeeded || result.Principal?.Identity?.IsAuthenticated != true)
        {
            await WriteChallenge(context, baseRoute, "unauthorized");
            return false;
        }

        context.User = result.Principal;

        // SEC-0006 D2 — enforce the RFC 8707 audience: the token must be bound to THIS resource. An authentic
        // token for another resource (a sibling API, a service-mesh aud=koan token) is rejected here — the
        // confused-deputy fix. A conformant client re-authorizes with resource=<this> on the challenge.
        var resource = McpResourceIdentity.Resolve(context, baseRoute, configuredResource);
        if (!McpResourceIdentity.AudienceMatches(context.User, resource))
        {
            await WriteChallenge(context, baseRoute, "invalid_token", error: "invalid_token", description: "audience");
            return false;
        }

        return true;
    }

    /// <summary>
    /// SEC-0006 — the bearer identity on an <c>/mcp/rpc</c> POST must be the same subject that established the
    /// session on the SSE handshake. The session id alone is not a capability: a different authenticated caller
    /// must not be able to inject RPC into another user's session and have it run under that user's principal.
    /// </summary>
    public static bool SamePrincipal(ClaimsPrincipal caller, ClaimsPrincipal sessionOwner)
    {
        var a = Subject(caller);
        var b = Subject(sessionOwner);
        return !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.Ordinal);
    }

    private static string? Subject(ClaimsPrincipal p)
        => p.FindFirst("sub")?.Value ?? p.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    private static async Task WriteChallenge(HttpContext context, string baseRoute, string body,
        string? error = null, string? description = null)
    {
        context.Response.Headers.WWWAuthenticate = McpResourceIdentity.BuildChallenge(context, baseRoute, error, description);
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = body }, cancellationToken: context.RequestAborted);
    }
}
