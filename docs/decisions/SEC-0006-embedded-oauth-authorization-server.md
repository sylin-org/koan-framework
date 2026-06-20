# SEC-0006 — Embedded OAuth 2.1 Authorization Server (the MCP auth on-ramp)

- **Status:** Proposed
- **Date:** 2026-06-19
- **Deciders:** framework architect
- **Related:** [SEC-0001](SEC-0001-fleet-identity-and-trust-fabric.md) (trust fabric / issuer), [SEC-0002](SEC-0002-unified-authorization-model.md) (`IAuthorize`/scopes), [SEC-0003](SEC-0003-dev-and-shared-secret-identity.md) (dev identity, fail-closed boot guard), [SEC-0004](SEC-0004-capability-authorization-gate-constrain-project.md) (gate·constrain·project), [SEC-0005](SEC-0005-governed-agent-access-grants-audit-door.md) (grants·audit·door), [WEB-0071](WEB-0071-auth-engine-swap-dynamic-schemes.md) (maintained OAuth handlers / `AuthSchemeSeeder`), [AI-0012](AI-0012-mcp-jsonrpc-runtime.md) (MCP runtime). AN6 + AN10 ([07/AN-cards.md](../assessment/prompts/07/AN-cards.md)).

## Context

Koan exposes entities and verbs to AI agents over MCP ([AI-0012](AI-0012-mcp-jsonrpc-runtime.md)). The HTTP/SSE transport is a **resource server**: it reads the authenticated `ClaimsPrincipal` and enforces the SEC-0004/0005 gate·constrain·project·origin·grant chain. But there is **no way for an MCP client to obtain a token** — no OAuth authorization server, no discovery, no dynamic client registration. A stock MCP client (e.g. Claude Desktop) does Authorization Code + PKCE with auto-discovery + DCR; a headless agent does the Device Authorization Grant. Koan ships neither.

The app already owns the user's identity (interactive login via the configured providers — the `Koan.cookie` session principal carries `sub`, name, roles, permissions). The missing piece is an **Authorization Server that turns that session into a bearer token an MCP client can present** — and a small UI on-ramp for the user to consent.

This ADR defines that AS as a **first-class, opt-in Koan capability** (Reference = Intent), reusing the trust fabric for token issuance/validation and the existing login for the human leg, with the OAuth protocol owned by the framework and **only two pages owned by the app**.

## Decision

Add a new leaf **`Koan.Web.Auth.Server`** — an embedded OAuth 2.1 Authorization Server, mounted at its own root **`/oauth/…`** (a distinct concern from `/auth/{provider}/…` login; it issues tokens to *clients*, it is not a login provider). It supports **Authorization Code + PKCE** (desktop clients) and the **Device Authorization Grant** (headless clients), advertises itself per the MCP authorization spec, mints **asymmetric ES256** access tokens audience-bound to the requesting resource, and drives the browser to **two app-rendered pages** for consent.

### Endpoint surface

| Path | Owner | RFC |
|---|---|---|
| `GET /.well-known/oauth-protected-resource/mcp` | framework | RFC 9728 — points at the AS + the canonical resource id |
| `GET /.well-known/oauth-authorization-server` | framework | RFC 8414 — advertises the endpoints below (issuer = host root) |
| `GET /.well-known/jwks.json` | framework | RFC 7517 — the **public** ES256 key(s) |
| `GET /oauth/authorize` | framework | OAuth 2.1 §4.1 + RFC 7636 |
| `POST /oauth/device` | framework | RFC 8628 §3.1 |
| `POST /oauth/token` | framework | OAuth 2.1 §4.1.3 / RFC 8628 §3.4 |
| `POST /oauth/register` | framework | RFC 7591 (DCR) |
| `GET /oauth/request/{rid}` · `POST …/approve` · `…/deny` | framework | the consent seam the app page consumes |
| `GET /oauth/dev-token` | framework | **dev-only** convenience (see §Dev-token) |
| consent page (e.g. `/me/connect`) | **app** | renders client + scopes + provider pills |
| terminal page (e.g. `/me/connect/done`) | **app** | "you can close this page now" |

