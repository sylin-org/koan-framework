# Koan.Web.Auth.Connector.Google - Technical reference

Contract

- Inputs: Google ClientId/ClientSecret, redirect URI, scopes; ASP.NET Core auth pipeline.
- Outputs: Auth handler registered (scheme: "Google"); ClaimsPrincipal populated from Google tokens/userinfo.
- Errors: Invalid credentials, redirect mismatch, consent denied, nonce/state mismatch.

Configuration

- Add authentication and controllers; bind `GoogleAuthOptions` from configuration.

Example

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services
    .AddAuthentication(o =>
    {
        o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        o.DefaultChallengeScheme = "Google";
    })
    .AddCookie();

builder.Services.Configure<GoogleAuthOptions>(builder.Configuration.GetSection("Auth:Providers:Google"));
// builder.Services.AddGoogleAuth(); // provided by this module

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
```

Controller endpoints

- Challenge: GET /auth/google/challenge → Challenge("Google")
- Callback: GET /auth/google/callback → Authenticate and sign-in, then redirect

Options

- ClientId, ClientSecret, AuthorizationEndpoint, TokenEndpoint, UserInfoEndpoint, Scope[]
- CallbackPath default: "/auth/google/callback"

Scopes and claims

- Request minimal scopes (e.g., openid, profile, email). Avoid broad Google API scopes unless necessary.
- Claims mapping: sub → NameIdentifier, name → Name, email → Email; check email_verified.

PKCE and token handling

- Use authorization code with PKCE.
- Avoid SaveTokens unless calling Google APIs; store tokens securely and refresh per expires_in.

Cookie/session

- Cookie defaults: HttpOnly, Secure, SameSite=Lax (or None when necessary).

Operations

- Watch for rate limits and consent changes; errors like access_denied and redirect_uri_mismatch point to console settings.

Edge cases

- SameSite cookies on cross-site flows - set Lax/None+Secure as needed.
- OIDC vs pure OAuth: prefer OIDC with PKCE and nonce.

References

- Controllers only: `/docs/decisions/WEB-0035-entitycontroller-transformers.md`
- Per-project docs: `/docs/decisions/ARCH-0042-per-project-companion-docs.md`

