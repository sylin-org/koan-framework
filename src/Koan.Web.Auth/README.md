# Sylin.Koan.Web.Auth

Authentication scaffolding and shared components for Koan Web.

- Target framework: net9.0
- License: Apache-2.0

## Capabilities
- Multi-protocol auth (OIDC/OAuth2)
- Discovery and health integration
- Provider adapters shipped as separate modules

## Install

```powershell
dotnet add package Sylin.Koan.Web.Auth
```

## Usage — quick notes
- Configure providers via typed Options; avoid inline endpoints.
- Use MVC controllers with attribute routing for auth callbacks.

Dev/testing tip
- When using Koan.Web.Auth.TestProvider in Development, its userinfo may include `roles[]`, `permissions[]`, and `claims{}`. These are mapped into the cookie principal (roles → ClaimTypes.Role, permissions → `Koan.permission`, claims{} → 1:1) so you can exercise authorization flows end-to-end.

Sign-out (controller)

```csharp
[ApiController]
[Route("auth")]
public sealed class SignOutController : ControllerBase
{
	[HttpPost("signout")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> SignOutApp([FromForm] string? returnUrl)
	{
		await HttpContext.SignOutAsync();
		// Validate returnUrl before redirecting
		return LocalRedirect(string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl);
	}
}
```

See [`TECHNICAL.md`](TECHNICAL.md) for contracts and configuration.

## References
- Decisions: `/docs/decisions/WEB-0043-auth-multi-protocol-oauth-oidc-saml.md`, `/docs/decisions/WEB-0044-web-auth-discovery-and-health.md`, `/docs/decisions/WEB-0045-auth-provider-adapters-separate-projects.md`
