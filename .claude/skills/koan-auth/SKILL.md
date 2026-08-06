---
name: koan-auth
description: External sign-in and authorization for Koan web apps — OAuth2/OIDC connector packages (Reference = Intent), the AuthController challenge/callback flow, [Authorize]/[Authorize(Roles=)] gates, the entity capability authorization model gate·constrain·project ([Access] per-action gate + EntityAccess<T> row Constrain + the can:[] projection, enforced cross-surface by the IAuthorize seam — SEC-0004/ARCH-0092), dynamic scheme registration (WEB-0071), and Security.Trust inbound bearer token validation
pillar: web
card: docs/reference/identity/index.md
status: current
last_validated: 2026-07-22
---

# Koan Auth

## Trigger this skill when you see

- `[Authorize]`, `[Authorize(Roles = "admin")]`, `[Authorize(Policy = "...")]` on a Koan controller / action
- The **entity capability model — gate · constrain · project** (SEC-0004): `[Access(read:, write:, remove:)]` per-action gate on `Entity<T>` (legacy `[Authorize]` / `[AllowAnonymous]` / `[RequireScope]` lower into it as sugar), an `EntityAccess<T>` realization for row-level `Owner` + `Constrain`, and the `can:[]` projection — all enforced on every surface (REST + MCP) by the unified `IAuthorize` seam (ARCH-0092)
- `EntityAccess<T>`, `[Access(...)]`, `Constrain(q, action)`, `Owner`, `q.Where(...)` / `q.Stamp(...)`, the `Koan-Access` header, `?access=true`, `can:[...]`
- Provider config under `Koan:Web:Auth:Providers` (`ClientId` / `ClientSecret` / `Authority`)
- A `Sylin.Koan.Web.Auth.Connector.Google` / `.Microsoft` / `.Discord` package reference
- `[AuthProviderDescriptor("id", "Name", "OIDC")]` (assembly-level) or `IAuthProviderContributor` / `ProviderOptions`
- `AuthController` challenge/callback, `/auth/{provider}/challenge`, `/auth/logout`, cookie-session login
- `IProviderRegistry.EffectiveProviders`, `AuthSchemeSeeder`, dynamic scheme registration (WEB-0071)
- `KoanBearerDefaults.AuthenticationScheme`, `AddKoanBearer`, inbound KSVID / bearer-token validation (`Koan.Security.Trust`)
- "sign in with Google", "OAuth", "OIDC", "external login", "roles", "claims", "authorize", "401/403", "trust fabric"

## Core principle

**Reference = Intent for identity.** Add an auth connector package (for example, `Sylin.Koan.Web.Auth.Connector.Google`) and `AddKoan()` composes its provider; config under `Koan:Web:Auth:Providers` supplies the per-provider `ClientId` / `ClientSecret`. The OAuth2/OIDC exchange itself rides the **maintained ASP.NET `OAuthHandler` / `OpenIdConnectHandler`** — PKCE, nonce, state, and `id_token` validation are owned by the framework, bound at startup by `AuthSchemeSeeder` via **dynamic scheme registration** ([WEB-0071](../../../docs/decisions/WEB-0071-auth-engine-swap-dynamic-schemes.md)). Koan stopped hand-rolling the exchange. You gate custom actions with the stock ASP.NET `[Authorize]`. Provider roles land on `ClaimTypes.Role`, so `[Authorize(Roles = "admin")]` and `User.IsInRole("admin")` work once a provider returns roles.

**Authorize the entity surface with gate · constrain · project** ([SEC-0004](../../../docs/decisions/SEC-0004-capability-authorization-gate-constrain-project.md)) — one model, enforced identically on REST **and** MCP through the one `IAuthorize` seam ([ARCH-0092](../../../docs/decisions/ARCH-0092-entity-exposure-surfaces.md)), composable from nothing up:

- **Gate** — *who may touch this entity at all, per action.* `[Access(read: "anyone", write: "is:member", remove: "is:admin")]` on the entity. Allow-by-default: an unspecified action is open. Legacy `[Authorize]` / `[AllowAnonymous]` / `[RequireScope]` still work — they lower into the same gate as sugar.
- **Constrain** — *which rows, per action.* Derive an `EntityAccess<T>` realization, declare `Owner` ONCE, and `Constrain(q, action)` transforms the query: the same one-liner filters the collection, 404s an out-of-scope fetch, bounds a mass delete, and **stamps** the owner on create (server-truth, so a forged owner id cannot escalate).
- **Project** — *what you may actually do, per item.* Every response advertises the realized `can:[...]` verbs (the honesty counterweight to the open default): a single item → the `Koan-Access: read, write` header; a collection → an opt-in `{ items, access }` sidecar (`?access=true`); MCP → per-item `can` in the tool metadata, default-on.

