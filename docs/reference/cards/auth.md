---
type: REF
domain: web
title: "Auth — pillar map"
audience: [developers, ai-agents]
status: current
last_updated: 2026-06-18
framework_version: v0.17.0
validation:
  date_last_tested: 2026-06-18
  status: verified
  scope: docs/reference/cards/auth.md
---

# Auth — pillar map

> One-screen map of the Auth pillar — external sign-in and authorization on top of the [Web](web.md) pillar. Full detail: [authentication-setup.md](../../guides/authentication-setup.md).

**What it does** — Cookie-session sign-in through external OAuth2 / OIDC providers, then `[Authorize]`-gated APIs. Reference an auth connector package (`Koan.Web.Auth.Connector.Google` / `.Microsoft` / `.Discord`) and its provider self-registers; config supplies the per-provider `ClientId` / `ClientSecret`. The flow itself rides the **maintained ASP.NET `OAuthHandler` / `OpenIdConnectHandler`** (PKCE, nonce, state, id_token validation owned by the framework), bound at startup via dynamic scheme registration — Koan stopped hand-rolling the OAuth2/OIDC exchange ([WEB-0071](../../decisions/WEB-0071-auth-engine-swap-dynamic-schemes.md)).

## The one canonical pattern

Reference a connector, supply config, then `[Authorize]` your controllers and override the `Can*` gates on `EntityController<T>` to authorize per-operation.

```jsonc
// appsettings.json — Reference = Intent: the Google connector reference makes "google" a provider; config supplies its secret.
"Koan": { "Web": { "Auth": { "Providers": {
  "google": { "ClientId": "{GOOGLE_CLIENT_ID}", "ClientSecret": "{GOOGLE_CLIENT_SECRET}" }
} } } }
```

```csharp
[ApiController, Route("api/library")]
public sealed class LibraryController : ControllerBase
{
    [Authorize]                                  // any signed-in user
    [HttpPut("by-me/{id}")]
    public Task<IActionResult> UpdateMine(string id) { /* ... */ }
}

// Per-operation gate on the entity REST surface:
public sealed class AdminTodoController : EntityController<Todo>
{
    protected override bool CanRemove => User.IsInRole("admin");   // 403 on DELETE otherwise
}
```

Provider roles map onto `ClaimTypes.Role`, so `[Authorize(Roles = "admin")]` works once a provider returns roles.

## ≤5 attributes you'll use

| Attribute | What it does |
|---|---|
| `[Authorize]` | ASP.NET gate — require any authenticated user on a controller / action. |
| `[Authorize(Roles = "admin")]` | ASP.NET gate — require the caller to hold a mapped role claim. |
| `[AuthProviderDescriptor("id", "Name", "OIDC")]` | Assembly-level (connector authors) — declares a provider's id, display name, and protocol for discovery. |

`CanRead` / `CanWrite` / `CanRemove` are `protected virtual bool` *overrides* on `EntityController<T>`, not attributes — they gate the generated CRUD surface (see the canonical pattern).

## The escape hatch

When no first-party connector fits, reference the generic **`Koan.Web.Auth.Connector.Oidc`** package (disabled by default — `Defaults.Enabled = false`, it ships handler wiring with no provider defaults). Add a provider entry under `Koan:Web:Auth:Providers` with its `Authority` / `ClientId` / `ClientSecret` and the dynamic-scheme seeder wires a real `OpenIdConnectHandler` for it. To contribute provider defaults from your own package instead, implement **`IAuthProviderContributor`** (`Koan.Web.Auth.Providers`) and return `ProviderOptions` keyed by id — the same seam the built-in connectors use.

## The sample that shows it

[`samples/S5.Recs`](../../../samples/S5.Recs/README.md) — centralized provider discovery, cookie-session challenge/callback login, and `[Authorize]`-gated, claim-scoped controllers over a real app.
