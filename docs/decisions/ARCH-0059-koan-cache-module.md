```markdown
---
id: ARCH-0059
slug: koan-cache-module
domain: Architecture
status: approved
date: 2025-10-06
title: Koan Cache Module and Adapter Architecture
---

> **Contract**
>
> - **Inputs:** Koan.Core auto-registration, Entity<T> data surface, Koan data connector capability model, and application scenarios requiring deterministic cache behaviours.
> - **Outputs:** A first-class cache pillar (Koan.Cache, Koan.Cache.Abstractions, provider adapters, testing harness) with declarative registration, policy discovery, and DX-aligned fluent APIs.
> - **Failure modes:** Divergent caching patterns across modules, provider-specific leaks, cache-induced stale data, or configuration drift between cache and data adapters.
> - **Success criteria:** Developers opt into caching via package reference + `AddKoanCache()`, policies are centrally discoverable, providers expose capabilities through a unified contract, and instrumentation/health checks align with other Koan pillars.

## Edge Cases We Must Handle

1. Environments without Docker or external cache endpoints (tests must gracefully fall back to in-memory fixtures).
2. Providers lacking feature parity (pub/sub, compare/exchange, tagging) requiring predictable downgrades.
3. Concurrent writers attempting the same cache key — avoid duplication via singleflight coordination.
4. Large payloads (streams, vectors) that strain JSON serializers or memory when materialised eagerly.
5. Multi-tenant or scoped applications needing deterministic region/prefix handling and deterministic invalidations.

## Context

Koan applications currently duplicate caching logic via ASP.NET `IMemoryCache`, third-party Redis clients, or bespoke wrappers. These approaches break the "reference = intent" posture, lack capability discovery, and cannot be governed by Koan's policy registry. The new `Koan.Cache` module family introduces:

- `Koan.Cache.Abstractions` defining contracts (`ICacheStore`, `CacheCapabilities`, `CachePolicyAttribute`, serializers).
- `Koan.Cache` implementing fluent builders, `CacheClient`, policy registry, singleflight dedupe, instrumentation, and DI extensions.
- Provider adapters (initially memory + Redis) adopting the same capability model as data connectors.
- Testing harnesses enabling deterministic coverage for adapters and policy registries.

This ADR formalises architecture, guardrails, and integration expectations for the cache pillar, complementing the architectural brief in `docs/architecture/koan-cache-module.md`.

## Decision

We approve `Koan.Cache` as a first-class Koan module, with the following binding requirements:

- **Entity-first integration:** Cache policies attach to `Entity<T>`, `EntityController<T>`, Canon pipelines, and instruction interceptors using declarative attributes and registry entries.
- **Auto-registration only:** Bootstrap must occur through `services.AddKoanCache()` and `services.AddKoanCacheAdapter("<provider>")` inside a module's `KoanAutoRegistrar`. Manual DI wiring is prohibited.
- **Capability transparency:** Every `ICacheStore` exposes `CacheCapabilities` so higher layers branch safely when providers lack certain features.
- **Unified fluent API:** Consumers use `Cache.WithJson(...)`, `Cache.WithRecord<T>(...)`, or injected `ICacheClient` instead of bespoke helper classes or repositories.
- **Instrumentation baseline:** Cache metrics/logs follow Koan telemetry conventions (OpenTelemetry counters, structured key prefix logging) and health checks validate provider reachability during boot.

## Architectural Outcomes

- **DX Cohesion:** Package references now express caching intent, maintaining the Koan principal that references imply functionality. CLI tooling can surface cache capabilities during module inventory.
- **Policy Governance:** `CachePolicyRegistry` unifies discovery for attributes across entities, controllers, and pipelines, allowing canonical invalidation flows and preventing duplicated key formatting logic.
- **Provider Reuse:** Adapters reuse existing data connector infrastructure (configuration resolution, connection pooling, diagnostics). Additional providers must implement `ICacheStore` atop Koan connectors rather than external SDKs.
- **Resilience:** Singleflight and stale-while-revalidate behaviours are centrally managed, avoiding inconsistent throttling or duplicate upstream load.

## Implementation Guardrails

1. **Contracts First:** All cache operations go through `CacheEntryBuilder<T>` and `CacheEntryOptions`. Providers may extend capability hints but cannot introduce bespoke option types.
2. **No Repository Pattern:** Services must consume `Entity<T>` statics + cache builders, never introduce custom cache repositories or manual DI factories.
3. **Configuration Discovery:** Provider selection resolves via `Configuration.Read(...)`, respecting `Cache:Provider`, `Cache:Redis:*`, etc., with constants defined in `CacheConstants`.
4. **Policy Resolution:** Global configuration (e.g., `Cache:Policies:*`) defines defaults/overrides, while `[CachePolicy]` attributes capture per-entity or per-action intent. The registry merges both sources with documented precedence (explicit configuration overrides attribute data when conflicts occur).
5. **Testing Expectations:** Each adapter satisfies the shared trait tests in `tests/Koan.Cache.Tests`. Integration suites (Redis) must degrade gracefully when containers are unavailable.
6. **Documentation Pairing:** Architecture and user guides describe policy usage, capability negotiation, and integration flows; updates must remain in sync with this ADR.

## Configuration Strategy

- Introduce strongly typed options for global cache policies, enabling administrators to set enablement, key templates, and overrides via `appsettings`.
- Merge configuration-provided policies with attribute metadata inside `CachePolicyRegistry`, ensuring deterministic precedence and avoiding duplicate definitions.
- Surface configuration state through diagnostics so teams can audit effective policies at runtime.

## Redis Compare-and-Exchange Scope

Compare-and-exchange (CAS) support is part of the MVP delivery. The Redis adapter must implement atomic operations (Lua scripts or `StringSet` with `When`) for CAS semantics, expose the capability flag, and ship integration tests that demonstrate correctness under contention.

## Diagnostics Integration

Cache diagnostics (policy inventory, capability matrices, hit/miss metrics) are folded into the existing Koan diagnostics pipeline. `CacheOptions.EnableDiagnosticsEndpoint` gates registration with the shared diagnostics module rather than introducing a standalone endpoint.

## Observability & Operations

- Emit metrics: `koan.cache.hits`, `koan.cache.misses`, `koan.cache.fetch.duration`. Attach key prefixes (never full keys) and provider names as dimensions.
- Health checks: `AddKoanCache()` registers a contributor that pings the selected provider and surfaces capability mismatches.
- Diagnostics: When diagnostics are enabled, cache policies, capability matrices, and adapter registration surface through the shared Koan diagnostics endpoint—no bespoke cache endpoint is created.

## Alternatives Considered

- **Status quo (per-app caching):** Rejected due to duplicated logic, inconsistent DX, and no capability model.
- **Direct dependency on `IMemoryCache`/`IDistributedCache`:** Rejected because it breaks provider transparency, lacks tagging/pub-sub semantics, and conflicts with Koan auto-registration posture.
- **Embed caching inside `Koan.Data.Core`:** Rejected to keep storage and caching concerns separated while sharing capability descriptors via contracts.

## Consequences

### Positive

- Unified cache ergonomics with minimal developer ceremony.
- Declarative policy governance enabling Canon, Web, and Data pillars to share invalidation semantics.
- Reusable instrumentation and health checks aligned with existing Koan telemetry.

### Trade-offs

- Additional abstractions require disciplined adherence to `CacheCapabilities` to avoid feature leaks.
- Integration tests rely on Testcontainers; environments without Docker must override via explicit connection strings or rely on memory adapter.
- Requires continuous maintenance of adapter capability matrices as new providers are added.

### Neutral

- Existing modules not adopting caching remain unaffected until they reference `Koan.Cache`.
- Entity-first patterns remain intact; cache opt-in augments rather than replaces data access flows.

## Follow-ups

- Update module recipes and CLI scaffolding to include optional cache configuration manifests (deferred beyond MVP).
- Finalise developer guide (`docs/guides/cache-policies-howto.md`, forthcoming) with policy authoring walkthroughs.
- Resolve Testcontainers/Docker.DotNet version gap causing `MissingMethodException` during Redis fixture startup.
- Evaluate additional adapters (Couchbase, Hazelcast) using the approved capability contract.

## References

- [Koan Cache Module Architecture](../architecture/koan-cache-module.md)
- [Koan.Cache.Abstractions contracts](../../src/Koan.Cache.Abstractions)
- [Koan.Cache implementation](../../src/Koan.Cache)
- [Redis integration tests](../../tests/Koan.Cache.Adapter.Redis.IntegrationTests)
- [OPS-0048 Standardized Docker probing for tests](OPS-0048-standardize-docker-probing-for-tests.md)
```
