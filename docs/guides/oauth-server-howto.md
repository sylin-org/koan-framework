---
type: GUIDE
domain: web
title: "Embedded OAuth 2.1 Authorization Server (the MCP auth on-ramp)"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2026-07-18
framework_version: v1.0.0
validation:
  date_last_tested: 2026-07-18
  status: verified
  scope: end-to-end against Koan.Web.Auth.Server.IntegrationTests (50 specs)
  notes: "The full Authorization Code + PKCE, Device, refresh, discovery, and DCR flows are exercised by the real-host AS integration suite."
related_guides:
  - mcp-http-sse-howto.md
  - mcp-agent-native-howto.md
  - authentication-setup.md
---

# Embedded OAuth 2.1 Authorization Server (the MCP auth on-ramp)

**Related Guides**
- [MCP over HTTP + SSE](mcp-http-sse-howto.md) — the `/mcp` resource-server edge that validates the tokens this AS issues
- [MCP Agent-Native](mcp-agent-native-howto.md) — the end-to-end "go remote with auth" walkthrough
- [Authentication Setup](authentication-setup.md) — provider sign-in vs. token issuance

---

This guide is about Koan **issuing** OAuth tokens. It is what lets a stock MCP client — Claude Desktop, a headless agent — connect to your app's `/mcp` surface, get a token through the standard OAuth dance, and call your tools under the **same `[Access]` authorization** your REST surface already enforces.

## Two different things at `/auth` and `/oauth`

Koan has two distinct auth surfaces. Keep them separate in your head:

| Surface | Job | Who is authenticating |
|---|---|---|
| `/auth/{provider}/…` | **Sign a USER in** through an external IdP (Google / Microsoft / Discord / OIDC) → a `Koan.cookie` session | a human, in a browser |
| `/oauth/…` | **Issue a token to a CLIENT** (the embedded Authorization Server) | an app / agent (e.g. Claude Desktop) |

The Authorization Server is **not** a login provider — it turns the user's existing cookie session into a bearer token a client can present. See [authentication-setup.md](authentication-setup.md) for the sign-in half; this guide is the issuing half.

## Activation — Reference = Intent

The AS is an opt-in leaf. Reference it; that is the whole wiring:

```xml
<PackageReference Include="Sylin.Koan.Web.Auth.Server" />
```

```csharp
// Program.cs — the whole bootstrap.
builder.Services.AddKoan();
```

Referencing `Koan.Web.Auth.Server` activates the AS at `/oauth/…`, the discovery documents under `/.well-known/…`,
and Trust's persisted ES256 signing-key lifecycle. Trust itself wires the `Koan.bearer` scheme that the `/mcp` edge
uses before applying its exact audience check. You write declarations, not ceremony.

## What you implement: exactly two pages

The framework owns the OAuth **protocol**. Your app owns **two rendered pages** — mirroring how your existing sign-in page renders while the framework serves `/.well-known/auth/providers`:

1. **A consent page** (e.g. `/me/connect`) — renders the requesting client, the scopes approving gives (for a person signed in, those they hold of what was requested), and **Allow / Deny**.
2. **A terminal page** (e.g. `/me/connect/done`) — a simple "you can close this page now".

Tell the framework where they are:

```jsonc
"Koan": { "Web": { "Auth": { "Server": {
  "ConsentPath": "/me/connect",
  "DonePath":    "/me/connect/done"
} } } }
```

> The AS also honors `Koan:Mcp:Auth:ConsentPath` / `DonePath` (the keys the MCP integration prompt uses), so either namespace works.

### The consent-seam contract

The consent page reads `rid` (authorization-code flow) **or** `user_code` (device flow) from the query string, then talks to the framework:

```http
GET /oauth/request/{rid}
→ 200 {
    "client":   { "name": "Claude", "verified": false },
    "scopes":   [ { "id": "mcp.read", "description": "mcp.read" } ],
    "resource": "https://app.example.com/mcp",
    "user":     { "loggedIn": true },
    "providers": [ /* same shape as /.well-known/auth/providers */ ]
  }
```

- If `user.loggedIn` is `false`, render the **same provider pills** your sign-in page already shows (the user signs in, then returns here).
- **Allow** → `POST /oauth/request/{rid}/approve`. **Deny** → `POST /oauth/request/{rid}/deny`.
- The framework responds with the redirect to follow (a `302` for a form post, or `200 { "redirect": "…" }` for a `fetch()` — navigate the top window to it). The device flow lands the user on the terminal page; the device, which was polling, now has its token.

