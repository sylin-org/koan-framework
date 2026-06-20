---
type: REF
domain: web
title: "Auth — pillar map"
audience: [developers, ai-agents]
status: current
last_updated: 2026-06-20
framework_version: v0.17.0
validation:
  date_last_tested: 2026-06-20
  status: verified
  scope: docs/reference/cards/auth.md
---

# Auth — pillar map

> One-screen map of the Auth pillar — external sign-in, authorization, and **token issuance** on top of the [Web](web.md) pillar. Full detail: [authentication-setup.md](../../guides/authentication-setup.md).

**What it does** — Cookie-session sign-in through external OAuth2 / OIDC providers, then entity surfaces authorized by **gate · constrain · project** ([SEC-0004](../../decisions/SEC-0004-capability-authorization-gate-constrain-project.md)). Reference an auth connector package (`Koan.Web.Auth.Connector.Google` / `.Microsoft` / `.Discord`) and its provider self-registers; config supplies the per-provider `ClientId` / `ClientSecret`. The flow itself rides the **maintained ASP.NET `OAuthHandler` / `OpenIdConnectHandler`** (PKCE, nonce, state, id_token validation owned by the framework), bound at startup via dynamic scheme registration — Koan stopped hand-rolling the OAuth2/OIDC exchange ([WEB-0071](../../decisions/WEB-0071-auth-engine-swap-dynamic-schemes.md)).

## The one canonical pattern

Reference a connector + supply config for sign-in; then authorize the entity surface with the three composable layers (each enforced identically on REST **and** MCP through the one `IAuthorize` seam):

```jsonc
// appsettings.json — Reference = Intent: the Google connector reference makes "google" a provider; config supplies its secret.
"Koan": { "Web": { "Auth": { "Providers": {
  "google": { "ClientId": "{GOOGLE_CLIENT_ID}", "ClientSecret": "{GOOGLE_CLIENT_SECRET}" }
} } } }
```

```csharp
// GATE — who, per action (coarse, allow-by-default). Legacy [Authorize]/[RequireScope] still lower into this.
[Access(read: "anyone", write: "authenticated", remove: "is:admin")]
public sealed class Ledger : Entity<Ledger> { public string Entry { get; set; } = ""; }

// CONSTRAIN — which rows. Declare Owner once; one Constrain drives the filter, the 404, the mass-delete bound,
// and the create-stamp. PROJECT is automatic — each response advertises the realized can:[] verbs.
public sealed class MemoAccess : EntityAccess<Memo>
{
    protected override Expression<Func<Memo, bool>>? Owner => m => m.OwnerId == CurrentUserId;
    public override IAccessFilter<Memo> Constrain(IAccessFilter<Memo> q, AccessAction action) => action switch
    {
        AccessAction.Create => q.Stamp(m => m.OwnerId, CurrentUserId),
        AccessAction.Update => q.Where(Owner!).Stamp(m => m.OwnerId, CurrentUserId),
        _ => q.Where(Owner!),
    };
}

// Custom (non-entity) actions still gate with stock ASP.NET [Authorize]:
[ApiController, Route("api/reports")]
public sealed class ReportsController : ControllerBase
{
    [Authorize(Roles = "admin")] [HttpPost("rebuild")]
    public Task<IActionResult> Rebuild() { /* ... */ return Task.FromResult<IActionResult>(NoContent()); }
}
```

Provider roles map onto `ClaimTypes.Role`, so `[Authorize(Roles = "admin")]` and the gate's `is:admin` work once a provider returns roles.

## ≤5 attributes you'll use

| Attribute / type | What it does |
|---|---|
| `[Access(read:, write:, remove:)]` | The per-action **gate** on `Entity<T>` (SEC-0004) — a token bag per action (`anyone` / `authenticated` / `is:role` / `has:scope:x` / `owner`), allow-by-default, enforced on every surface. |
| `EntityAccess<T>` | The **constrain** realization — `Owner` declared once + `Constrain(q, action)` (`q.Where(...)` / `q.Stamp(...)`) for row-level ownership. |
| `[Authorize]` / `[Authorize(Roles = "admin")]` | ASP.NET gate for custom (non-entity) controller actions; also lowers into the entity gate as sugar when placed on `Entity<T>`. |
| `[AuthProviderDescriptor("id", "Name", "OIDC")]` | Assembly-level (connector authors) — declares a provider's id, display name, and protocol for discovery. |

The old `CanRead` / `CanWrite` / `CanRemove` virtuals on `EntityController<T>` were **removed** (ARCH-0092) — use `[Access(...)]` for the gate and `EntityAccess<T>` for row scope.

**Governed agent access** ([SEC-0005](../../decisions/SEC-0005-governed-agent-access-grants-audit-door.md)) layers on the gate: a server-side **`AgentGrant`** lends a subject a capability *beyond* its token (`new AgentGrant { Subject, Capability = "has:scope:x", Resource, ExpiresAt }.Save()`) — the gate materializes active grants only on the token-denied path and re-evaluates the same gate; `Remove()`/expiry revoke on the next call. **`[Audit]`** writes an `AgentAction` per mutation; **`[Door]`** discloses a denied verb's `needs` instead of walling it (role-gated stays a Wall). See the [agent-native card](agent-native.md).

## Issuing tokens (OAuth 2.1 AS)

Beyond signing users in, Koan can **issue** tokens — an embedded OAuth 2.1 Authorization Server, the on-ramp that lets an MCP client (Claude Desktop, a headless agent) obtain a bearer token. Reference the opt-in leaf **`Koan.Web.Auth.Server`** and it activates under `AddKoan()` (no ceremony). It lives at its own root **`/oauth/…`** — distinct from `/auth/{provider}/` login: it issues tokens to **clients**, it is not a provider. It speaks Authorization Code + PKCE, the RFC 8628 device grant, RFC 7591 DCR, and rotating refresh tokens (reuse-detection, backed by a revocable SEC-0005 `AgentGrant`), with discovery at `/.well-known/oauth-authorization-server` + a published JWKS. The signing tier is the `Koan.Security.Trust` ES256 asymmetric issuer (`IAsymmetricIssuer` / `EcdsaIssuer` — persisted + rotating keys, published JWKS, fail-closed boot guard); the `Koan.bearer` scheme validates the tokens it mints. The app owns only **two render-only pages** (consent + done) — the framework owns the protocol. Full walkthrough: [oauth-server-howto.md](../../guides/oauth-server-howto.md) ([SEC-0006](../../decisions/SEC-0006-embedded-oauth-authorization-server.md)).

## The escape hatch

When no first-party connector fits, reference the generic **`Koan.Web.Auth.Connector.Oidc`** package (disabled by default — `Defaults.Enabled = false`, it ships handler wiring with no provider defaults). Add a provider entry under `Koan:Web:Auth:Providers` with its `Authority` / `ClientId` / `ClientSecret` and the dynamic-scheme seeder wires a real `OpenIdConnectHandler` for it. To contribute provider defaults from your own package instead, implement **`IAuthProviderContributor`** (`Koan.Web.Auth.Providers`) and return `ProviderOptions` keyed by id — the same seam the built-in connectors use.

## The sample that shows it

[`samples/S5.Recs`](../../../samples/S5.Recs/README.md) — centralized provider discovery, cookie-session challenge/callback login, and `[Authorize]`-gated, claim-scoped controllers over a real app.
