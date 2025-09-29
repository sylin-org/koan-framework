---
id: WEB-0044
slug: web-auth-discovery-and-health
domain: WEB
status: Accepted
date: 2025-08-23
title: Koan.Web.Auth discovery payload and initialization reporting
---

## Context

We introduced Koan.Web.Auth to support multiple authentication protocols (OIDC, OAuth2, SAML). Provider discovery (a single well-known endpoint) powers the client UX. We also want reliable runtime visibility during startup about which providers are detected and their protocol types to aid configuration and ops.

On top of the initial design (WEB-0043), we need:

- Initialization logs to explicitly list detected providers with type (e.g., "Google (OIDC), Discord (OAuth)").
- Discovery payload to indicate a provider health state so UIs can disable or annotate providers when configuration is incomplete.

## Decision

- Extend the bootstrap report for Koan.Web.Auth to include:
  - `Providers=<count>` and `DetectedProviders=<comma-separated list>` using friendly names and types.
  - Fallback to well-known adapters when no explicit configuration is present.
- Extend `ProviderDescriptor` shape returned by `GET /.well-known/auth/providers` with:
  - `state: "Healthy" | "Unhealthy" | "Unknown"`.
- Define minimal static health checks per protocol for v1:
  - OIDC: `Authority`, `ClientId`, and one of `ClientSecret` or `SecretRef` must be present.
  - OAuth2: `AuthorizationEndpoint`, `TokenEndpoint`, `ClientId`, and one of `ClientSecret` or `SecretRef`.
  - SAML: `EntityId`, and one of `IdpMetadataUrl` or `IdpMetadataXml`.
  - Unknown protocols: `Unhealthy` if `Enabled=true`, else `Unknown`.

## Scope

- Affects module initialization (boot report) and discovery response only.
- No external network calls are made as part of health computation in v1 to keep startup fast and deterministic.

## Consequences

Pros:

- Clearer operator/developer visibility at startup.
- Client UIs can present only viable sign-in options.

Cons/Trade-offs:

- Static health checks can produce false positives (e.g., wrong values but present). A future active probe mode could mitigate.

## Implementation notes

- Initialization: implemented in `Koan.Web.Auth/Initialization/KoanAutoRegistrar.cs` by composing display name and protocol type from configured providers. Defaults for known providers are supplied by separate adapter modules via `IAuthProviderContributor` (see WEB-0045), not by the core.
- Discovery: `ProviderDescriptor` adds `state`, computed in `ProviderRegistry.EvaluateHealth(...)` based on minimal fields per protocol.
- Schema after change:
  - `{ id, name, protocol, enabled, state, icon?, challengeUrl?, metadataUrl?, scopes? }`.

## Follow-ups

- Optional: add an `active` probe mode that performs a HEAD/GET to relevant endpoints (OIDC discovery, OAuth2/token) gated by a config knob.
- Tie into `/.well-known/Koan/observability` snapshot once standardized in WEB to expose auth discovery there too.

## References

- WEB-0043 - Multi-protocol authentication (OIDC/OAuth2/SAML)
- ARCH-0040 - Config and constants naming
- ARCH-0041 - Docs posture (instructions over tutorials)
