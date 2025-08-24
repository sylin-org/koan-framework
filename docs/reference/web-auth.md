# Web Authentication (Sora.Web.Auth)

This page defines the contracts, options, and wiring patterns for Sora.Web.Auth (OIDC, OAuth2, SAML) with provider discovery, sign-in, and account linking. Provider adapters (Google, Microsoft, Discord, generic OIDC) are separate thin modules that self-register defaults via `IAuthProviderContributor`; the core centralizes behavior and composition.

See also: WEB-0043 (multi-protocol auth), ARCH-0040 (constants), WEB-0035 (controllers + transformers), OPS-0015 (config fallback).

## Contract (at a glance)

- Inputs: HTTP requests to controller routes; configuration under `Sora:Web:Auth:*` and per-provider under `Sora:Web:Auth:Providers:{id}:*`.
- Outputs: Redirects to IdP (challenge), authenticated session on callback; JSON payloads for discovery and profile.
- Error modes: ProblemDetails (400/401/404/429) for misconfig, disabled providers, invalid state/nonce, replay, rate limits.
- Success criteria: Provider discovery returns enabled providers; challenge redirects; callback creates or resumes a user session; linking/unlinking enforces policy and audits.

## HTTP endpoints (controllers only)

- GET `/.well-known/auth/providers` → ProviderDescriptor[]
- OIDC/OAuth2
  - GET `/auth/{provider}/challenge`
  - GET `/auth/{provider}/callback`
- SAML
  - GET `/auth/{provider}/saml/metadata`
  - POST `/auth/{provider}/saml/acs`
- Current user and connections
  - GET `/me` → minimal profile and linked connections
  - GET `/me/connections` → linked identities
  - GET `/me/connections/providers` → linkable providers
  - POST `/me/connections/{provider}/link` → initiate link
  - DELETE `/me/connections/{provider}/{keyHash}` → unlink (≥1 method stays; admin override exists via admin API)

ProviderDescriptor shape (stable):
```
{
  id: string,
  name: string,
  protocol: "oidc" | "oauth2" | "saml",
  enabled: boolean,
  icon?: string,
  challengeUrl?: string,   // OIDC/OAuth2
  metadataUrl?: string,    // SAML
  scopes?: string[]
}
```

## Options

Root: `Sora:Web:Auth`
- ReturnUrl:
  - DefaultPath: "/"
  - AllowList: []
- Bff:
  - Enabled: false
- RateLimit:
  - ChallengesPerMinutePerIp: 10
  - CallbackFailuresPer10MinPerIp: 5
- Tokens:
  - PersistTokens: false
- ReConsent:
  - ForceOnLink: false

Per provider: `Sora:Web:Auth:Providers:{id}`
- Common: { Type: `oidc|oauth2|saml`, DisplayName, Icon }
- OIDC: { Authority, ClientId, ClientSecret|SecretRef, Scopes[], CallbackPath? }
- OAuth2 (Discord): { AuthorizationEndpoint, TokenEndpoint, UserInfoEndpoint?, ClientId, ClientSecret|SecretRef, Scopes[], CallbackPath? }
- SAML: { EntityId, IdpMetadataUrl|IdpMetadataXml, SigningCertRef?, DecryptionCertRef?, AllowIdpInitiated: false, ClockSkewSeconds: 120 }

Secrets: default config; optional secret-store adapters (Azure Key Vault, AWS Secrets Manager, GCP Secret Manager, HashiCorp Vault) via `SecretRef`.

### Settings composition and minimal overrides

- Adapters provide sane defaults (protocol `Type`, endpoints, scopes, icons). You can specify only the minimum credentials, and Sora composes final settings by overlaying your values on top of adapter defaults. Defaults are contributed by adapter modules, not hard-coded in the core.
- Precedence: adapter defaults ← app defaults (if any) ← appsettings.json ← appsettings.{Environment}.json ← environment variables; `SecretRef` resolves last.
- Missing required keys after composition produce a clear ProblemDetails error at startup or first use.

Production gating

- In Production, providers contributed only by adapters (no explicit `Sora:Web:Auth:Providers:{id}` entry) are disabled by default unless one of the following is set:
  - `Sora:Web:Auth:AllowDynamicProvidersInProduction=true`, or
  - `Sora:AllowMagicInProduction=true`.
- In Development, adapter defaults are active by default for fast starts.

Minimal Discord example (only credentials provided):
```
"Sora:Web:Auth:Providers:discord": {
  "ClientId": "${DISCORD_CLIENT_ID}",
  "ClientSecret": "${DISCORD_CLIENT_SECRET}"
}
```
The Discord adapter supplies `Type=oauth2`, Authorization/Token/UserInfo endpoints, default scopes, and icon.

## Minimal wiring examples

Note: routes live in controllers; the following shows options snapshots and expected effects. Replace placeholders with your values.

### 1) Google (OIDC) — minimal (adapter provides defaults)

appsettings.json
```
{
  "Sora": {
    "Web": {
      "Auth": {
        "Providers": {
          "google": {
            "ClientId": "${GOOGLE_CLIENT_ID}",
            "ClientSecret": "${GOOGLE_CLIENT_SECRET}"
          }
        }
      }
    }
  }
}
```

