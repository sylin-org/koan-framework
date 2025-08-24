---
id: WEB-0043
slug: auth-multi-protocol-oauth-oidc-saml
domain: WEB
status: Accepted
date: 2025-08-23
title: Sora.Web.Auth — multi-protocol authentication (OIDC, OAuth2, SAML) with pluggable adapters
---

## Context

Modern apps need first-class social and enterprise sign-in. Requirements:

- Protocol-agnostic core with adapters for OIDC/OAuth2 (Google, Microsoft, Discord) and SAML 2.0.
- Minimal configuration via Sora config resolution (env/appsettings) and optional secret-store adapters.
- Provider discovery endpoint for UI (“Select a provider”).
- Sign-in plus account linking/unlinking without email-based linking.
- Provider-agnostic user store using Sora Entity<> resolution by default; extensible via DI.
- Strong security defaults (PKCE, state/nonce, strict returnUrl policy), audit trail, and rate limiting.

## Decision

Adopt a new module, Sora.Web.Auth (core), with adapter packages:

- Sora.Web.Auth.Oidc (generic OIDC)
- Sora.Web.Auth.Google, Sora.Web.Auth.Microsoft, Sora.Web.Auth.Discord (thin wrappers)
- Sora.Web.Auth.Saml2 (SAML 2.0 SP)

Expose controller-routed HTTP endpoints only:

- Discovery: GET `/.well-known/auth/providers` → array of ProviderDescriptor
- OIDC/OAuth2: GET `/auth/{provider}/challenge`, GET `/auth/{provider}/callback`
- SAML: GET `/auth/{provider}/saml/metadata`, POST `/auth/{provider}/saml/acs` (SLO optional)

Data model:

- `User` (minimal profile)
- `ExternalIdentity` (UserId, Provider, ProviderKeyHash, ClaimsJson, CreatedUtc) with unique (Provider, ProviderKeyHash)
- `AuditLog` entity for structured audit

Linking policy: links are created only by an authenticated user (no email auto-link). Unlink enforces ≥1 remaining sign-in method for self-service; admin override allowed and audited.

Security defaults:

- OIDC/OAuth2: PKCE, state/nonce, strict callback path binding, cookies Secure+HttpOnly, open-redirect prevention via allow-listed returnUrl.
- SAML: signature/issuer validation required, strict ACS path, clock skew guard, assertion replay protection.

Configuration model (keys abbreviated):

- `Sora:Web:Auth:*` (ReturnUrl, Bff, RateLimit, Tokens, ReConsent)
- `Sora:Web:Auth:Providers:{id}:*` with `Type` = `oidc|oauth2|saml` and protocol-specific settings
- Secrets: default config resolution; optional secret-store adapters via `SecretRef`

Settings composition (defaults + overrides):

- Known adapters (e.g., `google`, `microsoft`, `discord`) ship sane defaults (authorization endpoints, scopes, icons, protocol `Type`).
- Developers may provide only the minimal required keys (typically `ClientId` and `ClientSecret` or `SecretRef`).
- Final options are composed as: adapter defaults ← app defaults (if any) ← `appsettings.json` ← `appsettings.{Environment}.json` ← environment variables. `SecretRef` is resolved last.
- `Type` may be inferred from the adapter when omitted (e.g., `discord` implies `oauth2`). For generic adapters (e.g., arbitrary OIDC), `Type` is required.
- Validation runs on the composed result; missing mandatory fields after composition produce a clear ProblemDetails error.

Example (Discord) — minimal developer config overlays defaults:

```
"Sora:Web:Auth:Providers:discord": {
	"ClientId": "${DISCORD_CLIENT_ID}",
	"ClientSecret": "${DISCORD_CLIENT_SECRET}"
}
```

The adapter supplies: `Type=oauth2`, endpoints, default scopes, display name, and icon.

## Scope

In scope (v1):

- Login, linking, unlinking, discovery endpoint; minimal current user endpoint.
- OIDC (generic) + Google/Microsoft/Discord wrappers; SAML 2.0 SP (metadata + ACS). SLO optional, off by default.
- Ephemeral tokens by default (no persistence).
- Rate limiting with sane defaults.
- DI stores: `IUserStore`, `IExternalIdentityStore` defaulting to Entity<>; replaceable.

Out of scope (v1):

- Multi-tenant scoping (defer to v2).
- Recent-login policy enforcement (design-ready only).
- Advanced admin UX; provide APIs and samples, not UI.

## Consequences

Pros:

- Pluggable providers, minimal DX, strong defaults.
- Single discovery endpoint for mixed OIDC/OAuth2/SAML providers.

Trade-offs:

- Added complexity for SAML (metadata, certs, clock skew); we mitigate by using a mature library in the adapter.
- Multiple packages to maintain; wrappers remain thin.

## Implementation notes

- Constants: centralize route names, scheme names, headers (per ARCH-0040).
- Controllers only; no inline endpoints (per WEB-0035 and engineering guidance).
- Discovery payload (ProviderDescriptor): `{ id, name, protocol, icon, enabled, challengeUrl?, metadataUrl?, scopes? }`.
- ReturnUrl: default `/`; allow-list configurable. RelayState for SAML must pass the same policy.
- ProviderKeyHash: SHA-256 of provider-specific stable identifier, salted by provider id.
- Audit: structured, PII-minimized; retain 90 days hot; redact secrets.
- Icons: resolution order adapter-embedded → app static → generic fallback.
- Rate limits: per-IP challenge/ACS defaults, configurable via options.

## Follow-ups

- Add docs/reference page for configuration patterns and examples.
- Add sample wiring in an existing sample app demonstrating provider discovery and linking UI.
- Add integration tests with a fake OIDC server and local SAML IdP.

## References

- WEB-0035 — EntityController transformers
- ARCH-0040 — Config and constants naming
- ARCH-0041 — Docs posture (instructions over tutorials)
- OPS-0015 — Default configuration fallback
