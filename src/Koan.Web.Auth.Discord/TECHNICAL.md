# Koan.Web.Auth.Discord — Technical reference

Contract
- Inputs: Discord application credentials (ClientId, ClientSecret), redirect URI, scopes; ASP.NET Core auth pipeline.
- Outputs: Auth handler registered under scheme name "Discord"; ClaimsPrincipal with Discord user info.
- Errors: Invalid client/secret, redirect URI mismatch, denied consent, nonce/state mismatch.

Configuration
- In Program.cs, add authentication (cookie/bearer) and controllers.
- Bind `DiscordOptions` from configuration and register the Discord handler via the module’s service extension.

Example

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services
    .AddAuthentication(o =>
    {
        o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        o.DefaultChallengeScheme = "Discord";
    })
    .AddCookie();

// Assuming the provider exposes an AddDiscordAuth extension
builder.Services.Configure<DiscordAuthOptions>(builder.Configuration.GetSection("Auth:Providers:Discord"));
// builder.Services.AddDiscordAuth(); // provided by this module

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
```

Controller endpoints
- Challenge: GET /auth/discord/challenge → Challenge("Discord")
- Callback: GET /auth/discord/callback → Authenticate and sign-in, then redirect.

Options
- ClientId, ClientSecret, AuthorizationEndpoint, TokenEndpoint, UserInformationEndpoint, Scope[]
- CallbackPath default: "/auth/discord/callback"

Scopes and claims
- Minimal scopes: identify and email (if needed). Add guilds/guilds.members.read only for guild-aware features.
- Claims mapping: id → NameIdentifier, username/discriminator (or global name) → Name, email → Email when available.

PKCE and token handling
- Prefer authorization code with PKCE.
- SaveTokens only if you call Discord APIs; store securely; respect token expiry.

Cookie/session
- Cookie policy must support Discord’s cross-site redirects: use SameSite=Lax or None+Secure.

Operations
- Common failures: invalid_scope, invalid_client, redirect_uri_mismatch. Verify application portal settings.

Edge cases
- Missing guild/member scopes for guild-protected resources.
- SameSite cookie behavior on cross-site flows; prefer Lax or None+Secure.

References
- Controllers only: `/docs/decisions/WEB-0035-entitycontroller-transformers.md`
- Per-project docs: `/docs/decisions/ARCH-0042-per-project-companion-docs.md`