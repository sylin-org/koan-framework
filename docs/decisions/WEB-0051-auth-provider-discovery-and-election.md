---
id: WEB-0051
slug: auth-provider-discovery-and-election
domain: Web
status: accepted
date: 2025-10-13
title: Auth provider discovery and election parity with data adapters
deciders: Koan maintainers
consulted: Web pillar, Security stewards
---

## Contract

- Inputs: Auth provider adapters (core + connectors), configuration via `Koan:Web:Auth:*`, environment context.
- Outputs: Single elected interactive provider (challenge path + metadata) exposed to middleware and admin surfaces.
- Error modes: No provider discovered, provider disabled in Production due to gating, preferred provider misconfigured, non-interactive protocols (SAML) lacking challenge flow.
- Success criteria: Auth auto-discovery behaves like data adapters—prioritized election, deterministic fallback in Development, clear reporting/logging, and zero manual setup for samples such as `g1c1`.

### Edge Cases

- Production apps without explicit providers remain locked down (no fallback) unless `AllowDynamicProvidersInProduction` is set.
- `PreferredProviderId` references an unknown or disabled provider → election logs warning and falls back to priority order.
- All discovered providers are non-interactive (e.g., SAML-only) → `.koan/admin` responds with 401/403 instead of throwing.
- Development fallback should only surface the TestProvider when no explicit provider wins and the adapter is referenced.

## Context

Data adapters already self-register, advertise capabilities, and are elected using priority + health checks. The Web auth pipeline still required manual configuration (e.g., S5 sample env vars) and default challenge schemes were unset, producing runtime exceptions (g1c1). We need functional parity so Koan apps can opt into auth simply by referencing adapters.

## Decision

- Introduce `IAuthProviderElection` that evaluates discovered providers, honors environment gating, and elects a single interactive provider.
- Extend `ProviderOptions` with optional `Priority` and add `PreferredProviderId` to `AuthOptions`; contributors (Google, Microsoft, Discord, Test) now set sane priorities.
- Cookie auth middleware now sets authenticate/challenge defaults and redirects to the elected provider’s `/auth/{id}/challenge` endpoint, preserving return URLs and falling back to JSON 401/403 for API requests.
- Development fallback: when no provider is explicitly enabled, the TestProvider is auto-elected (marked `IsFallback`) so `.koan/admin` works out-of-the-box. Production keeps the previous safety rules.
- Boot logs record the election result (`id`, `protocol`, `priority`, fallback flag, reason) to mirror data adapter reporting.

## Implementation

1. `AuthProviderElection` (scoped) builds candidates from `IProviderRegistry`, applies priority ordering (explicit > higher priority > lexical), and emits `AuthProviderSelection` with challenge URL + reason.
2. `ProviderOptions.Priority` flows through contributors, registry merge, and discovery descriptors; `ProviderDescriptor` now surfaces the numeric priority for diagnostics.
3. `AuthOptions` gains `PreferredProviderId`, allowing ops to pin a provider without editing code; misconfigurations log warnings and fall through to default selection.
4. Cookie middleware intercepts login redirects, resolves the desired return URL, and targets the elected provider. JSON callers keep status codes instead of redirects.
5. Koan Admin now auto-creates a permissive `KoanAdmin` policy in Development when none exists (toggleable), so samples like `g1c1.GardenCoop` light up the admin UI without extra configuration.

## Alternatives Considered

- Keep manual `Configure<AuthenticationOptions>` overrides inside `Koan.Web.Auth.Connector.Test`: rejected, because it referenced a non-existent scheme and failed parity goals.
- Register full ASP.NET Core handlers per provider (OIDC/OAuth) at boot: deferred; Koan currently centralizes challenge/callback logic in `AuthController`.

## Consequences

Positive:
- Consistent “drop-in adapter” experience across Data and Web pillars.
- Samples and greenfield apps no longer need to hand-author `Koan__Web__Auth__TestProvider__*` env vars for Development.
- Diagnostics improve: admin surfaces and logs explicitly state which provider was elected and why.

Trade-offs / Risks:
- Existing apps that relied on manual redirects should validate the new cookie event behavior.
- Providers without interactive flows (SAML) remain unsupported for challenge redirects until downstream work implements their UI flow.

## Follow-up Work

- Extend provider contributors to advertise capability flags (interactive vs. non-interactive) and surface them via the discovery endpoint.
- Add integration tests covering multi-provider elections, preferred provider overrides, and Production gating warnings.
- Update Web reference docs (`docs/api/web-http-api.md`) with the new config knobs (`PreferredProviderId`, priority) and election behavior when we broaden coverage.

## References

- ARCH-0044 – Standardized module config and discovery
- WEB-0044 – Web auth discovery and health
- WEB-0045 – Auth provider adapters shipped as modules
- DATA-0061 – Data adapter pagination/streaming parity
