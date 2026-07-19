using Koan.Security.Trust.Issuer;
using Koan.Web.Auth.Server.Infrastructure;
using Koan.Web.Auth.Server.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Web.Auth.Server.Protocol;

/// <summary>
/// SEC-0006 Phase 3 — discovery. RFC 8414 Authorization-Server metadata (+ an OIDC-form mirror for clients that
/// only probe <c>/.well-known/openid-configuration</c>) and the public ES256 JWKS. All derived from the live
/// request host so the advertised endpoints match where the client actually reached the AS.
/// </summary>
internal static class WellKnownEndpoints
{
    internal static Task Metadata(HttpContext ctx)
    {
        var options = ctx.RequestServices.GetRequiredService<IOptions<AuthServerOptions>>().Value;
        var root = RequestHost.Root(ctx);

        var grantTypes = new List<string> { "authorization_code", "urn:ietf:params:oauth:grant-type:device_code" };
        if (options.EnableRefreshTokens) grantTypes.Add("refresh_token");

        var doc = new Dictionary<string, object?>
        {
            ["issuer"] = root,
            ["authorization_endpoint"] = root + AuthServerRoutes.Authorize,
            ["token_endpoint"] = root + AuthServerRoutes.Token,
            ["device_authorization_endpoint"] = root + AuthServerRoutes.Device,
            ["jwks_uri"] = root + AuthServerRoutes.Jwks,
            ["response_types_supported"] = new[] { "code" },
            ["grant_types_supported"] = grantTypes.ToArray(),
            ["code_challenge_methods_supported"] = new[] { Pkce.MethodS256 },
            ["token_endpoint_auth_methods_supported"] = new[] { "none" },
        };
        if (options.AllowDynamicRegistration)
            doc["registration_endpoint"] = root + AuthServerRoutes.Register;

        return ctx.Response.WriteAsJsonAsync(doc, cancellationToken: ctx.RequestAborted);
    }

    internal static Task Jwks(HttpContext ctx)
    {
        var issuer = ctx.RequestServices.GetRequiredService<IIssuer>();
        ctx.Response.Headers.CacheControl = "public, max-age=3600"; // cacheable; rotation keeps the retiring key published
        // Project to explicit lower-case JWK members (the AS signs ES256 / P-256; public coordinates only).
        var keys = issuer.PublishedKeys.Select(k => new
        {
            kty = k.Kty,
            use = k.Use,
            alg = k.Alg,
            kid = k.Kid,
            crv = k.Crv,
            x = k.X,
            y = k.Y,
        }).ToArray();
        return ctx.Response.WriteAsJsonAsync(new { keys }, cancellationToken: ctx.RequestAborted);
    }
}
