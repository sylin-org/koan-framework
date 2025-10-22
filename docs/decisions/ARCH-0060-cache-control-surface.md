```markdown
---
id: ARCH-0060
slug: cache-control-surface
domain: Architecture
status: approved
date: 2025-10-07
title: Cache Control Surface Helpers and Entity Policy Integration
---

> **Contract**
>
> - **Inputs:** Koan.Cache client abstractions, cache policy registry metadata, Entity<TEntity> lifecycles, and developer requests for targeted cache invalidation diagnostics.
> - **Outputs:** Unified helpers for checking cache state (`Exists`), enumerating/invalidating tagged entries (`CacheTagSet` facade + tag-aware client APIs), and entity-level controls that honour declared cache policies.
> - **Failure modes:** Helpers leaking provider-specific behaviour, inconsistent tag resolution between policies and runtime helpers, or additional Async suffixes contradicting DX decisions.
> - **Success criteria:** Applications can query cache state and flush policy-derived tag groups via terse, synchronous method names; entity helpers re-use policy metadata without bespoke wiring; adapters expose existence and tag enumeration consistently.

## Edge Cases We Must Handle

1. Tags defined by policies that include templated tokens (`{TenantId}`) must be ignored by static flush/count helpers to avoid accidental wildcard removal.
2. Providers without tag support (or stale metadata) should short-circuit gracefully, returning zero without failures.
3. Expired entries discovered during tag enumeration should be evicted opportunistically to keep tag indexes clean.
4. `Exists` must respect stale-while-revalidate semantics—return `true` when stale payloads are still serviceable.
5. Entity cache helpers must tolerate the cache subsystem being absent (clear exception with remediation guidance).

## Context

The previous cache MVP exposed fluent entry builders (`Cache.WithJson("key")...`) but lacked ergonomic controls for introspecting or invalidating cached data. Downstream teams needed three additions:

- **Existence checks** without materializing payloads to drive cache-warm analytics.
- **Bulk tag flush/count helpers** to attach to maintenance workflows and admin tooling.
- **Entity-aware shortcuts** that derive tag lists directly from `[CachePolicy]` metadata instead of copy/pasted constants.

Developers also asked that the new helpers omit the `Async` suffix while still returning `ValueTask` for composability.

## Decision

We extend the cache surface with the following components:

- `ICacheStore.ExistsAsync` enabling adapters to implement efficient key lookups (Redis = `KEYEXISTS`, memory = in-process index check).
- `ICacheClient.FlushTagsAsync` / `CountTagsAsync` consuming tag enumerators and deduplicating keys across tags.
- Fluent façade updates:
  - `Cache.Exists("key")`
  - `Cache.Tags("tag")` returning a `CacheTagSet` with `.Flush()`, `.Count()`, `.Any()` (all sans `Async`).
  - `ICacheEntryBuilder<T>.Exists(ct)` for per-entry probes.
- `Entity<TEntity, TKey>.Cache` partial helper using `ICachePolicyRegistry` + `ICacheClient` pulled from `AppHost` to flush or count policy-derived tags.

All new public helpers drop the `Async` suffix while remaining asynchronous under the hood.

## Architectural Outcomes

- **Policy-first invalidation:** Entity cache helpers reuse the single source of truth (policy registry) for tag discovery, ensuring controller/entity invalidation flows stay consistent.
- **Provider parity:** New store/client contracts are minimal and map to primitives available on both memory and Redis adapters. Providers that cannot offer tag enumeration can still return zero without throwing.
- **DX alignment:** Call sites gain simple, synchronous method names that reflect intent (`Cache.Tags("todo").Flush()`) while still returning `ValueTask` for efficient awaits.
- **Operational insight:** Tag-level counts allow operators to verify cache population before bulk invalidations, and existence checks unlock lightweight probes.

## Implementation Notes

1. `ICacheStore.ExistsAsync` implementations re-use existing deserialization to respect stale windows and clean up invalid envelopes.
2. Tag-based helpers deduplicate keys via `HashSet<CacheKey>` to avoid double eviction when multiple tags reference the same cache entry.
3. Expired entries discovered during enumeration trigger immediate removal to keep tag indexes healthy.
4. `EntityCacheAccessor` filters out tags containing `{}` placeholders to avoid malformed flushes; callers can supply additional explicit tags per invocation.
5. Cache façade overloads accept `IEnumerable<string>` to make composition straightforward for admin tooling.
6. Tests cover memory adapter existence checks, `CacheClient` tag operations, and entity helper behaviour with mock policy/client services.

## Consequences and Follow-ups

- Adapters beyond memory/Redis must implement `ExistsAsync` when ported to the new contract; capability documents should be updated accordingly.
- Future work may introduce structured responses (e.g., per-tag counts) once we have consumer scenarios beyond binary counts.
- Admin tooling can now layer on these helpers without touching provider APIs directly.
```
