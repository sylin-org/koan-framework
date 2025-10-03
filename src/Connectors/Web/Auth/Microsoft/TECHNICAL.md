# Koan.Web.Auth.Connector.Microsoft - Technical reference

Contract

- Inputs: Azure AD/Entra tenant (common/organizations/<tenantId>), ClientId/ClientSecret, redirect URI, scopes.
- Outputs: Auth handler registered (scheme: "Microsoft"); ClaimsPrincipal from ID token/userinfo/Graph (if used).
- Errors: Invalid app registration, redirect mismatch, consent denied, nonce/state mismatch.

Configuration

- Add authentication and controllers; bind `MicrosoftAuthOptions` from configuration.

Example

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services
    .AddAuthentication(o =>
    {
        o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        o.DefaultChallengeScheme = "Microsoft";
    })
    .AddCookie();

builder.Services.Configure<MicrosoftAuthOptions>(builder.Configuration.GetSection("Auth:Providers:Microsoft"));
// builder.Services.AddMicrosoftAuth(); // provided by this module

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
```

Controller endpoints

- Challenge: GET /auth/microsoft/challenge → Challenge("Microsoft")
- Callback: GET /auth/microsoft/callback → Authenticate and sign-in, then redirect

Options

- ClientId, ClientSecret, Tenant, Authority, Scope[], CallbackPath
- Default callback path: "/auth/microsoft/callback"

Scopes and claims

- Use v2 scopes (Microsoft identity platform): openid, profile, email; add offline_access if refresh is required.
- Claims mapping: oid/objectId or sub → NameIdentifier, name → Name, preferred_username/email → Email; app roles → Role.

Multi-tenant

- Tenants: common, organizations, consumers, or a specific tenant ID. Validate iss/tenant in ID token.
- For multi-tenant apps, restrict allowed tenants explicitly and enforce issuer validation.

PKCE and token handling

- Authorization code with PKCE recommended.
- SaveTokens off by default; store and rotate refresh tokens securely if needed.

Cookie/session

- SameSite=Lax (or None+Secure) for redirects; HttpOnly and Secure cookies.

Operations

- Frequent errors: AADSTS50011 (redirect URI mismatch), AADSTS65001 (consent required). Fix app registration and granted permissions.

Edge cases

- Multi-tenant vs single-tenant; set correct Authority/Tenant.
- SameSite cookie settings for cross-site redirects.

References

- Controllers only: `/docs/decisions/WEB-0035-entitycontroller-transformers.md`
- Per-project docs: `/docs/decisions/ARCH-0042-per-project-companion-docs.md`

