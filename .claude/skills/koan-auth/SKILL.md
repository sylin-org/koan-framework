---
name: koan-auth
description: External sign-in and authorization for Koan web apps — OAuth2/OIDC connector packages (Reference = Intent), the AuthController challenge/callback flow, [Authorize]/[Authorize(Roles=)] gates, EntityController Can* overrides, dynamic scheme registration (WEB-0071), and Security.Trust inbound bearer token validation
pillar: web
card: docs/reference/cards/auth.md
status: current
last_validated: 2026-06-18
---

# Koan Auth

## Trigger this skill when you see

- `[Authorize]`, `[Authorize(Roles = "admin")]`, `[Authorize(Policy = "...")]` on a Koan controller / action
- `CanRead` / `CanWrite` / `CanRemove` overrides on `EntityController<T>` (per-operation gates)
- Provider config under `Koan:Web:Auth:Providers` (`ClientId` / `ClientSecret` / `Authority`)
- A `Koan.Web.Auth.Connector.Google` / `.Microsoft` / `.Discord` / `.Oidc` package reference
- `[AuthProviderDescriptor("id", "Name", "OIDC")]` (assembly-level) or `IAuthProviderContributor` / `ProviderOptions`
- `AuthController` challenge/callback, `/auth/{provider}/challenge`, `/auth/logout`, cookie-session login
- `IProviderRegistry.EffectiveProviders`, `AuthSchemeSeeder`, dynamic scheme registration (WEB-0071)
- `KoanBearerDefaults.AuthenticationScheme`, `AddKoanBearer`, inbound KSVID / bearer-token validation (`Koan.Security.Trust`)
- "sign in with Google", "OAuth", "OIDC", "external login", "roles", "claims", "authorize", "401/403", "trust fabric"

## Core principle

**Reference = Intent for identity.** Add an auth connector package (e.g. `Koan.Web.Auth.Connector.Google`) and its provider self-registers via an assembly-level `[AuthProviderDescriptor]`; config under `Koan:Web:Auth:Providers` supplies only the per-provider `ClientId` / `ClientSecret`. The OAuth2/OIDC exchange itself rides the **maintained ASP.NET `OAuthHandler` / `OpenIdConnectHandler`** — PKCE, nonce, state, and `id_token` validation are owned by the framework, bound at startup by `AuthSchemeSeeder` via **dynamic scheme registration** ([WEB-0071](../../../docs/decisions/WEB-0071-auth-engine-swap-dynamic-schemes.md)). Koan stopped hand-rolling the exchange. You then gate APIs with the stock ASP.NET `[Authorize]` and override the `Can*` gates on `EntityController<T>` to authorize per CRUD operation. Provider roles land on `ClaimTypes.Role`, so `[Authorize(Roles = "admin")]` and `User.IsInRole("admin")` work once a provider returns roles.

<!-- validate -->
```csharp
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Koan.Data.Core.Model;
using Koan.Web.Controllers;

public sealed class Todo : Entity<Todo>      // string-keyed aggregate; REST surface is generated
{
    public string Title { get; set; } = "";
    public bool Done { get; set; }
}

[ApiController, Route("api/library")]
public sealed class LibraryController : ControllerBase
{
    [Authorize]                                   // any signed-in user (cookie session)
    [HttpPut("by-me/{id}")]
    public Task<IActionResult> UpdateMine(string id) => Task.FromResult<IActionResult>(NoContent());

    [Authorize(Roles = "admin")]                  // requires the mapped ClaimTypes.Role = "admin"
    [HttpDelete("{id}")]
    public Task<IActionResult> Purge(string id) => Task.FromResult<IActionResult>(NoContent());
}

// Per-operation gate on the generated Entity<T> CRUD surface — Can* are overrides, NOT attributes:
public sealed class AdminTodoController : EntityController<Todo>
{
    protected override bool CanRemove => User.IsInRole("admin");  // 403 on DELETE otherwise
    protected override bool CanWrite  => User.Identity?.IsAuthenticated == true;
}
```

```jsonc
// appsettings.json — the Google connector reference makes "google" a provider; config supplies its secret.
"Koan": { "Web": { "Auth": { "Providers": {
  "google": { "ClientId": "{GOOGLE_CLIENT_ID}", "ClientSecret": "{GOOGLE_CLIENT_SECRET}" }
} } } }
```

Sign-in is a cookie-session flow: the browser hits `GET /auth/{provider}/challenge?return=/app`, the seeded handler runs the OAuth2/OIDC dance and intercepts `/auth/{provider}/callback`, and `GET|POST /auth/logout` clears the cookie. `AuthController` only issues the framework challenge after the return-url allow-list runs (SEC-0001) — there is no callback action to write.

## Reference = Intent activation

