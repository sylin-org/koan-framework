---
uid: reference.modules.koan.secrets.abstractions
title: Koan.Secrets.Abstractions – Technical Reference
description: Contract types and primitives that standardize secret identifiers, payload handling, and provider interactions in Koan applications.
since: 0.6.3
packages: [Sylin.Koan.Secrets.Abstractions]
source: src/Koan.Secrets.Abstractions/
validation:
  date: 2025-09-29
  status: verified
---

## Contract

- Provide immutable identifiers (`SecretId`) and metadata carriers (`SecretMetadata`) so secret requests flow consistently across providers and resolvers.
- Encapsulate secret payloads via `SecretValue`, including typed projections (UTF-8 strings, JSON) and opaque binary access that never logs the actual content.
- Define asynchronous contracts for fetching secrets (`ISecretProvider`) and performing templated resolution (`ISecretResolver`) used by `Koan.Secrets.Core` and downstream providers.
- Express failure conditions through dedicated exception types (`SecretNotFoundException`, `SecretUnauthorizedException`, `SecretProviderUnavailableException`) that align with provider retry logic and diagnostics.

## Architectural role

`Koan.Secrets.Abstractions` sits at the boundary between application code and concrete secret providers:

- **Consumers** (_callers_) depend on the abstractions to request secrets without binding to a storage backend (Vault, Kubernetes, Azure Key Vault, etc.).
- **Providers** implement `ISecretProvider` (and optionally participate in templated resolution) to plug into the Koan secrets pipeline discovered through `Koan.Secrets.Core` auto-registration.
- **Resolvers** orchestrate providers and perform interpolation by implementing `ISecretResolver.ResolveAsync`, leveraging `SecretId.Parse` for URI-style inputs (`secret://scope/name`).

## Type inventory

| Type                        | Responsibility                                                                                                                                                                     |
| --------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `SecretId`                  | Canonical identifier with scope, name, optional version, and provider hint. Supports parsing from `secret://` and `secret+provider://` URIs and renders back to canonical strings. |
| `SecretValue`               | Holds the raw payload and metadata. Provides `AsBytes()`, `AsString()`, and `AsJson<T>()` projections (UTF-8 for textual forms). Never exposes the payload in `ToString()`.        |
| `SecretMetadata`            | Optional details (version, creation timestamp, TTL, provider id) used by rotation tooling and health checks.                                                                       |
| `SecretContentType`         | Enumerates payload shapes: `Text`, `Bytes`, `Json`. Governs projection behavior in `SecretValue`.                                                                                  |
| `ISecretProvider`           | Minimal contract for fetching a secret by `SecretId`. Providers implement retries, caching, or transport concerns.                                                                 |
| `ISecretResolver`           | Extends provider capabilities with string templating (`ResolveAsync`) that can replace `{{secret://...}}` placeholders and fallback to raw strings when no markers are present.    |
| `SecretException` & friends | Shared base plus targeted exceptions for not-found, unauthorized, and provider-outage scenarios. Providers and resolvers throw these to steer caller retry / error handling.       |

## Flow highlights

1. **Compose identifiers** – Callers construct `SecretId` directly or via `SecretId.Parse("secret://app/api-key?version=blue")`. Optional provider hints (`secret+vault://...`) steer multi-provider deployments.
2. **Fetch payload** – Resolvers invoke `ISecretProvider.GetAsync`. Providers return a `SecretValue` with accurate `SecretContentType` and metadata (version, TTL).
3. **Consume data** – Callers choose the right projection based on content type (e.g., `AsJson<ApiKeyConfig>()`). `SecretValue` throws `InvalidOperationException` if projections mismatch the declared type, preventing silent corruption.
4. **Handle errors** – When providers throw a `SecretException` derivative, higher layers map responses: not found → 404 equivalent, unauthorized → audit logs, provider unavailable → fallback/resilience policies.
5. **Template resolution** – `ISecretResolver.ResolveAsync` accepts strings containing secret tokens and replaces each `secret://` URI with the resolved payload (string projection). Plain strings without tokens short-circuit to avoid unnecessary provider calls.

## Error handling

- `SecretNotFoundException` signals missing material; callers may retry after creating/rotating the secret.
- `SecretUnauthorizedException` indicates the runtime identity lacks access. Providers should include sufficient context for auditing.
- `SecretProviderUnavailableException` captures connectivity or backend outages and can include an explanatory message.
- `SecretId.Parse` throws `ArgumentException` when URIs are malformed, unsupported schemes are used, or the scope/name pair is missing.
- `SecretValue.AsJson<T>` and `AsString()` guard against incorrect content type usage to prevent leaking binary secrets into textual contexts.

## Extension considerations

- Providers can extend `SecretMetadata` by supplying additional key/value pairs via companion options classes; the abstractions deliberately keep metadata lightweight to stay provider-agnostic.
- `SecretId.Provider` exists so orchestrators can route requests to specialized providers (`secret+vault://`, `secret+config://`). If null, selection falls back to `Koan.Secrets.Core` routing heuristics.
- `SecretValue` intentionally accepts externally constructed metadata, enabling providers to attach rotation timestamps or TTL hints for downstream caching.

## Edge cases

- Empty or whitespace secret URIs fail fast in `SecretId.Parse`—validate inputs before user composition.
- Hostless URIs (`secret:///scope/name`) parse correctly; the helper reconciles scope/name from either host or path segments.
- Multi-segment names: only the first path segment after scope is treated as the secret name to keep identifiers concise. Additional segments should be encoded in scope or metadata if needed.
- JSON secrets must be UTF-8 encoded. Providers returning a different encoding should normalize before constructing `SecretValue`.
- Templated strings without secret tokens return the original value, avoiding extra allocations or provider calls.

## Validation notes

- Reviewed `SecretId`, `SecretValue`, `SecretMetadata`, `SecretContentType`, `ISecretProvider`, `ISecretResolver`, and exception types on 2025-09-29.
- Exercised URI parsing scenarios (provider-qualified, hostless, missing segments) using the included unit tests in `tests/Koan.Secrets.Core.Tests`.
- Validated templated resolution behavior indirectly through `Koan.Secrets.Core` resolver tests (`SecretsResolutionTests`), ensuring `ResolveAsync` gracefully handles strings without secrets.
- DocFX strict build (`docs:build`) previously run for the secrets documentation sweep; rerun when integrating additional provider docs.
