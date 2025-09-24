---
id: ARCH-0051
slug: secrets-dev-alignment-and-provider-swap-guardrails
domain: Architecture
status: approved
date: 2025-08-28
title: Secrets - dev alignment and provider-swap guardrails
---

## Context

Developers often lack access to production secret backends (e.g., Vault) during local development. We want application code and configuration to behave consistently when Recipes swap a dev-time source for a real provider at deploy time, avoiding “works in dev, breaks in prod” drift.

This ADR clarifies guardrails that minimize behavior dissonance across environments and sets the default posture for development: prefer existing configuration-backed sources (User Secrets/.env/environment) without inventing bespoke dev secret stores.

## Decision

Adopt provider-agnostic references and a stable resolver chain across environments, with unified error and metadata semantics. Prefer leveraging .NET User Secrets via the existing Configuration provider in Development; only introduce a dev-specific Secrets provider if a concrete bootstrap gap requires it.

Key guardrails:

1. Provider-agnostic references by default

- Use secret://scope/name (and inline ${secret://…}) in configuration. Reserve secret+provider://… only when the specific backend must be enforced. Forced schemes never fall back.

2. Stable chain order across envs

- Default precedence: [Vault (if present), Configuration (User Secrets/.env/appsettings), Environment]. Keep the order stable across Development/CI/Prod; Recipes add or remove providers but do not reorder them.

3. Canonical key mapping

- Normalize secret://scope/name → Secrets:scope:name for configuration-backed sources. Use one normalizer (in Abstractions) so all providers resolve the same canonical keys.

4. Unified error semantics

- Map provider errors into domain exceptions: NotFound → try next provider (unless scheme is forced), Unauthorized/ProviderUnavailable → fail fast. Do not log payloads.

5. Normalized metadata and TTL

- Providers may emit version/ttl; the resolver uses provider TTL when present, else a configured default. App code doesn’t branch on provider-specific metadata.

6. Consistent reload behavior

- The configuration wrapper remains provider-agnostic. Push-capable providers raise change tokens; others rely on TTL expiry. Options binding sees a uniform reload surface.

7. Redaction and logging parity

- Redaction is centralized. Secret values never appear in logs, traces, or exceptions, regardless of provider.

Dev posture:

- Prefer no new dev provider. In Development, wire AddUserSecrets() into IConfiguration; the existing ConfigurationSecretProvider participates in the chain. Optionally support .env and environment variables. If a dev-only provider is ever added, it must be hard-gated to Development and fail closed in non-Development.

## Scope

In scope

- Reference/chain guardrails and the dev posture for secrets.
- Recipe behavior for provider swap without config edits.

Out of scope

- Implementing new providers; provider-specific features (covered by their own docs/ADRs if needed).

## Consequences

Positive

- Recipes can swap providers with minimal surprises. Configuration doesn’t change between dev and prod.
- Clear, deterministic precedence and error handling.
- Reduced maintenance by reusing existing configuration sources in Development.

Tradeoffs

- Advanced backend features (e.g., Vault versioning/leases) are flattened to a common contract unless explicitly opted into via forced schemes.
- TTL-based refresh in dev won’t perfectly mimic push-based rotation.

## Implementation notes

- Keep the default chain order identical across environments; only presence varies.
- Ensure secret+provider:// never falls back to the next provider on NotFound.
- Provide a shared compliance test suite for providers: normalization, error mapping, TTL, placeholder expansion, redaction.
- Documentation should steer developers toward secret://, AddUserSecrets in Development, and Recipes-based provider enablement.

## References

- ARCH-0050 - secrets management and configuration resolution
- ARCH-0046 - Recipes: intention-driven bootstrap and layered config
