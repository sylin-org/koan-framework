# Sylin.Koan.Web.Auth.Server — technical contract

## Ownership

The package owns one embedded OAuth 2.1 authorization-server boundary. `OAuthServerController` is the complete HTTP
route/method inventory; concern-specific protocol handlers own request parsing and protocol mechanics. `AuthServerRoutes`
is the single route vocabulary used by routing, metadata, cookies, and startup reporting.

`AuthServerModule` registers the controller, options, artifact cleanup, signing-key lifecycle, and startup guard.
Application code retains the ordinary `AddKoan()` bootstrap. Package reference is activation intent.

## HTTP surface

| Method | Route | Responsibility |
|---|---|---|
| `GET` | `/oauth/authorize` | Validate a public client, redirect, resource, scopes, and PKCE request; begin consent. |
| `GET` | `/oauth/request/{rid}` | Project the browser-bound consent request for the app's page. |
| `POST` | `/oauth/request/{rid}/approve` | Approve an authenticated user's pending request. |
| `POST` | `/oauth/request/{rid}/deny` | Deny the pending request. |
| `POST` | `/oauth/token` | Exchange authorization codes, device codes, or refresh tokens. |
| `POST` | `/oauth/device` | Start a Device Authorization Grant. |
| `POST` | `/oauth/register` | Register a constrained dynamic public client when enabled. |
| `GET` | `/oauth/dev-token` | Development-only token for the current cookie principal. |
| `GET` | `/.well-known/oauth-authorization-server` | RFC 8414-style authorization-server metadata. |
| `GET` | `/.well-known/openid-configuration` | Metadata mirror for compatible clients. |
| `GET` | `/.well-known/jwks.json` | Active and overlapping public signing keys. |

The controller is deliberately transport-thin. It does not duplicate OAuth mechanics or introduce a second service
model around the protocol handlers.

## Client and artifact model

`OAuthClient`, authorization codes, device codes, consent requests, refresh tokens, signing-key records, and grants
are Entity-backed. The client row ID is `client_id`. All clients are public and token metadata advertises only
`token_endpoint_auth_methods_supported: ["none"]`; there is no dormant confidential-client flag or secret path.

Dynamic registration forces loopback redirect URIs and the code grant only, applies source/global rate limits, and
assigns an expiry. Entity-first pre-registration may declare deliberate non-loopback redirects, no expiry, and the
grants the client may use. Every redirect URI is an exact-string match. PKCE-S256 is mandatory for authorization-code
clients.

`OAuthClient.GrantTypes` (values from `OAuthGrantTypes`) is the client's permission: empty allows the code grant only,
RFC 7591's default; `/oauth/device` answers `unauthorized_client` to a client that does not name
`OAuthGrantTypes.DeviceCode`, and `/oauth/authorize` to one whose named grants leave out the code grant (SEC-0024).
The device grant is read again at every later step of a device request: `ConsentPage.Find` and `GET /oauth/request/{id}`,
the device approval, and the `device_code` exchange each refuse a request whose client is missing, expired, or no longer
names `OAuthGrantTypes.DeviceCode` (`unauthorized_client`; `ClientNotAllowed` from `Find`). A device denial is still
accepted, since it grants nothing.

Short-lived opaque protocol artifacts are single-use and cleaned in the background. Refresh rotates on every use;
replay revokes the token family. Remembered consent depends on an unrevoked approval and an unchanged scope set.

`/oauth/token` asks `SubjectStandings` before every mint and before spending the code or the refresh token. A subject
who may not act gets `invalid_grant` and the code or token stays unspent, so an agent refused during a time-out resumes
with the same token, which is not reuse, and a store that cannot answer costs the client nothing. A live subject's
reported name and roles replace those captured at approval; what no contributor speaks to stands as captured. The rows
keep what was last written, for the host with no owner of its people.

