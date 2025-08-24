# Sora.Web.Auth.TestProvider — Technical reference

Contract
- Inputs: None (config knobs for user id/name/roles optional); ASP.NET Core auth pipeline.
- Outputs: Deterministic ClaimsPrincipal under a test scheme (e.g., "Test").
- Errors: Misconfiguration of scheme names if mixed with real providers.

Configuration
- Add authentication and controllers; register the Test provider handler.

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

// builder.Services.AddTestAuth(o => { o.UserId = "dev"; o.Roles = new[] {"Admin"}; });

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

References
- Controllers only: `/docs/decisions/WEB-0035-entitycontroller-transformers.md`
- Per-project docs: `/docs/decisions/ARCH-0042-per-project-companion-docs.md`