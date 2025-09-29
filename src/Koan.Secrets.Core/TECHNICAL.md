---
uid: reference.modules.koan.secrets.core
title: Koan.Secrets.Core – Technical Reference
description: Secrets runtime that chains providers, manages caching, and resolves configuration placeholders for Koan applications.
since: 0.6.3
packages: [Sylin.Koan.Secrets.Core]
source: src/Koan.Secrets.Core/
validation:
  date: 2025-09-29
  status: verified
---

## Contract

- Provide a DI-friendly secrets runtime that discovers providers, caches results, and exposes `ISecretResolver` for application code.
- Expand configuration builders with a resolver-aware provider so `${secret://...}` and `secret://` placeholders hydrate automatically.
- Offer a bootstrap chain (environment variables + configuration values) that works before dependency injection is assembled, then upgrade to the container-backed resolver at runtime.
- Surface predictable caching semantics so rotation policies, multi-provider fallbacks, and diagnostics behave consistently across environments.

## Key components

| Component | Responsibility |
| --- | --- |
| `ServiceCollectionExtensions.AddKoanSecrets` | Registers `SecretsOptions`, memory cache, default providers (`EnvSecretProvider`, `ConfigurationSecretProvider`), and the `ChainSecretResolver`; returns `ISecretsBuilder` so callers can append providers. |
| `SecretsBuilder` / `ISecretsBuilder` | Lightweight builder that adds additional `ISecretProvider` implementations to the chain. |
| `SecretsOptions` | Holds runtime defaults, currently `DefaultTtl`, used when providers omit TTL metadata. |
| `ChainSecretResolver` | Core resolver that checks the cache, walks the provider chain, caches results per TTL, and expands placeholders within strings. |
| `SecretResolvingConfigurationSource` | Wraps an existing `IConfiguration` provider, resolving `${secret://}` templates and whole-value URIs; supports upgrading from bootstrap to DI resolver. |
| `SecretResolvingConfigurationExtensions` | Adds bootstrap configuration providers and exposes `UpgradeSecretsConfiguration(IServiceProvider)` to swap in the DI resolver after the host builds. |
| `EnvSecretProvider` / `ConfigurationSecretProvider` | Built-in providers that serve secrets from environment variables or the `Secrets:<scope>:<name>` configuration tree. |
| `Initialization.KoanAutoRegistrar` | Auto-registers the runtime when the package is referenced and records resolver topology in the Koan boot report. |

## Runtime flow

1. **Bootstrap configuration** – During host construction, `AddSecretsReferenceConfiguration()` wraps the active configuration, enabling `${secret://...}` placeholders and whole-value `secret://` URIs using a bootstrap resolver that includes environment variables and configuration-backed secrets.
2. **Register services** – Calling `services.AddKoanSecrets()` adds the memory cache, default providers, and the `ChainSecretResolver`. Consumers can append custom providers via `ISecretsBuilder.AddProvider<T>()`.
3. **Build host** – After `builder.Build()`, `UpgradeSecretsConfiguration(app.Services)` replaces the bootstrap resolver with the DI-managed resolver and triggers configuration reload tokens so options rebind with hydrated values.
4. **Resolve secrets** – Application code injects `ISecretResolver` and calls `GetAsync` for structured access or `ResolveAsync` for template expansion. Results are cached under the stringified `SecretId` until `SecretMetadata.Ttl` (or `SecretsOptions.DefaultTtl`) expires. Providers are queried sequentially until one succeeds or all throw `SecretNotFoundException`.

## Configuration & extension points

- **Caching defaults** – `SecretsOptions.DefaultTtl` provides a fallback TTL when providers omit `SecretMetadata.Ttl`. Set per application based on rotation cadence.
- **Custom providers** – Implement `ISecretProvider` and register with `AddProvider<T>()` to extend beyond env/config. Place faster providers earlier in the chain to minimise latency.
- **Configuration upgrades** – Always call `SecretResolvingConfigurationExtensions.UpgradeSecretsConfiguration(app.Services)` after the container is built so configuration consumers pick up the DI resolver.
- **Logging** – `ChainSecretResolver` accepts an optional `ILogger` to track cache hits/misses and provider fallbacks; inject logging via DI to aid troubleshooting.
- **Options binding** – Because configuration providers trigger reload tokens during upgrade, secrets-bound options automatically refresh without application restarts.

## Failure handling & diagnostics

- Provider lookups swallow `SecretNotFoundException` to continue down the chain; all other exceptions bubble up and surface in logs.
- The runtime caches successful results; failures are not cached to allow recovery when providers become available.
- The Koan auto registrar records resolver topology (“env → config → adapters”) in the boot report, enabling operators to confirm which providers are active at boot.
- In configuration sources, unresolved placeholders return the original template, allowing the app to decide whether to fail fast or continue with fallback behaviour.

## Edge cases

- Hostless URIs (`secret:///scope/name`) normalise to the expected scope/name pair and resolve like `secret://scope/name`.
- Provider-qualified URIs (e.g., `secret+vault://team/key`) carry the provider hint through `SecretId.Provider`; downstream providers can inspect it to route requests appropriately.
- Multiple occurrences of the same placeholder in a string reuse the cached value within the current resolution call, preventing duplicate provider accesses.
- When providers omit TTL metadata, the default TTL ensures values eventually refresh; set it low for hot-rotation secrets such as credentials.
- During bootstrap, only environment and configuration providers are available. Register additional providers via DI to ensure fully hydrated values after upgrade.

## Validation notes

- Code paths reviewed: `DI/ServiceCollectionExtensions.cs`, `Resolver/ChainSecretResolver.cs`, `Configuration/SecretResolvingConfigurationSource.cs`, `Providers/*.cs`, and `Initialization/KoanAutoRegistrar.cs` (commit date 2025-09-29).
- Automated checks:
  - `dotnet test tests/Koan.Secrets.Core.Tests` (cache refresh, placeholder resolution, upgrade behaviour).
  - `pwsh -File scripts/build-docs.ps1 -ConfigPath docs/api/docfx.json -LogLevel Warning -Strict`.
