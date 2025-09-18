---
id: ARCH-0050
slug: secrets-management-and-configuration-resolution
domain: Architecture
status: approved
date: 2025-08-28
title: Secrets management - provider chain, configuration resolution, and orchestration references
---

## Context

Koan needs a first-class, zero-config secrets capability that works consistently across modules (Data, Messaging, Web) and orchestration. The goals are:

- Reference = intent: referencing Koan.Secrets.\* packages enables secrets behavior without extra wiring.
- Safe-by-default: no secret values baked into artifacts or logs; redaction everywhere; rotation-friendly.
- Simple DX: explicit secret references in configuration and a small DI surface for late-bound resolution.
- Orchestration alignment: exporters prefer references over values (secretsRefOnly) and can map to platform-native secret primitives.

We must resolve “boot order” concerns (configuration before DI), preserve rotation semantics, avoid leaking values into configuration trees, and keep orchestration predictable and explainable.

## Decision

We will introduce a secrets module with a provider chain and a configuration-resolution layer that expands secret references on read, not on file load. Key decisions:

1. Configuration resolution (lazy, rotation-aware)

- Add a SecretResolvingConfigurationSource/Provider that wraps IConfiguration at the end of the provider chain. It detects explicit secret references/placeholders and resolves them on read via a resolver.
- Do not materialize resolved values back into the configuration tree. Use change tokens to propagate updates (OptionsMonitor).

2. Two-stage resolver to solve boot order

- Bootstrap resolver (env-first) is used before DI is fully built: Env → .NET User Secrets (Development) → optional OS keychain → provider hints from environment (e.g., VAULT_ADDR).
- After the ServiceProvider is built, upgrade to the DI-backed ISecretResolver composed from referenced adapters. Trigger a configuration reload to update bound options.

3. Provider chain and Reference = Intent

- Referencing Koan.Secrets.Core registers the resolver and default chain. Referencing provider packages (e.g., Koan.Secrets.HashiCorpVault) registers those providers into the chain via KoanAutoRegistrar.
- Default precedence is stable and configurable. Adapters can be forced via explicit scheme (secret+vault://…).

4. Explicit reference syntax (no magic keys)

- Whole-value reference: secret://<scope>/<name>[?version=…]
- Inline placeholder: ${secret://<scope>/<name>[?version=…]}
- Provider-forced schemes: secret+vault://..., secret+k8s://..., secret+sops://...

5. Orchestration exporters prefer references

- When any Koan.Secrets.\* provider is present, exporters set capabilities.secretsRefOnly = true and emit references (envRef) rather than values. Renderers map to platform mechanisms (Kubernetes secretKeyRef/ESO; Compose secrets; optional Vault Agent).
- Sidecars/agents or file-sinks are enabled only under minimal, explicit signals (e.g., Koan:Secrets:Provider=Vault or VAULT_ADDR present), keeping Reference = Intent predictable without surprising topology changes.

6. Safety, health, and observability

- Redaction is enforced centrally. Secret values never appear in logs, traces, exceptions, or OpenAPI examples.
- TTL-aware cache with optional background renewal; grace window during outages. Health checks per provider aggregate into readiness.
- Optional required secrets list can gate readiness at startup (resolve with timeout).

## Scope

In scope

- Secrets abstractions and resolver chain; configuration source for on-read expansion.
- DI wiring via KoanAutoRegistrar; explicit reference syntax and parsing rules.
- Orchestration exporter behavior (secretsRefOnly), envRef contract, and sidecar hints policy.

Out of scope (this ADR)

- Provider-specific details beyond minimal options (Vault/Kubernetes/SOPS follow their own docs if needed).
- Cloud managers (AKV/ASM/GSN) - optional, separate packages.

## Consequences

Positive

- Zero-config in dev; deterministic and safe in prod. Rotation-safe because values are resolved on read and cached by SecretId TTL.
- Consistent DX across modules: same references work for configuration, Options binding, and on-demand API calls.
- Orchestration artifacts are portable and value-free by default.

Tradeoffs / Risks

- Boot-order complexity is addressed by bootstrap→upgrade, but requires careful implementation and tests.
- Slight overhead on read; mitigated via TTL cache and short-lived expansion cache.
- Mixed inline JSON is constrained (JSON secrets must be whole-value references).

## Implementation notes

Contracts (Abstractions)

- ISecretProvider: GetAsync(SecretId id, CancellationToken) → SecretValue; optional TryGet/List/Watch.
- ISecretResolver: GetAsync(SecretId), ResolveAsync(string template) for placeholder expansion.
- SecretId: scope/name with optional version/provider; parse from secret:// URIs.
- SecretValue: data (string/bytes/JSON), metadata (version, created, ttl, provider).
- Exceptions: NotFound, Unauthorized, LeaseExpired, ProviderUnavailable (never include payloads).

Configuration

- AddSecretsReferenceConfiguration() registers SecretResolvingConfigurationSource using the bootstrap resolver.
- After DI build, the source upgrades to the DI-backed resolver and raises a reload token.

DI surface

- services.AddKoanSecrets(o => { /_ options _/ }).UseDefaultChain().AddProvider<T>()
- Providers auto-register via KoanAutoRegistrar; order is deterministic and user-overridable.

Parsing and placeholders

- Expand only well-formed tokens: secret://… and ${secret://…}. For deterministic routing, use secret+provider://…
- Inline expansion supports compound values (e.g., connection strings). JSON secrets should be whole-value references.

Orchestration exporters (capabilities)

- Capabilities.secretsRefOnly=true when any secrets provider is present.
- Emit envRef entries with fields: { name, ref, provider?, version?, mode(env|file)?, path?, required?, reload? }.
- Vault Agent/file-sink hints are opt-in via minimal config/env.

Safety

- Central redaction; SecretValue.ToString throws; identifiers only in logs.
- Metrics and tracing via ActivitySource; counters for cache hit/miss/latency/renewals.

## Follow-ups

- Implement Core + Vault adapter first; add Kubernetes and SOPS next based on demand.
- Add docs/reference/secrets.md with contract, examples, and orchestration envRef mapping.
- Golden tests: redaction, placeholder expansion, rotation/TTL, bootstrap→upgrade, exporter secretsRefOnly.

## References

- ARCH-0046 - Recipes: intention-driven bootstrap and layered config
- ARCH-0047 - Orchestration hosting/providers/exporters as adapters
- ARCH-0048 - Endpoint resolution and persistence mounts
- ARCH-0040 - Config and constants naming
  \*\*\* End Patch