A page rendered on the server can ask the same question in process: `ConsentPage.Find(HttpContext, rid ?? userCode)` answers the pending request (client, scopes, resource, the user code or redirect) with the paths Allow and Deny post to, normalising a typed user code and sharing the endpoint's per-address allowance; the terminal page reads `await ConsentPage.Outcome(HttpContext)` — `Approved`, `Denied`, or null for anything this server did not write for the person reading it ([SEC-0028](../decisions/SEC-0028-a-consent-page-reads-koans-answer.md)).

The `rid` is a high-entropy, single-use handle **bound to the initiating browser** (an httpOnly `Lax` cookie); approval requires an authenticated session. You never implement `/oauth/authorize`, `/oauth/token`, `/oauth/device`, `/oauth/register`, the consent endpoints, the discovery docs, or token minting — those are framework endpoints.

## The flows (what the client does)

A conformant client never needs hand-holding — it discovers and drives these itself. The catalogue:

### Discovery (RFC 9728 / RFC 8414)
- A `401` from `/mcp` carries `WWW-Authenticate: Bearer resource_metadata="…/.well-known/oauth-protected-resource/mcp"`.
- `GET /.well-known/oauth-protected-resource/mcp` → the resource id + the authorization server.
- `GET /.well-known/oauth-authorization-server` (+ an OIDC-form mirror at `/.well-known/openid-configuration`) → the endpoints, `code_challenge_methods_supported: ["S256"]`, `token_endpoint_auth_methods_supported: ["none"]`, the grant types.
- `GET /.well-known/jwks.json` → the public ES256 keys.

### Dynamic Client Registration (RFC 7591)
`POST /oauth/register` is open by default (Claude Desktop has no pre-shared `client_id`) but **zero-trust**: every dynamic client is forced public (no secret), constrained to **loopback** redirect URIs (RFC 8252), rate-limited, and TTL-expired. Disable it for a hardened deployment with `Koan:Web:Auth:Server:AllowDynamicRegistration=false`.

All supported clients are public; confidential clients and client secrets are not implemented. When DCR is disabled,
pre-register known clients through the Entity model:

```csharp
using Koan.Web.Auth.Server.Protocol;

await new OAuthClient
{
    Id = "operations-console",
    ClientName = "Operations Console",
    RedirectUris = ["https://console.example.com/oauth/callback"],
    CreatedUtc = DateTimeOffset.UtcNow
}.Save();
```

Dynamic clients remain loopback-only. A pre-registered client may declare a deliberate non-loopback redirect, which
the server matches exactly.

### Authorization Code + PKCE (desktop clients)
`GET /oauth/authorize?response_type=code&client_id=…&redirect_uri=…&code_challenge=…&code_challenge_method=S256&resource=…&state=…` → consent → `POST /oauth/token` (grant `authorization_code`). PKCE-S256 is **mandatory**; the code is bound to `(client_id, redirect_uri, code_challenge, scope, subject, resource)`, re-verified at the token endpoint, and single-use.

### Device Authorization Grant (RFC 8628, headless clients)
`POST /oauth/device` → `{ device_code, user_code, verification_uri, verification_uri_complete, interval }`. The user enters the `user_code` on the consent page (the same seam); the device polls `POST /oauth/token` (grant `urn:ietf:params:oauth:grant-type:device_code`) through `authorization_pending` / `slow_down` until it gets the token.

Only a client allowed the grant may start it: pre-register it with `GrantTypes = [OAuthGrantTypes.DeviceCode]`. A client that names no grant is allowed the code grant only, and a dynamically registered client never gets the device grant; either is answered `unauthorized_client` ([SEC-0024](../decisions/SEC-0024-the-device-grant-is-a-clients-explicit-permission.md)). The permission is read again when the code is looked up, approved and exchanged, so a request made before an operator removed the grant is answered `unauthorized_client` there too, and `ConsentPage.Find` answers it `ClientNotAllowed` without the request.

### Refresh (stay connected)
`POST /oauth/token` (grant `refresh_token`) rotates the token. Refresh tokens are **on by default** so a client is not forced to re-pop the browser every 15 minutes — made safe by rotation with reuse-detection (replaying a rotated token revokes the whole family) and backed by a **revocable grant** (see Security, below). **Consent is remembered** — once the person consents, a re-connect with the same scopes skips the consent page until that consent's grant expires or any of the person's approvals for that client and scope set is revoked.

## The `/mcp` resource-server edge

With `Koan:Mcp:RequireAuthentication=true`, the MCP HTTP edge (Streamable HTTP by default; [AI-0037](../decisions/AI-0037-mcp-streamable-http-transport.md)) is an OAuth 2.1 **resource server**:

```jsonc
"Koan": { "Mcp": {
  "EnableStreamableHttpTransport": true,
  "RequireAuthentication": true,
  "ResourceUri": "https://app.example.com/mcp"   // canonical aud — see below
} }
```

