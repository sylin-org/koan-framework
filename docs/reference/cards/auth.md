---
type: REF
domain: web
title: "Auth — pillar map"
audience: [developers, ai-agents]
status: current
last_updated: 2026-07-18
framework_version: source-first
validation:
  date_last_tested: 2026-07-18
  status: verified
  scope: docs/reference/cards/auth.md
---

# Auth — pillar map

> One-screen map of the Auth pillar — external sign-in, authorization, and **token issuance** on top of the [Web](web.md) pillar. Full detail: [authentication-setup.md](../../guides/authentication-setup.md).

**What it does** — Cookie-session sign-in through external OAuth2 / OIDC providers, then entity surfaces authorized by **gate · constrain · project** ([SEC-0004](../../decisions/SEC-0004-capability-authorization-gate-constrain-project.md)). Reference an auth connector package (`Sylin.Koan.Web.Auth.Connector.Google` / `.Microsoft` / `.Discord`) and its provider contributes defaults; config supplies the per-provider `ClientId` / `ClientSecret`. The flow rides the maintained ASP.NET `OAuthHandler` / `OpenIdConnectHandler`, bound at startup through dynamic scheme registration ([WEB-0071](../../decisions/WEB-0071-auth-engine-swap-dynamic-schemes.md)).

## The one canonical pattern

Reference a connector and supply config for sign-in; then authorize the entity surface with the three
composable layers, enforced identically on REST and MCP through one `IAuthorize` seam:

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
| `IKoanAuthFlowHandler` | Optional module seam for sign-in, sign-out, validation, challenge, denied, and bootstrap behavior; implement only the events owned by the module. |

The old `CanRead` / `CanWrite` / `CanRemove` virtuals on `EntityController<T>` were **removed** (ARCH-0092) — use `[Access(...)]` for the gate and `EntityAccess<T>` for row scope.

**Governed agent access** ([SEC-0005](../../decisions/SEC-0005-governed-agent-access-grants-audit-door.md)) layers on the gate: a server-side **`AgentGrant`** lends a subject a capability *beyond* its token (`new AgentGrant { Subject, Capability = "has:scope:x", Resource, ExpiresAt }.Save()`) — the gate materializes active grants only on the token-denied path and re-evaluates the same gate; `Remove()`/expiry revoke on the next call. **`[Audit]`** writes an `AgentAction` per mutation; **`[Door]`** discloses a denied verb's `needs` instead of walling it (role-gated stays a Wall). See the [agent-native card](agent-native.md).

## Issuing tokens (OAuth 2.1 AS)

Beyond signing users in, Koan can **issue** tokens — an embedded OAuth 2.1 Authorization Server, the on-ramp that lets an MCP client obtain a bearer token. Reference the opt-in leaf **`Sylin.Koan.Web.Auth.Server`** and it activates under `AddKoan()`. It lives at its own root **`/oauth/…`**, distinct from `/auth/{provider}/` login. It supports Authorization Code + PKCE, the RFC 8628 device grant, RFC 7591 DCR, and rotating refresh tokens, with discovery at `/.well-known/oauth-authorization-server` and a published JWKS. See [oauth-server-howto.md](../../guides/oauth-server-howto.md) for the exercised boundary ([SEC-0006](../../decisions/SEC-0006-embedded-oauth-authorization-server.md)).

## The escape hatch

When no first-party connector fits, reference **`Sylin.Koan.Web.Auth`** and add a provider directly under
`Koan:Web:Auth:Providers` with `Type: "oidc"`, `Authority`, `ClientId`, and `ClientSecret`. OAuth2/OIDC mechanics
already belong to Web Auth; there is no generic OIDC connector package.

A reusable connector module registers one immutable `AuthProviderDefinition` through ordinary DI. It supplies only
stable provider knowledge (ID, protocol endpoints/authority, display metadata, scopes, and priority); Web Auth overlays
application configuration and alone owns activation, eligibility, election, scheme creation, and evidence. Cross-module
contracts live in the inert `Sylin.Koan.Web.Auth.Abstractions` package.

## Where it is exercised

The current proof lives in the focused integration suites under
[`tests/Suites/Auth`](../../../tests/Suites/Auth) and
[`tests/Suites/Security`](../../../tests/Suites/Security). There is no graduated public sign-in sample
yet; connector presence alone is not a provider-deployment claim.
