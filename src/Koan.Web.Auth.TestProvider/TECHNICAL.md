# Koan.Web.Auth.TestProvider - Technical reference

Contract

- Inputs: Optional query/UX-provided roles, permissions, and claims; ASP.NET Core auth pipeline.
- Outputs: Deterministic ClaimsPrincipal under a test scheme (e.g., "Test"); userinfo contains roles/permissions/claims for dev mapping.
- Errors: Misconfiguration of scheme names if mixed with real providers.

Configuration

- Add authentication and controllers; register the Test provider handler.
- Dev login UI is served at `/.testoauth/login.html` to keep controller code clean.

Example

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services
    .AddAuthentication(o =>
    {
        o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        o.DefaultChallengeScheme = "Test";
    })
    .AddCookie();

// Extras via query: roles=admin,author&perms=content:write&claim.department=ENG
// Or use the UI at /.testoauth/login.html (persists persona in LocalStorage).

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
```

Edge cases

- Ensure this provider is excluded from production builds/deployments.

Operations

- Gate this provider behind environment checks; never enable in production.
- Add explicit health logging to prevent accidental reliance during tests.

Claims mapping

- UserInfo JSON may include `roles[]`, `permissions[]`, and `claims{}`; AuthController maps these into the cookie principal.
- Roles -> ClaimTypes.Role; Permissions -> `Koan.permission`; Claims{} -> 1:1 (string or multi-value).

References

- Controllers only: `/docs/decisions/WEB-0035-entitycontroller-transformers.md`
- Per-project docs: `/docs/decisions/ARCH-0042-per-project-companion-docs.md`