Behavior:
- Adapter supplies `Type=oidc`, `Authority=https://accounts.google.com`, default scopes, display name, and icon.
- `/.well-known/auth/providers` includes `google` with challengeUrl `/auth/google/challenge`.
- `/auth/google/challenge` redirects to Google with PKCE and state.

### 2) Microsoft (OIDC) — minimal (adapter provides defaults)

```
"Sora:Web:Auth:Providers:microsoft": {
  "ClientId": "${MS_CLIENT_ID}",
  "ClientSecret": "${MS_CLIENT_SECRET}"
}
```

Behavior: adapter supplies `Type=oidc`, common authority and scopes; you may override tenant/authority if needed.

### 3) Discord (OAuth2) — minimal (adapter provides defaults)

```
"Sora:Web:Auth:Providers:discord": {
  "ClientId": "${DISCORD_CLIENT_ID}",
  "ClientSecret": "${DISCORD_CLIENT_SECRET}"
}
```

Behavior: adapter supplies `Type=oauth2`, Authorization/Token/UserInfo endpoints, default scopes, and icon.

Optional override (e.g., add scopes):
```
"Sora:Web:Auth:Providers:discord": {
  "ClientId": "${DISCORD_CLIENT_ID}",
  "ClientSecret": "${DISCORD_CLIENT_SECRET}",
  "Scopes": ["identify","email","guilds"]
}
```

### 4) Generic OIDC (no wrapper) — minimal required fields

```
"Sora:Web:Auth:Providers:my-oidc": {
  "Type": "oidc",
  "Authority": "https://idp.example.com",
  "ClientId": "${OIDC_CLIENT_ID}",
  "ClientSecret": "${OIDC_CLIENT_SECRET}",
  "Scopes": ["openid","profile","email"]
}
```

Note: generic OIDC has no adapter defaults for Authority; you must provide it.

### 5) Add SAML (enterprise IdP)

```
"Sora:Web:Auth:Providers:corp-saml": {
  "Type": "saml",
  "EntityId": "https://yourapp.example.com/auth/corp-saml/saml/metadata",
  "IdpMetadataUrl": "https://idp.example.com/metadata",
  "SigningCertRef": "kv:auth/corp-saml/signingCert",
  "AllowIdpInitiated": false,
  "DisplayName": "Corporate SSO"
}
```

Behavior:
- Metadata at `/auth/corp-saml/saml/metadata`.
- ACS at `/auth/corp-saml/saml/acs` validates signature/issuer and signs-in or links when user started a link flow.

### 6) Mixed: Google + Microsoft + Discord + SAML

Define providers `google`, `microsoft`, `discord`, `corp-saml` as above. Discovery aggregates all enabled providers for the UI panel. Linking/unlinking works uniformly; SLO remains disabled unless explicitly enabled.

## Storage and linking

- `ExternalIdentity` stores `ProviderKeyHash` (SHA-256 of provider-stable id + provider salt), `ClaimsJson`, timestamps.
- No email-based linking. Links only from authenticated sessions; ensure ≥1 remaining method on unlink for self-service.
- Admin can override unlink constraint; actions audited.

## Security defaults

- OIDC/OAuth2: PKCE, state/nonce, strict callback paths; cookies Secure+HttpOnly; open-redirect prevention (DefaultPath + allow-list).
- SAML: signature + issuer validation, clock skew guard, replay protection (cache assertion ids), `AllowIdpInitiated` off by default.
- Tokens not persisted by default; if enabled, encrypt at rest and redact everywhere.

## Login procedures (summary)

- Discover available providers via `GET /.well-known/auth/providers`.
- Start login with `GET /auth/{provider}/challenge?return={relative-path}`; server sets state/return cookies and redirects to the IdP.
- Complete login at `GET /auth/{provider}/callback?code=...&state=...`; on success, cookie session is established and a local redirect is performed.
- Logout via `GET|POST /auth/logout?return=/`.
- Return URL policy: only relative paths or configured allow-list prefixes are accepted; configure under `Sora:Web:Auth:ReturnUrl:{ DefaultPath, AllowList[] }`.

## Examples: Discovery and profile payloads

Discovery (truncated):
```
[
  { "id":"google","name":"Google","protocol":"oidc","enabled":true,"icon":"/icons/google.svg","challengeUrl":"/auth/google/challenge","scopes":["openid","email","profile"]},
  { "id":"corp-saml","name":"Corporate SSO","protocol":"saml","enabled":true,"icon":"/icons/saml.svg","metadataUrl":"/auth/corp-saml/saml/metadata"}
]
```

Current user:
```
{
  "id": "u_123",
  "displayName": "Ada",
  "pictureUrl": "/img/ada.png",
  "connections": [
    { "provider": "google", "displayName": "Ada (Google)", "keyHash": "..." },
    { "provider": "corp-saml", "displayName": "Ada (Corp)", "keyHash": "..." }
  ]
}
```

## Admin APIs (capabilities)

- Toggle provider enabled/disabled; list and force-unlink user connections (audited).
- Optional global “freeze linking” switch.

## Edge cases

- Misconfigured/disabled provider → 404/400 with ProblemDetails.
- Invalid/missing state/nonce or SAML signature → 400; correlation cookie cleared.
- Email absent from provider → proceed; no email-based linking is performed.
- ReturnUrl not on allow-list → DefaultPath.

## See also

- Decision: `../decisions/WEB-0043-auth-multi-protocol-oauth-oidc-saml.md`
- Web reference: `web.md`
