---
id: WEB-0045
slug: auth-provider-adapters-separate-projects
domain: Web
status: accepted
date: 2025-08-23
title: Auth provider adapters shipped as separate self-registering modules
---

Context

- Sora mandates Separation of Concerns and clean module boundaries.
- Early Sora.Web.Auth builds hard-coded provider defaults (Google, Microsoft, Discord) inside the core auth module.
- This made the core aware of adapter-specific metadata and violated the separation rule.

Decision

- Extract each provider into its own project/package:
  - Sora.Web.Auth.Google (OIDC)
  - Sora.Web.Auth.Microsoft (OIDC)
  - Sora.Web.Auth.Discord (OAuth2)
  - Sora.Web.Auth.Oidc (generic handler; no defaults)
- Each adapter self-registers via ISoraAutoRegistrar and contributes defaults through IAuthProviderContributor.
- The core registry (Sora.Web.Auth) no longer carries any hard-coded defaults or brand knowledge.
- Production gating: dynamic providers (those contributed by adapters without explicit appsettings) are disabled by default in Production unless either:
  - Sora:Web:Auth:AllowDynamicProvidersInProduction=true, or
  - Sora:AllowMagicInProduction=true.

Scope

- Applies to Sora.Web.Auth and all provider adapters listed above.
- Samples updated to reference provider modules explicitly; no appsettings are required for providers to appear in Development.

Consequences

- Cleaner core; providers evolve independently; optional packaging and versioning per adapter.
- Discovery remains stable; defaults supplied by adapters; health evaluation unchanged.
- In Production, adapter-supplied providers remain disabled unless explicitly allowed as per gating flags.

Dev-only provider

- A separate adapter `Sora.Web.Auth.TestProvider` offers an in-process OAuth2 test IdP for local runs. It self-registers in Development (or when explicitly enabled via `Sora:Web:Auth:TestProvider:Enabled=true`) and contributes a `test` OAuth2 provider with built-in endpoints. No appsettings are necessary for local discovery; credentials default to `test-client` / `test-secret` and may be overridden. The adapter logs a warning if enabled outside Development.

Implementation notes

- ProviderRegistry.Compose now aggregates only contributors and user config; no built-in defaults.
- New projects register IAuthProviderContributor with DI via TryAddEnumerable.
- S6.Auth references provider projects and can display providers without any appsettings entries.

Follow-ups

- Implement handler wiring inside OIDC/OAuth2 modules (challenge/callback).
- Document adapter packaging and versioning in docs/reference when stabilized.

References

- ARCH-0040 config and constants naming
- WEB-0043 auth multi-protocol
- WEB-0044 web auth discovery and health