Each approval is its own SEC-0005 `AgentGrant`: every authorization-code redemption and device-code exchange starts a
refresh family and issues the grant that family's tokens name, so revoking one grant ends one connection, and refresh
reuse revokes only its own family's grant. With refresh tokens off, nothing would name a grant, so none is issued. The
grant carries the subject, an internal capability term naming the client and a hash of the scope set, and the
resource. It records `IssuedAt` (the token endpoint's clock, the one its `ExpiresAt` is read from), `ClientId`, and
`ClientName`, the name the client registered, read once from its `OAuthClient` and null when it registered none. A
consumer reads those fields, or `AgentGrant.Approvals(subject, take)` for a person's live approvals newest first, and
never parses the capability term.

Remembered consent is a `RememberedConsent` row whose id hashes the subject, the capability term and the resource. A
consent the person gives on the consent page, in either flow, writes it for the lifetime of the grant it issues; a
re-connect it lets through issues its own grant but does not extend it. The module's `AgentGrant` before-remove hook
deletes it whenever a live grant this server issued is removed, by `Remove()`, a removal by id, the access explainer,
erasure or refresh reuse, so revoking any approval re-prompts while the others keep refreshing. The hook runs before the
grant is deleted, on a ten-second token of its own rather than the caller's, so a revoke fails toward asking again: a
cancelled or failed delete can leave the grant, never the consent; a failed forget stops the removal and leaves both
for a retry. A grant from before these rows existed has none, so it never skips the consent page.

`AgentGrant` declares a composite index on `(Subject, ExpiresAt)`. The gate's per-subject lookup and `Approvals` ask
the store for the subject's unexpired rows; a connector that builds the index reads only those, while one that does
not reads more to answer the same (the in-memory connector scans every row, and SQL Server declines an index keyed on
a `string`). The cleanup service removes expired remembered consents and the expired grants that name a client, beside
the other artifacts; removing an expired grant forgets no consent. A grant from before `ClientId` existed is not swept.
With `Koan.Tenancy` referenced, the signing keys, clients, codes, consent requests, device codes and refresh tokens are the host's (`IAmbientExempt`), so the server starts under a Closed posture and its token endpoint needs no tenant to find them. A grant and a remembered consent stay in their tenant; the sweep reaches them through `IMaintenanceScopes`, once per tenant, and a refresh under Closed needs a tenant carrier that can name one (SEC-0033).

## Consent page seam in process

`ConsentPage` is the in-process form of the `/oauth/request/{id}` seam (SEC-0028), and that endpoint answers from it.
`Find(context, id)` returns a `ConsentLookup`: a pending, unexpired `ConsentRequest` by `rid` without spending any
allowance, else — after one attempt against `UserCodeVerificationRateLimitPerMinute` for the address (key
`usercode:{address}`, shared with the endpoint, approve and deny) — a pending, unexpired `DeviceCode` by the normalised
user code, or `Throttled`, or `ClientNotAllowed` (without the request) when that device request's client is not
allowed the device grant. A `PendingConsent` carries the id approve and deny address (the `rid`, or the normalised
user code), the client id and its registered name (or the id), scopes, resource, the display user code or the redirect,
and the approve and deny paths under the application's path base. Ids longer than 128 characters find nothing.

A Device-flow approval or denial needs the person's session, and redirects to the done page with `outcome`: a
time-limited Data Protection payload (purpose `Koan.Web.Auth.Server.ConsentOutcome.v1`, lifetime
`ConsentRequestLifetime`) holding the outcome and the deciding session's subject. `Outcome(context)` unprotects it and
answers only to the session of that subject; anything else reads as null. The code flow's decisions
return to the client's redirect, as before.

## Identity, issuer, and resource boundaries

The authorization server consumes the existing `Koan.cookie` principal established by Web Auth, and reads it
explicitly: `/oauth/authorize` (remembered consent), the `/oauth/request/{rid}` projection, approve and deny
authenticate the person through `Koan.cookie` themselves, so whatever the application's default scheme produced — a
bearer for the same person through Web Auth's resource edge included — never signs anyone in there (SEC-0026). It uses the Trust
pillar's `IIssuer` to mint ES256 access tokens; it does not own login-provider mechanics. Issued authority is
derived from the authenticated session and approved scopes, not from caller-supplied roles.

Tokens are bound to the requested resource. Web Auth's resource edge, on every route and at `/mcp`, validates the signature, issuer, lifetime,
and exact audience before the normal Koan access pipeline evaluates declarations. The origin used for advertised
endpoints is `Issuer` when configured, else the application's canonical base URL (`Koan:Web:ApplicationUrl`,
WEB-0078, path included, else the URL the application accepted, WEB-0081, read per request); with neither,
Development derives it from the request. A token minted under a previous accepted URL carries that issuer and
audience, and is refused once another is accepted; its client re-authorizes.