| Add this reference | Effect |
|---|---|
| `Koan.Web.Auth` | The auth pillar: `AuthController`, `IProviderRegistry`, `AuthSchemeSeeder`, cookie scheme, `/me`. Wired by `AddKoanWebAuth()` (registrar-driven — Reference = Intent). |
| `+ Koan.Web.Auth.Connector.Google` | `[AuthProviderDescriptor("google", "Google", "OIDC")]` self-registers; `GoogleProviderContributor : IAuthProviderContributor` supplies the `Authority` + scope defaults. Config adds the secret. |
| `+ Koan.Web.Auth.Connector.Microsoft` / `.Discord` | Same shape for Microsoft (OIDC) / Discord (OAuth2). |
| `+ Koan.Web.Auth.Connector.Oidc` | **Generic** OIDC — disabled by default (`Defaults.Enabled = false`); supply `Authority` + `ClientId` + `ClientSecret` per provider and the seeder wires a real `OpenIdConnectHandler`. The escape hatch. |
| `+ Koan.Security.Trust` | Inbound trust fabric: opt-in bearer scheme `KoanBearerDefaults.AuthenticationScheme` (`"Koan.bearer"`) validates KSVID JWTs against the issuer key (ES256-pinned). |

The cookie scheme stays the **default**; bearer is additive and opted into per endpoint with `[Authorize(AuthenticationSchemes = KoanBearerDefaults.AuthenticationScheme)]`.

## Anti-patterns to flag

| If you see | Suggest |
|---|---|
| Hand-rolling the OAuth2/OIDC authorize+token exchange (building authorize URLs, parsing `id_token`) | Reference a connector — the maintained ASP.NET handler does PKCE/nonce/state/`id_token` validation (WEB-0071). |
| `services.AddAuthentication().AddGoogle(...)` wired manually in `Program.cs` | Reference `Koan.Web.Auth.Connector.Google`; the descriptor + contributor self-register, config supplies the secret. |
| `[Cacheable]`-style attribute hunt for "CanRead/CanWrite/CanRemove attributes" | They are `protected virtual bool` **overrides** on `EntityController<T>`, not attributes. |
| `if (user.Role != "admin") return Forbid();` scattered in actions | `[Authorize(Roles = "admin")]` (action) or a `CanRemove` override (CRUD surface). |
| A custom callback action handling the OAuth redirect | The handler intercepts `/auth/{provider}/callback` as RemoteAuthentication middleware — there is no callback action to write. |
| Reading `returnUrl` and redirecting without validation | The challenge resolves it through the SEC-0001 allow-list (`Koan:Web:Auth:ReturnUrl:AllowList`) before handing it to the handler. |
| Hand-validating inbound JWTs (manual `JwtSecurityTokenHandler`) | `Koan.Security.Trust` + `[Authorize(AuthenticationSchemes = KoanBearerDefaults.AuthenticationScheme)]` — validation params come from the issuer. |

## Escape hatches

- **No first-party connector fits** → reference `Koan.Web.Auth.Connector.Oidc` (generic, disabled-by-default) and add a provider entry under `Koan:Web:Auth:Providers` with `Authority` / `ClientId` / `ClientSecret`; `AuthSchemeSeeder` wires an `OpenIdConnectHandler` for it.
- **Contribute provider defaults from your own package** → implement `IAuthProviderContributor` (`Koan.Web.Auth.Providers`) returning `IReadOnlyDictionary<string, ProviderOptions>` keyed by id (use `AuthProviderDefaults.Oidc(...)`), and declare `[assembly: AuthProviderDescriptor("id", "Name", "OIDC")]`. This is the exact seam the built-in connectors use.
- **Production gating** — dynamic provider defaults (contributors/adapters) are off in Production unless `Koan:Web:Auth:AllowDynamicProvidersInProduction = true`. Explicit config providers are always honored.
- **Inspect/iterate providers at runtime** → inject `IProviderRegistry`; `EffectiveProviders` is the merged (defaults ⊕ config) id→`ProviderOptions` map, `GetDescriptors()` the discovery list (also exposed at `/.well-known/auth/providers`).
- **Inbound fleet identity** → `Koan.Security.Trust` registers the bearer scheme via `AddKoanBearer()`; protect token endpoints with `[Authorize(AuthenticationSchemes = KoanBearerDefaults.AuthenticationScheme)]` so the bearer 401 fires while cookie redirects stay intact.

## See also

- [Authentication setup](../../../docs/guides/authentication-setup.md) — provider config, the challenge/callback flow, full detail
- [Auth how-to](../../../docs/guides/auth-howto.md) — connector walkthrough
- [Authorization how-to](../../../docs/guides/authorization-howto.md) — `[Authorize]`, roles, and the `Can*` gates
- [Reference card: auth.md](../../../docs/reference/cards/auth.md) — one-screen pillar map
- [WEB-0071 — auth engine swap to dynamic schemes](../../../docs/decisions/WEB-0071-auth-engine-swap-dynamic-schemes.md)
- [`samples/S5.Recs`](../../../samples/S5.Recs/README.md) — provider discovery, cookie-session login, `[Authorize]`-gated claim-scoped controllers