The MCP edge (`Koan.Mcp`) emits `WWW-Authenticate: Bearer resource_metadata="…"` on its 401 so clients discover the AS.

### The load-bearing decisions

**D1 — Signing: framework-managed asymmetric ES256, zero key config; the verifier never holds the minting key.**
The AS **must not** be built on the existing `SharedKeyIssuer` (HS256 symmetric). Today `IIssuer.CreateValidationParameters()` hands the *same symmetric key* to the inbound bearer scheme, so the MCP edge that **verifies** tokens could also **mint** them — and `SharedKeyIssuer` defaults to a public well-known key. That violates SEC-0001's own invariant ("whoever can verify must not be able to forge"). Decision: introduce a **first-class ES256 issuer tier** in `Koan.Security.Trust` (promoting the dev `JwtTokenService` model — P-256, JWKS via `JsonWebKeyConverter`). **Zero key config (the delightful default):** the keypair is **auto-generated on first boot, persisted encrypted-at-rest** (via the data layer, protected with `IDataProtector`), and **auto-rotated with JWKS overlap** (the retiring public key stays published until its tokens expire) — the developer never provisions or rotates a key. Development uses an ephemeral per-process key. The embedded AS holds the private key; the MCP edge and any peer verifier hold only the public key (published JWKS). A **SEC-0003-style fail-closed boot guard** refuses to start a production AS on a symmetric/default/well-known key (so the forgeable dev key can never ship). `alg=ES256` is pinned at the verifier (alg=none / HS↔ES confusion structurally impossible). HS256 `SharedKeyIssuer` remains the dev / service-mesh tier; the AS does not use it.

**D2 — Audience binding (RFC 8707): per-resource `aud`, validated at the edge.**
*This also fixes a bug that is live today.* The MCP edge validates only `IsAuthenticated` + required scopes; the bearer scheme validates a single fixed `IIssuer.Audience`. So **any** same-issuer token (a sibling resource, a service token) is already accepted at the MCP tool surface — a confused deputy. Decision: require + bind the OAuth `resource` parameter at `/oauth/authorize` and `/oauth/token`, stamp the **canonical MCP resource URI** into the token `aud` (`IIssuer.Issue` gains an audience path — overload or explicit `aud`), and make the **MCP edge validate `aud == this resource`**, not just the issuer's default audience. Ship D2 **with** the AS — never after.

**D3 — Wire `Koan.bearer` into the MCP route (the single highest-leverage change).**
`EndpointRouteBuilderExtensions` calls `RequireAuthorization()` against the default scheme (`Koan.cookie`), so an MCP client's `Authorization: Bearer …` is **never validated into `context.User`** today — an issued token is inert at the edge. Decision: the `/mcp` group authenticates with **`Koan.bearer`** (per-route scheme or a combined cookie+bearer policy). Once the bearer identity lands in `context.User`, the existing `OriginStamp` → `HttpSseSession.User` → `HttpSseRpcBridge` → SEC-0004/0005 chain enforces it unchanged — **no downstream work**.

**D4 — Authorization-code integrity: bound, single-use, PKCE-mandatory.**
The Test template binds the code to **neither** `client_id` **nor** `redirect_uri` at redemption, makes PKCE optional, and has a host-agnostic `…/callback` redirect fallback. **All three are rejected** (see §Rejected template behaviors). Decision: the code is opaque, server-side, single-use, **TTL ≤ 60s**, bound at issue-time to `(client_id, redirect_uri, code_challenge, scope, subject, resource, origin-tier)` and **every** field re-verified at `/token`; a second redemption **revokes all tokens** issued from that code. **PKCE S256 is mandatory for every code** — reject `/authorize` with no `code_challenge`, accept only `method=S256`, and reject `/token` for a code that carried no challenge. `redirect_uri` is **exact-string match** against the client's registered set, verified identically at both endpoints.

