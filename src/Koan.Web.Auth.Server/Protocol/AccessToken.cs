using System.Security.Claims;
using Koan.Core;
using Koan.Web.Auth.Options;
using Koan.Web.Auth.Server.Hosting;
using Koan.Web.Auth.Server.Options;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Web.Auth.Server.Protocol;

/// <summary>
/// SEC-0037 — a bearer for a person or an agent the application has verified by itself, at a door the authorization
/// server does not stand behind. It is the token the server's own flows mint, for the application's own resource
/// (<c>Koan:Web:Auth:ResourceServer:Resource</c>, else the base URL), for <see cref="AuthServerOptions.AccessTokenLifetime"/>.
/// It has no refresh token and no grant: whoever proved themselves once proves themselves again.
/// </summary>
/// <param name="Value">The token, for an <c>Authorization: Bearer</c> header.</param>
/// <param name="Lifetime">How long it lasts from now, for <c>expires_in</c>.</param>
public sealed record AccessToken(string Value, TimeSpan Lifetime)
{
    /// <summary>
    /// A token for <paramref name="principal"/>, whom this request has verified: its subject, name, email, roles and scopes,
    /// with the subject's standing over its name and roles, as in every token the server issues. Null when that subject may
    /// not act now. Where standing comes from Koan.Identity, a new account has none until it has signed in, so a door signs
    /// in first and passes the principal it signed in, which sign-in settles on the person's id.
    /// </summary>
    /// <exception cref="InvalidOperationException"><paramref name="principal"/> names no subject; or, outside Development,
    /// nothing names the application yet, and a caller's <c>Host</c> would choose the token's audience.</exception>
    public static async Task<AccessToken?> For(HttpContext context, ClaimsPrincipal principal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(principal);
        var services = context.RequestServices;

        var claims = SessionPrincipal.ToTrustClaims(principal);
        var captured = new OAuthProtocolEndpoints.GrantedIdentity(claims.Subject, claims.Name, claims.Email, [.. claims.Roles], [.. claims.Scopes]);
        if (await OAuthProtocolEndpoints.Standing(services, captured, ct) is not { } identity) return null;

        var options = services.GetRequiredService<IOptions<AuthServerOptions>>().Value;
        var baseUrl = services.GetRequiredService<KoanWebBaseUrl>();
        var resource = services.GetRequiredService<IOptions<AuthOptions>>().Value.ResourceServer.Resource
            ?? baseUrl.Current
            ?? (KoanEnv.Gate.DevelopmentOnly(services.GetService<IHostEnvironment>())
                ? baseUrl.Resolve(context)
                : throw new InvalidOperationException("A token names the application, and nothing names it yet: set Koan:Web:ApplicationUrl or accept a URL. A caller's Host does not choose an audience outside Development."));
        return new AccessToken(OAuthProtocolEndpoints.Mint(services, options, identity, clientId: null, resource), options.AccessTokenLifetime);
    }
}