<!-- validate -->
```csharp
using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Koan.Data.Core.Model;
using Koan.Web.Authorization;

// GATE — per-action, on the entity. Enforced identically on REST + MCP. (Legacy [Authorize]/[RequireScope]
// still lower into this same gate as sugar; [Access] adds the per-action grain the entity-wide floor lacked.)
[Access(read: "anyone", write: "authenticated", remove: "is:admin")]
public sealed class Ledger : Entity<Ledger>
{
    public string Entry { get; set; } = "";
}

// CONSTRAIN — an owned entity. Owner is declared ONCE and drives the collection filter, the out-of-scope 404,
// mass-delete bounding, AND the create-stamp / freeze-on-update. No row-level rule is hand-repeated.
public sealed class Memo : Entity<Memo>
{
    public string? OwnerId { get; set; }
    public string Text { get; set; } = "";
}

public sealed class MemoAccess : EntityAccess<Memo>
{
    protected override Expression<Func<Memo, bool>>? Owner => m => m.OwnerId == CurrentUserId;

    public override IAccessFilter<Memo> Constrain(IAccessFilter<Memo> q, AccessAction action) => action switch
    {
        AccessAction.Create => q.Stamp(m => m.OwnerId, CurrentUserId),                  // server-truth owner
        AccessAction.Update => q.Where(Owner!).Stamp(m => m.OwnerId, CurrentUserId),    // own rows; freeze owner
        _ => q.Where(Owner!),                                                           // read/delete: own rows
    };
}
// PROJECT is automatic: each response carries the realized can:[] (header / ?access=true sidecar / MCP metadata).

// Custom (non-entity) actions still gate with the stock ASP.NET [Authorize] (cookie session / roles).
[ApiController, Route("api/reports")]
public sealed class ReportsController : ControllerBase
{
    [Authorize(Roles = "admin")]
    [HttpPost("rebuild")]
    public Task<IActionResult> Rebuild() => Task.FromResult<IActionResult>(NoContent());
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
| `Sylin.Koan.Web.Auth` | The auth pillar: `AuthController`, `IProviderRegistry`, `AuthSchemeSeeder`, cookie scheme, and `/me`. Its `KoanModule` participates through the ordinary `AddKoan()` composition. |
| `+ Sylin.Koan.Web.Auth.Connector.Google` | `[AuthProviderDescriptor("google", "Google", "OIDC")]` contributes Google defaults; config adds the secret. |
| `+ Sylin.Koan.Web.Auth.Connector.Microsoft` / `.Discord` | Same shape for Microsoft (OIDC) / Discord (OAuth2). |
| `+ Sylin.Koan.Security.Trust` | Inbound trust fabric: the opt-in bearer scheme `"Koan.bearer"` validates the supported issuer contracts and fails closed when required key material is absent. |
| `+ Sylin.Koan.Web.Auth.Server` | The embedded **OAuth 2.1 Authorization Server** at `/oauth/…`: Authorization Code + PKCE-S256, device grant, DCR, rotating refresh tokens, discovery, and JWKS. It issues tokens to clients; it is not an external login provider. See [oauth-server-howto.md](../../../docs/guides/oauth-server-howto.md). |

The cookie scheme stays the **default**; bearer is additive and opted into per endpoint with `[Authorize(AuthenticationSchemes = KoanBearerDefaults.AuthenticationScheme)]`.

## Anti-patterns to flag

| If you see | Suggest |
|---|---|
| Hand-rolling the OAuth2/OIDC authorize+token exchange (building authorize URLs, parsing `id_token`) | Reference a connector — the maintained ASP.NET handler does PKCE/nonce/state/`id_token` validation (WEB-0071). |
| `services.AddAuthentication().AddGoogle(...)` wired manually in `Program.cs` | Reference `Sylin.Koan.Web.Auth.Connector.Google`; `AddKoan()` composes it and config supplies the secret. |
| Hunting for `CanRead/CanWrite/CanRemove` overrides on `EntityController<T>` | They were removed (ARCH-0092). Declare the **gate** on the entity: `[Access(read:, write:, remove:)]` — per-action, enforced on REST + MCP. |
| `if (user.Role != "admin") return Forbid();` scattered in actions | `[Access(remove: "is:admin")]` on the entity (per-action gate, enforced on every surface). |
| Hand-writing a `Where(o => o.OwnerId == me)` row filter in every read/update/delete | Declare `Owner` once on an `EntityAccess<T>` realization and `Constrain(q, action)` — the rule IS the filter (collection + 404 + mass-delete bound + create-stamp all derive from it). |
| A `Where` on create to scope ownership | A `Where` on create is a silent no-op (no row yet). **Stamp** instead: `q.Stamp(o => o.OwnerId, CurrentUserId)` overwrites a forged owner with server-truth. |
| Hand-building a per-row "can the user edit this?" map for the UI/agent | It is already projected — read the `Koan-Access` header (single), the `?access=true` `{ items, access }` sidecar (collection), or the MCP tool `can` metadata (default-on). |
| A custom callback action handling the OAuth redirect | The handler intercepts `/auth/{provider}/callback` as RemoteAuthentication middleware — there is no callback action to write. |
| Reading `returnUrl` and redirecting without validation | The challenge resolves it through the SEC-0001 allow-list (`Koan:Web:Auth:ReturnUrl:AllowList`) before handing it to the handler. |
| Hand-validating inbound JWTs (manual `JwtSecurityTokenHandler`) | `Koan.Security.Trust` + `[Authorize(AuthenticationSchemes = KoanBearerDefaults.AuthenticationScheme)]` — validation params come from the issuer. |
| Hand-rolling an OAuth `/authorize`+`/token` endpoint / token issuance / a consent-protocol to give an MCP client a token | Reference `Koan.Web.Auth.Server` — the framework owns the OAuth protocol (Authorization Code + PKCE, device, DCR, refresh, discovery). The app owns only the consent + done render pages ([SEC-0006](../../../docs/decisions/SEC-0006-embedded-oauth-authorization-server.md)). |

## Escape hatches

- **No first-party connector fits** → keep ordinary ASP.NET Core authentication for that application boundary, or author a provider contribution package against the documented Auth contracts. Do not invent a package ID or call Koan's internal registration methods.
- **Contribute provider defaults from your own package** → implement `IAuthProviderContributor` (`Koan.Web.Auth.Providers`) returning `IReadOnlyDictionary<string, ProviderOptions>` keyed by id (use `AuthProviderDefaults.Oidc(...)`), and declare `[assembly: AuthProviderDescriptor("id", "Name", "OIDC")]`. This is the exact seam the built-in connectors use.
- **Production gating** — dynamic provider defaults (contributors/adapters) are off in Production unless `Koan:Web:Auth:AllowDynamicProvidersInProduction = true`. Explicit config providers are always honored.
- **Inspect/iterate providers at runtime** → inject `IProviderRegistry`; `EffectiveProviders` is the merged (defaults ⊕ config) id→`ProviderOptions` map, `GetDescriptors()` the discovery list (also exposed at `/.well-known/auth/providers`).
- **Inbound fleet identity** → `Koan.Security.Trust` registers the bearer scheme via `AddKoanBearer()`; protect token endpoints with `[Authorize(AuthenticationSchemes = KoanBearerDefaults.AuthenticationScheme)]` so the bearer 401 fires while cookie redirects stay intact.

## See also

- [Authentication setup](../../../docs/guides/authentication-setup.md) — provider config, the challenge/callback flow, full detail
- [Auth how-to](../../../docs/guides/auth-howto.md) — connector walkthrough
- [Authorization how-to](../../../docs/guides/authorization-howto.md) — the `IAuthorize` seam + the gate · constrain · project entity model
- [SEC-0004 — capability authorization (gate · constrain · project)](../../../docs/decisions/SEC-0004-capability-authorization-gate-constrain-project.md) — the entity authorization model
- [Identity and isolation](../../../docs/reference/identity/index.md) — authentication, authorization, and tenancy map
- [WEB-0071 — auth engine swap to dynamic schemes](../../../docs/decisions/WEB-0071-auth-engine-swap-dynamic-schemes.md)
- [SnapVault](../../../samples/applications/SnapVault/README.md) — optional test-auth composition with access-scoped photo workflows