- It validates ES256 tokens via the framework's `Koan.bearer` scheme — **no `AddAuthentication`/`AddJwtBearer` ceremony**.
- It enforces the **per-resource audience** (RFC 8707: `aud == this resource`) — a token minted for another resource (a sibling API, a service-mesh token) is rejected even though its signature is valid (the confused-deputy fix).
- It emits the RFC 9728 `WWW-Authenticate` challenge and serves the protected-resource metadata.

`Koan:Mcp:ResourceUri` is the **canonical** resource id and is authoritative when set — it defeats a spoofed `Host` header. Leave it unset only in Development, where it derives from the request host.

Once the bearer identity lands in `context.User`, the existing **gate · constrain · project · origin · grant** chain ([SEC-0004](../decisions/SEC-0004-capability-authorization-gate-constrain-project.md) / [SEC-0005](../decisions/SEC-0005-governed-agent-access-grants-audit-door.md)) runs **unchanged** — the token is identity; your `[Access]` declarations are the authorization.

## The application's own pages as a resource

When a client should read the application's ordinary pages as the person who approved it, request the token with
`resource` set to the application's base URL and turn on Web Auth's resource edge:

```jsonc
"Koan": { "Web": { "Auth": { "ResourceServer": { "Enabled": true } } } }
```

A request with an `Authorization` header is then authenticated by its bearer alone; the token is accepted only for the
base URL (or `Koan:Web:Auth:ResourceServer:Resource`), its subject is the person's `ClaimTypes.NameIdentifier`, and a
token that is not accepted answers 401 with `Bearer error="invalid_token"`. The authorization server's own
authorize and consent endpoints keep deciding by the person's session alone ([SEC-0026](../decisions/SEC-0026-the-authorization-server-decides-by-the-session.md)). See [SEC-0025](../decisions/SEC-0025-an-application-accepts-its-own-bearer-on-its-pages.md).

## One token for the pages and the tools

A client that reads the pages and calls the tools holds one token when the MCP edge says it is part of the application:

```jsonc
"Koan": {
  "Mcp": { "EnableStreamableHttpTransport": true, "AcceptApplicationBearer": true },
  "Web": { "Auth": { "ResourceServer": { "Enabled": true } } }
}
```

