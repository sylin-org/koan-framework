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

Dynamic registration forces loopback redirect URIs, applies source/global rate limits, and assigns an expiry.
Entity-first pre-registration may declare deliberate non-loopback redirects and no expiry. Every redirect URI is an
exact-string match. PKCE-S256 is mandatory for authorization-code clients.

Short-lived opaque protocol artifacts are single-use and cleaned in the background. Refresh rotates on every use;
replay revokes the token family. Remembered consent depends on a live revocable grant and an unchanged scope set.

## Identity, issuer, and resource boundaries

The authorization server consumes the existing `Koan.cookie` principal established by Web Auth. It uses the Trust
pillar's `IAsymmetricIssuer` to mint ES256 access tokens; it does not own login-provider mechanics. Issued authority is
derived from the authenticated session and approved scopes, not from caller-supplied roles.

Tokens are bound to the requested resource. The MCP resource-server edge validates the signature, issuer, lifetime,
and exact audience before the normal Koan access pipeline evaluates declarations. `Issuer`, when configured, is the
canonical public origin used for metadata and advertised endpoints; otherwise Development derives it from the request.

## Signing-key lifecycle and failure posture

Development uses an ephemeral per-process issuer and may expose `/oauth/dev-token` and the well-known development
client. Other environments replace that default with `PersistedIssuerKeyStore`, protected through ASP.NET Data
Protection and backed by Koan Data. Keys rotate on `KeyRotationInterval`; retired keys remain in JWKS for `KeyOverlap`.

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
