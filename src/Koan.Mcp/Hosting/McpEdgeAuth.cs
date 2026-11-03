using System.Security.Claims;
using System.Threading.Tasks;
using Koan.Mcp.CustomTools;
using Koan.Mcp.Options;
using Koan.Security.Trust.Inbound;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Koan.Mcp.Hosting;

/// <summary>
/// SEC-0006 D2/D3 — the MCP edge as an OAuth resource server. It authenticates the bearer for its own resource
/// <b>explicitly</b> (rather than via <c>RequireAuthorization</c>) through Trust's <see cref="KoanBearerResource"/>, the
/// mechanism Web Auth's resource edge for ordinary requests shares (SEC-0025), so it owns its own challenge: a
/// resource server must emit an RFC 9728 <c>WWW-Authenticate</c> pointing at its protected-resource metadata, which
/// the shared bearer scheme cannot know. On success the bearer identity is placed in <see cref="HttpContext.User"/>
/// — from there the existing OriginStamp → session → SEC-0004/0005 chain runs unchanged (D3).
/// </summary>
internal static class McpEdgeAuth
{
    private const string NoApplicationUrl = "This application has no address yet, so no token can be for it. Set Koan:Web:ApplicationUrl or accept a URL, then connect again.";

    /// <summary>
    /// Authenticate + audience-check this request. Returns <c>true</c> to proceed; on failure it writes the
    /// 401 challenge itself and returns <c>false</c>. A no-op (returns true) when authentication is not required.
    /// </summary>
    public static async Task<bool> EnsureAuthorized(HttpContext context, string baseRoute, bool requireAuth, McpServerOptions options)
    {
        if (!requireAuth) return true;

        // Fail loudly (not with a confusing raw exception) if host composition is incomplete. Koan.Mcp references
        // Trust directly, so ordinary AddKoan discovery registers this scheme without a Web Auth prerequisite.
        var schemes = context.RequestServices.GetService<IAuthenticationSchemeProvider>();
        if (schemes is null || await schemes.GetSchemeAsync(KoanBearerDefaults.AuthenticationScheme) is null)
        {
            context.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("Koan.Mcp")
                .LogError("MCP edge requires authentication but the '{Scheme}' bearer scheme is not registered. " +
                          "Ensure AddKoan() module discovery completed before mapping MCP.",
                    KoanBearerDefaults.AuthenticationScheme);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { error = "bearer_scheme_unavailable" }, cancellationToken: context.RequestAborted);
            return false;
        }

        // Explicitly run the trust-fabric bearer for THIS resource (the bearer scheme is non-default). SEC-0006 D2 —
        // the RFC 8707 audience must name this resource: an authentic token for another resource (a sibling API, the
        // application's own pages, a service-mesh aud=koan token) is rejected here — the confused-deputy fix. A
        // conformant client re-authorizes with resource=<this> on the challenge. No token at all is a plain challenge.
        // With no name for this edge no token could pass, and a 401 would send a standard client through OAuth for one, again
        // and again: it is told why.
        if (McpResourceIdentity.Audience(context, baseRoute, options) is not { } resource)
        {
            await WriteNoApplicationUrl(context);
            return false;
        }

        var result = await KoanBearerResource.Authenticate(context, resource);
        if (!result.Succeeded || result.Principal?.Identity?.IsAuthenticated != true)
        {
            if (result.None) await WriteChallenge(context, baseRoute, "unauthorized");
            else await WriteChallenge(context, baseRoute, KoanBearerResource.InvalidToken, error: KoanBearerResource.InvalidToken);
            return false;
        }

        context.User = result.Principal;
        context.Features.Set<IAuthenticateResultFeature>(new RouteToolInvoker.Authenticated(result));
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

    private static Task WriteNoApplicationUrl(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return context.Response.WriteAsJsonAsync(new { error = "no_application_url", message = NoApplicationUrl }, cancellationToken: context.RequestAborted);
    }

    private static async Task WriteChallenge(HttpContext context, string baseRoute, string body,
        string? error = null, string? description = null)
    {
        // A fixed resource needs no URL for a token to pass; a challenge needs one to point at the metadata, and Production derives none.
        if (context.RequestServices.GetService<KoanWebBaseUrl>() is { Current: null, DerivesFromRequests: false })
        {
            await WriteNoApplicationUrl(context);
            return;
        }

        context.Response.Headers.WWWAuthenticate = KoanBearerResource.Challenge(error, description, McpResourceIdentity.MetadataUrl(context, baseRoute));
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = body }, cancellationToken: context.RequestAborted);
    }
}