A token for the application's resource is then accepted at `/mcp` too. The edge still advertises `{base}/mcp`, so a client
that knows only the edge authorizes for that and holds a token good there and on no route; a client that wants both doors
asks for `resource` set to the base URL. A token for the application also reaches tools with no route (`[McpTool]`, an
entity's tools), as far as its subject may. See [SEC-0037](../decisions/SEC-0037-an-edge-that-is-part-of-the-application.md).

## A token for an account your application verified

At a door where an account proved itself some other way, the application asks for the token the server's flows mint:

```csharp
await HttpContext.SignInAsync(AuthenticationExtensions.CookieScheme, person); // AtprotoServiceAuth.SignIn answers its person
var token = await AccessToken.For(HttpContext, person, ct);
return token is null ? StatusCode(StatusCodes.Status403Forbidden) : Ok(new { access_token = token.Value, token_type = "Bearer", expires_in = (int)token.Lifetime.TotalSeconds });
```

**Sign in first, and pass the principal you signed in.** Under Koan.Identity a subject with no row has no standing, so a new
account gets no token until it has signed in; sign-in also runs your admission, roles and ban, and settles a linked subject
on its person's id. The answer is `StatusCode`, not `Forbid()`, which forwards to the cookie scheme and redirects.

It is for the application's own resource and lasts `AccessTokenLifetime`. It carries the principal's subject, name, email,
roles and scopes, with the subject's standing over the name and the roles, and is null for a subject who may not act. It has
no refresh token: the account proves itself again. Outside Development, with no configured or accepted URL to name the
application, it refuses rather than take a caller's `Host` for its audience. A door that itself admits a stale bearer carries
`[ResourceServerExempt]`.

## Testing locally before a full client: `/oauth/dev-token`

In **Development only** (a hard `404` everywhere else), `GET /oauth/dev-token` mints a real ES256 token for the current cookie user, audience-bound to your MCP resource:

```bash
# sign in via your app first (sets the Koan.cookie), then:
curl -b cookies.txt "http://localhost:5000/oauth/dev-token"
# → { "access_token": "...", "token_type": "Bearer", "expires_in": 3600, "resource": "http://localhost:5000/mcp" }

# present it to the Streamable HTTP edge — the bearer passes the resource-server gate (a 200 + Mcp-Session-Id, not a 401):
curl -i -H "Authorization: Bearer <token>" -H "Accept: application/json, text/event-stream" \
  -X POST "http://localhost:5000/mcp" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"curl","version":"1"}}}'

# request arbitrary scopes/roles to exercise a scope-gated [Access(has:scope:x)] / [RequireScope] path:
curl -b cookies.txt "http://localhost:5000/oauth/dev-token?scope=orders:read%20orders:fulfill&roles=admin"
```

This is the fast path to exercise the authenticated `/mcp` edge end-to-end without standing up a real OAuth client. The `?scope=` (space-delimited) and `?roles=` knobs mint exactly those into the token **as-is** (no held-filter — a Development-only affordance; the endpoint stays a hard `404` outside Development).

## Configuration reference (`Koan:Web:Auth:Server:*`)

| Key | Default | Meaning |
|---|---|---|
| `Issuer` | _(`Koan:Security:Trust:Issuer`, else `Koan:Web:ApplicationUrl`, else the URL the application accepted, else the request host)_ | An explicit override of the AS's public origin for the discovery issuer + every advertised endpoint. Normally unset: the issuer is the application's canonical base URL, path included (WEB-0078), which is already host-spoof-proof behind a proxy. Every access token carries the issuer the metadata states as `iss` ([SEC-0023](../decisions/SEC-0023-a-token-carries-the-issuer-its-server-states.md)). |
| `ConsentPath` / `DonePath` | `/me/connect` · `/me/connect/done` | Your two app pages, application-relative: under a base URL with a path Koan adds the path once, in its redirects and absolute URLs alike ([SEC-0027](../decisions/SEC-0027-the-consent-flow-lives-under-the-base-path.md)). |
| `AccessTokenLifetime` | `15 min` | Issued access-token lifetime. |
| `RefreshTokenLifetime` | `30 days` | Refresh-token / backing-grant lifetime. |
| `EnableRefreshTokens` | `true` | Rotating refresh tokens, each family backed by its own grant. Off, no grant is issued and no consent remembered. |
| `RememberConsent` | `true` | Skip the consent page on a re-connect the person consented to, until that approval's grant expires or any of their approvals for it is revoked. Needs refresh tokens. |
| `AllowDynamicRegistration` | `true` | Open RFC 7591 DCR (zero-trust). |
| `DevTokenEnabled` | `true` | The `/oauth/dev-token` convenience (still hard dev-gated). |
| `KeyRotationInterval` / `KeyOverlap` | `30 days` · `2 h` | ES256 signing-key rotation cadence + JWKS overlap. |
| `AllowEphemeralKeyOutsideDevelopment` | `false` | Acknowledge running a production AS on a non-persisted key (the boot guard fails closed otherwise). |

## The security model in one breath

- **Asymmetric signing.** Tokens are ES256, signed by a framework-managed key the verifier never holds ("whoever can verify must not be able to forge"). The key is auto-generated, **persisted encrypted-at-rest, auto-rotated** with JWKS overlap; a **fail-closed boot guard** refuses to start a production AS on an ephemeral key. (Development uses an ephemeral per-process key unless the application names a key ring folder, `Koan:Web:KeyRingDirectory`, in which case the keys persist there too and a local install's tokens outlive a restart.)
- **Down-scope only.** A token's roles come from the user's **session**, never the request. Scopes are what was requested and what the person holds (SEC-0006 D6); the request can never widen authority. Granted scopes ride the RFC 9068 `scope` claim — the authorization grant the `[Access(has:scope:x)]` gate reads (not the inert `Koan.permission`).
- **Audience-bound (RFC 8707).** Every token is bound to a specific resource and validated `aud == this resource` at the edge.
- **Revocable.** Refresh is backed by a SEC-0005 `AgentGrant` — the user (or an admin) can revoke it fleet-wide, and the next refresh fails closed. Each approval is its own grant, stating when it was issued (`IssuedAt`) and to which client (`ClientId`, and the `ClientName` the client registered, unverified), so a page lists a person's live approvals newest first with `AgentGrant.Approvals(personId, take)` and revokes one with `Remove()`: that connection stops refreshing, the client's next connection asks the person again, and the person's other approvals keep working. Grants issued before Koan recorded these answer null for them, are not listed, and never keep consent remembered.

## See also

- [SEC-0006](../decisions/SEC-0006-embedded-oauth-authorization-server.md) — the decision record (the nine load-bearing decisions, the rejected behaviors, the threat model).
- [SEC-0001](../decisions/SEC-0001-fleet-identity-and-trust-fabric.md) — the trust fabric / issuer.
- [MCP over HTTP + SSE](mcp-http-sse-howto.md) — the resource-server edge in depth.
