using Koan.Web.Auth.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Web.Auth.Connector.Test.Infrastructure;
using Koan.Web.Auth.Connector.Test.Options;
using Koan.Web.Hosting;

namespace Koan.Web.Auth.Connector.Test.Controllers;

/// <summary>
/// OIDC IdP surface for the dev <c>test-oidc</c> provider (WEB-0071 / E5 chunk 4): the discovery document and
/// the JWKS (the ES256 public key from <see cref="JwtTokenService"/>). The maintained OpenIdConnectHandler
/// fetches these to validate the signed <c>id_token</c> the token endpoint mints. The issuer is the application's
/// base URL (<see cref="KoanWebBaseUrl"/>, WEB-0078), which is what the minted <c>id_token</c> carries; the
/// endpoints are derived from the request that fetched this document — the handler's back-channel address — under
/// its path base, so the handler reaches them wherever it reached the document.
/// </summary>
[ResourceServerExempt] // SEC-0025: a provider endpoint answering its own token, not the application's resource.
public sealed class OidcDiscoveryController(IOptionsSnapshot<TestProviderOptions> opts, JwtTokenService jwt, IHostEnvironment env, KoanWebBaseUrl baseUrl) : ControllerBase
{
    [HttpGet(Constants.Routes.Discovery)]
    public IActionResult Discovery()
    {
        if (!opts.Value.IsActive(env)) return NotFound();
        var issuer = baseUrl.Resolve(Request) + Constants.Routes.Base;
        var reached = $"{Request.Scheme}://{Request.Host}{Request.PathBase}{Constants.Routes.Base}";
        return Ok(new
        {
            issuer,
            authorization_endpoint = $"{reached}/authorize",
            token_endpoint = $"{reached}/token",
            userinfo_endpoint = $"{reached}/userinfo",
            jwks_uri = $"{reached}/jwks",
            response_types_supported = new[] { "code" },
            subject_types_supported = new[] { "public" },
            id_token_signing_alg_values_supported = new[] { "ES256" },
            scopes_supported = new[] { "openid", "profile", "email" },
            grant_types_supported = new[] { "authorization_code" },
            token_endpoint_auth_methods_supported = new[] { "client_secret_post", "none" },
            code_challenge_methods_supported = new[] { "S256" }
        });
    }

    [HttpGet(Constants.Routes.Jwks)]
    public IActionResult Jwks()
    {
        if (!opts.Value.IsActive(env)) return NotFound();
        var jwk = jwt.GetPublicJwk();
        // Project to explicit lower-case JWK members (Newtonsoft would otherwise PascalCase the JsonWebKey).
        return Ok(new { keys = new[] { new { kty = jwk.Kty, use = jwk.Use, alg = jwk.Alg, kid = jwk.Kid, crv = jwk.Crv, x = jwk.X, y = jwk.Y } } });
    }

}