**D5 — Dynamic Client Registration: open by default, but zero-trust.**
DCR is the Claude Desktop happy path (no pre-shared `client_id`), so `POST /oauth/register` is open by default — but a registered client is **untrusted**: forced **public-client** semantics (`token_endpoint_auth_method=none`, no secret), **PKCE-required**, `redirect_uri` constrained to **loopback** (`127.0.0.1`/`[::1]`, any port per RFC 8252) **or** the framework's own pages, **rate-limited** per source + globally, **TTL-expired** with GC, and `client_name`/`logo_uri` treated as **untrusted display-only** strings (escaped, labelled "unverified" on the consent page). A config switch disables DCR for hardened deployments (pre-registered clients only). A dynamic client may **never** register a non-loopback redirect.

**D6 — Down-scope only; AS tokens are identity, never origin.**
Effective authority = **(requested scope) ∩ (what the logged-in user actually holds in the cookie principal)** — never the union, never roles taken from the request (the template's query-string role injection is rejected outright). The scope→capability mapping (to `[Access]` terms / SEC-0005 `AgentGrant`) is **explicit and bounded**: an AS scope must not auto-confer `owner`/`origin` or unbounded grant terms (mirroring `AgentGrant`'s own exclusions). AS tokens are an **identity** signal; the framework-stamped `koan:origin` from the connection always wins — an AS bearer over HTTP/SSE is `origin:remote`, and `origin:internal` requires the declared network, never token contents. STDIO remains local + anonymous and is **not** gated by the AS.

**D7 — Consent surface hardening.**
`rid` is a high-entropy (≥128-bit) unguessable single-use handle bound to the initiating browser session **and** the exact `(client_id, redirect_uri, PKCE, scope, resource)` tuple, fast-expiring. Approval is a **POST with an anti-forgery token** tied to the cookie session. `/connect` + `/connect/done` set `X-Frame-Options: DENY` / CSP `frame-ancestors 'none'`. The consent page **displays the verified client, the exact scopes, and the resource** so substitution between authorize and consent is visible.

