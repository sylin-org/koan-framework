# Koan.Web.Auth - Technical reference

Contract

- Inputs: ASP.NET Core app with MVC; provider configuration via typed Options; cookies or bearer as the primary auth scheme.
- Outputs: Registered authentication handlers; controller-driven challenge/callback endpoints; enriched ClaimsPrincipal.
- Error modes: Invalid state/nonce, provider errors, clock skew, misconfigured redirect URIs.
- Success: Provider challenge redirects and callback executed; user principal established; auth cookies/tokens issued per policy.

Architecture and flow

- Controllers, not inline endpoints: Expose challenge and callback via attribute-routed controllers for testability.
- Providers are separate modules (e.g., Koan.Web.Auth.Oidc, Discord, Google). This package supplies shared primitives and policies.
- Options-centric: Bind provider options from configuration; no magic strings in code-use constants/options.

Schemes and cookies

- Default scheme: Prefer Cookie for interactive web apps; use Bearer/JWT for APIs.
- Challenge scheme: Set per-provider (e.g., "oidc", "Google", "Microsoft", "Discord").
- Cookie policy: Lax or None+Secure for cross-site redirects; HttpOnly; short-lived auth cookies with sliding expiration per policy.

Configuration

- Register authentication and MVC in Program.cs and bind options from configuration.
- Prefer AddControllers(); avoid MapGet/MapPost inline endpoints.

Example (minimal, production-safe)
// Program.cs
// services
// - Cookie or bearer default scheme
// - Controllers registered
// - Provider modules contribute handlers via their own Add\* extensions

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = "Discord"; // or OIDC/Google per provider
    })
    .AddCookie();

// Provider modules add their handlers in their own startup wiring.

builder.Services.Configure<CookiePolicyOptions>(o =>
{
    o.MinimumSameSitePolicy = SameSiteMode.Lax;
});

var app = builder.Build();

app.UseCookiePolicy();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
```

Callback controller sketch

```csharp
[ApiController]
[Route("auth/{provider}")]
public sealed class AuthController : ControllerBase
{
    [HttpGet("challenge")]
    public IActionResult ChallengeProvider([FromRoute] string provider, [FromQuery] string? returnUrl)
        => Challenge(new AuthenticationProperties { RedirectUri = Url.Action("Callback", new { provider, returnUrl }) }, provider);

    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromRoute] string provider, [FromQuery] string? returnUrl)
    {
        var result = await HttpContext.AuthenticateAsync();
        if (!result.Succeeded) return Forbid();
        return LocalRedirect(returnUrl ?? "/");
    }
}
```

Options and constants

- Bind provider-specific options under a clear prefix (e.g., Auth:Providers:Discord).
- Keep stable names (scheme names, header keys, default paths) in a central constants class in the project.

Scopes and claims

- Request only the scopes you need (least privilege). Typical OIDC scopes: openid, profile, email, offline_access.
- Claims mapping: Normalize common identifiers (sub/oid → NameIdentifier), email, name, roles. Prefer explicit claim type constants.

Token lifetimes and session

- Prefer server-side sessions (cookies) and do not store long-lived tokens in the browser.
- SaveTokens only when you need to call provider APIs; encrypt at rest and restrict access.
- Respect provider token lifetimes and refresh cadence; handle refresh failure by re-challenging the user.

PKCE, state, nonce

- Always enable state and nonce; reject callbacks with mismatches.
- Use PKCE for OAuth/OIDC code flows; enforce on all public clients.

Logout and sign-out

- Support local sign-out (delete auth cookie) and, if applicable, federated sign-out at the provider.
- Expose sign-out via a controller route; avoid inline endpoints. Validate return URLs.

Multi-tenant and callback URLs

- Ensure RedirectUri/CallbackPath is consistent between app settings and provider console.
- For multi-tenant providers, encode tenant selection in configuration (e.g., OIDC authority per tenant) and validate issuer.

Edge cases

- Callback path mismatches; fix RedirectUri and Allowed Callback URLs in the provider console.
- SameSite/cookie issues on cross-site redirects; set Lax/None with Secure when required.
- Clock skew on token validation; adjust token validation parameters appropriately.

Security

- Always enable state/nonce/PKCE where applicable.
- Limit scopes to least privilege; validate issuer/audience for OIDC.

Development provider mapping

- In Development, the TestProvider's userinfo may include `roles`, `permissions`, and a `claims` object. The callback maps these into the cookie principal to simulate downstream authorization (e.g., Koan.Web.Auth.Roles). This behavior is confined to dev/test providers.

Operations

- Metrics: count challenges, successful sign-ins, failed callbacks (by reason), and sign-outs.
- Logging: include correlation IDs spanning challenge→callback; never log tokens or PII.
- Runbook: on repeated provider errors (invalid_client, invalid_grant), recycle secrets, validate redirect URIs, and check clock skew.

References

- Controllers only (no inline endpoints): `/docs/decisions/WEB-0035-entitycontroller-transformers.md`
- Config and constants: `/docs/decisions/ARCH-0040-config-and-constants-naming.md`
- Per-project docs pattern: `/docs/decisions/ARCH-0042-per-project-companion-docs.md`