Every path the server writes for the browser follows one rule (SEC-0027, Web Auth's `ApplicationPaths`): it is
application-relative and gets the application's path base — the configured base URL's, canonical as configured, else
the request's — exactly once. That holds for the redirect from `/oauth/authorize` to the consent page, the
browser-binding cookie's `Path` (`{path base}/oauth`, set and cleared alike), each provider's `challengeUrl` in the
request projection, and `PendingConsent.Approve` and `Deny`, also when a route's first segment equals the path base
(`/oauth/oauth/request/{rid}/approve` under `/oauth`). `ConsentPath` and `DonePath` are application-relative too: one
written with the path base in it gets it again, in the redirect and in the absolute URLs alike. Absolute URLs — the
device `verification_uri`, the done redirect and the advertised endpoints — are the public origin above, which already
carries the path base once, plus the application-relative path.

`OAuthServerController` carries `ResourceServerExemptAttribute`: its endpoints never authenticate by bearer, so Web
Auth's resource edge leaves them their own protocol answers when a client attaches a stale token (SEC-0025).

The issuer identifier is one value, `AuthServerIssuer` (SEC-0023): `Issuer` when configured, else the application's
named `Koan:Security:Trust:Issuer`, else the base URL as above. The metadata states it; the server registers it as
Trust's `IIssuerIdentifier`, so every token the application issues carries it as `iss` and `Koan.bearer` accepts it.
An outside verifier that reads the metadata and the JWKS therefore accepts the tokens. Where Production refuses to
derive the base URL, the metadata refuses with the correction and a token keeps Trust's own name.

## Signing-key lifecycle and failure posture

Development uses an ephemeral per-process issuer unless the application names a persistent key ring folder
(`Koan:Web:KeyRingDirectory`, WEB-0079), and may expose `/oauth/dev-token` and the well-known development client. Other
environments, and Development with a key ring folder named, replace that default with `PersistedIssuerKeyStore`,
protected through ASP.NET Data Protection and backed by Koan Data (SEC-0022). Keys rotate on `KeyRotationInterval`
wherever they persist; retired keys remain in JWKS for `KeyOverlap`.

Startup fails closed outside Development when the effective key store is ephemeral unless
`AllowEphemeralKeyOutsideDevelopment` explicitly acknowledges that token and JWKS continuity will be lost. Persisted
store initialization failures propagate. Data Protection keys and the Koan Data provider must themselves be durable
and shared for a multi-node deployment.

## Configuration ownership

Options bind from `Koan:Web:Auth:Server`:

- public origin and application seam: `Issuer`, `ConsentPath`, `DonePath`;
- token/artifact lifetimes: `AuthorizationCodeLifetime`, `ConsentRequestLifetime`, `AccessTokenLifetime`,
  `DeviceCodeLifetime`, `RefreshTokenLifetime`, `DynamicClientLifetime`;
- capabilities: `AllowDynamicRegistration`, `EnableRefreshTokens`, `RememberConsent`;
- abuse controls: registration and user-code rate limits, device polling interval;
- development affordances: `DevTokenEnabled`, `DevTokenLifetimeMinutes`, `SeedDevClient`;
- key lifecycle: `KeyRotationInterval`, `KeyOverlap`, `AllowEphemeralKeyOutsideDevelopment`.

The MCP consent/done-path compatibility keys are also honored by the path resolver. They do not create a second
authorization-server configuration model.

## Inspectability

Module startup reporting identifies the embedded server, active key posture, and development-token posture. Standard
OAuth metadata and JWKS are the authoritative client-facing projection of routes, grants, PKCE, token authentication,
and keys. The 50 real-host integration specs exercise routes, status codes, cookies, redirects, discovery, DCR,
authorization code, device, refresh, key, host/issuer, and hardening behavior.

## Unsupported and deferred

- confidential clients, client secrets, and service-account/client-credentials issuance;
- general OpenID Provider claims such as an ID-token/user-info product surface;
- SAML or identity federation;
- operator UI for client registration, grants, or key management;
- automatic inference of public proxy origin, consent policy, or durable storage topology;
- treating the embedded server as a general public authorization service for unrelated third parties.