**D8 — Device flow secret hygiene (RFC 8628).**
`user_code` is short but high-entropy from an unambiguous alphabet, with rate-limited verification + lockout; `device_code` is ≥128-bit, opaque, single-use, and **never logged** (the template's `req.code` Debug logging is **not** inherited). The `slow_down`/`interval` poll contract is enforced server-side. The verification page shows the requesting client + scopes so the user does not approve a phished code.

**D9 — Authorize once, stays working, revocable: refresh tokens ON + remembered consent (the delightful default).**
A bare 15-minute access token with no refresh would force the client to re-pop the browser every 15 minutes — the feature would feel broken. So the default is short access tokens (~15 min) **plus** refresh tokens, made safe by rotation: every refresh **rotates** the token with reuse-detection that revokes the whole family on reuse (OAuth 2.1 §4.3.1), bound to `client_id`+scope+resource (no widening), stored hashed, supporting revocation (RFC 7009). Crucially, the refresh token is **backed by a SEC-0005 `AgentGrant`** — so "authorize once" creates a server-side, **revocable** grant the user (or an admin) can kill anytime, fleet-wide, and the next refresh fails closed. **Consent is remembered** (per user + client + scope set): a re-connect with an unchanged scope set and a live grant **skips the consent page entirely** — the user authorized once. New scopes or a revoked grant re-prompt. This is the standard "authorize once" OAuth UX, made revocable by reusing the grant machinery we already have.

### Where it lives

`Koan.Web.Auth.Server` → references `Koan.Web.Auth` (cookie session principal, `/.well-known/auth/providers` discovery, the `/auth/{provider}/challenge` round-trip, the chained `AddKoanBearer`) → transitively `Koan.Security.Trust` (the issuer). A clean downward edge, no cycle. It is **not** in `Koan.Mcp` (the AS is MCP-agnostic — it issues OAuth tokens to any bearer client) and **not** in `Koan.Web.Auth` core (it is a heavy opt-in capability; cf. the OTel leaf extraction in ARCH-0088). The **bearer-into-`/mcp` wiring (D3)** and the **protected-resource metadata + `WWW-Authenticate` (D2 discovery)** are `Koan.Mcp` concerns — the AS leaf only **mints**; the resource edge **validates**. The ES256 issuer tier (D1) lands in `Koan.Security.Trust`.

### Rejected template behaviors

The dev Test-provider controllers are an OAuth *shape*, not a secure baseline. The production AS **rejects**:
1. the host-agnostic `…/callback` redirect_uri fallback when the whitelist is empty (open redirect / code exfiltration);
2. optional PKCE (only verified "when a challenge is present");
3. the authorization code bound to neither `client_id` nor `redirect_uri` at redemption;
4. roles/permissions read from the authorize **query string** (`ParseExtras`) — a self-elevation-to-admin hole;
5. the `Enabled` disjunction that ships the dev mini-AS into any environment, and the `req.code` Debug logging;
6. the single static `client_id` + mandatory `client_secret` (breaks public DCR clients).

## The session → token bridge (precise)

At `/oauth/token`, after PKCE + code validation: read `HttpContext.User` (the `Koan.cookie` session principal) → `TrustClaims{ Subject = sub, Name, Email, Roles = session roles, Permissions = the granted scopes (down-scoped per D6, not the session's permission claims), Extra = { client_id, aud = resource } }` → `IIssuer.Issue(claims, lifetime, audience = resource)` (ES256, D1) → return `{ access_token, token_type: "Bearer", expires_in, [refresh_token] }`. The MCP edge validates it under `Koan.bearer` (D3) with `aud` enforcement (D2); `context.User` then carries `ClaimTypes.Role` + `Koan.permission` intact (`MapInboundClaims=false`), and the SEC-0004/0005 chain runs unchanged.

## Phased build

1. **Foundation + resource edge (unblocks Stage-1 testing).** ES256 issuer tier in Trust (persisted key, JWKS, fail-closed guard) + audience binding (D1, D2); wire `Koan.bearer` into `/mcp` (D3); serve `/.well-known/oauth-protected-resource/mcp` + `WWW-Authenticate`; **dev-token endpoint** (`/oauth/dev-token`, hard dev-gated) that mints a real ES256 token for the current user. → a hand-obtained token reaches the gates end-to-end.
2. **Auth-code AS.** `/oauth/authorize` + `/oauth/token` (auth-code) with D4 integrity + the `/oauth/request/{rid}` consent seam + the consent-page contract (D7). Hand-drive with curl/PKCE.
3. **Discovery + DCR.** `/.well-known/oauth-authorization-server` (RFC 8414, + OIDC-form mirror) + `/oauth/register` (D5).
4. **Device grant.** `/oauth/device` + the device branch of `/oauth/token` (D8).
5. **Claude Desktop e2e + hardening.** Full auto-discovery dance; refresh tokens + remembered consent backed by `AgentGrant` (D9); rate limits; conformance + adversarial tests.

Each phase is TDD + mutation-checked + ratchet-green; the integration suite boots a real host (ARCH-0079).

## Consequences

- **Positive:** a general, reusable Koan OAuth 2.1 AS (any app issues tokens to API/agent clients, not just MCP); the confused-deputy audience bug (D2) is closed; the trust fabric gains the real asymmetric tier SEC-0001 always intended; the app's auth surface stays two pages.
- **Negative / cost:** a security-critical new leaf with real attack surface (DCR, device flow, consent CSRF) that must be threat-modelled and conformance-tested, not assumed; a new persisted-key lifecycle (rotation, JWKS) to operate.
- **Neutral:** the app's only obligations are declaring `[Access]` gates and rendering `/connect` + `/connect/done` against the seam contract.
