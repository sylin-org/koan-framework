# Koan.Web.Auth.Oidc — Technical reference

Contract
- Inputs: Authority, ClientId/ClientSecret, response type (code), scopes, callback path; ASP.NET Core auth pipeline.
- Outputs: OIDC handler registered (scheme configurable, e.g., "oidc"); ClaimsPrincipal from ID token/userinfo.
- Errors: Authority discovery failure, invalid credentials, redirect mismatch, nonce/state mismatch.

Configuration
- Add authentication and controllers; bind `OidcAuthOptions` from configuration.

Example
```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services
    .AddAuthentication(o =>
    {
        o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        o.DefaultChallengeScheme = "oidc";
    })
    .AddCookie();

builder.Services.Configure<OidcAuthOptions>(builder.Configuration.GetSection("Auth:Providers:Oidc"));
// builder.Services.AddOidcAuth(); // provided by this module

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
```

Controller endpoints
- Challenge: GET /auth/oidc/challenge → Challenge("oidc")
- Callback: GET /auth/oidc/callback → Authenticate and sign-in, then redirect

Options
- Authority, ClientId, ClientSecret, ResponseType ("code"), Scope[], CallbackPath

Scopes and claims
- Default OIDC: openid, profile, email; add offline_access only if refresh tokens are required.
- Claims mapping: sub → NameIdentifier, name → Name, email → Email; validate email_verified if used for authz.

PKCE and security
- Enforce PKCE for the authorization code flow.
- Always validate state/nonce; reject if missing/mismatched.

Token handling
- SaveTokens = false by default; enable only when you must call the provider’s APIs.
- If using refresh tokens, store securely server-side and rotate on use; honor expires_in.

Cookie/session
- For interactive apps, use cookie scheme as DefaultScheme and OIDC as DefaultChallengeScheme.
- SameSite Lax or None+Secure for cross-site identity provider redirects.

Multi-tenant
- For multi-tenant scenarios, parameterize Authority per tenant and validate iss/tenant in ID token.

Edge cases
- PKCE recommended; ensure SameSite cookie settings are compatible with redirects.

Operations
- Monitor: challenge count, callback successes/failures, token refresh failures.
- Troubleshoot: 401 after callback usually indicates failed sign-in due to claim mapping or clock skew.

References
- Controllers only: `/docs/decisions/WEB-0035-entitycontroller-transformers.md`
- Per-project docs: `/docs/decisions/ARCH-0042-per-project-companion-docs.md`