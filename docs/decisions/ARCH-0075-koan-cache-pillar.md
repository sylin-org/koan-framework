# ARCH-0075: Koan.Cache Pillar — Storage, Coherence, Topology, Policy

**Status**: Accepted
**Date**: 2026-05-15
**Deciders**: Enterprise Architect
**Scope**: New pillar — `Koan.Cache.*` projects; touches `Koan.Data.Core` (EntityContext),
`Koan.Web` (middleware), `Koan.Messaging.Core` (coherence transport), `Koan.Media.Web` (M11 pilot)
**Related**: ARCH-0059 (initial koan-cache module proposal — superseded by this), ARCH-0060 (cache control surface),
ARCH-0057 (KoanLog facade), ARCH-0071 (partition context provider), ARCH-0074 (framework gap analysis)
**Supersedes**: ARCH-0059

---

## Context

The framework needs a coherent cache primitive. Today's `Koan.Cache.*` projects on `dev` are
unreleased scaffolding — there are no public consumers, the contracts conflate storage with
coherence, the receive-side eviction targets the wrong tier, and the write path never publishes
invalidations. Several pillars worked around this absence by building bespoke caches; the
audit catalogued at least six.

### Forces at play

1. **No prior public consumers.** Greenfield licence — break-and-rebuild authorized.
2. **`Koan.Cache.*` scaffolding is high-quality where it touches non-distributed concerns**
   (singleflight, scope, serializers, key templating, instrumentation, builders, registries).
   The audit found ~85% of existing code is reusable as-is or with minor tweaks.
3. **The architectural fault is structural, not implementation-level.** `ICacheStore` declares a
   `PublishInvalidation` method that has no business sitting on a K/V store. The receive-side
   listener lives in the Redis adapter package and evicts the wrong tier (L2 — already evicted
   by the writer — instead of L1, which is the only thing it can usefully evict).
4. **Bespoke caches in other pillars duplicate primitives.** `InMemoryMediaTransformCache`,
   `InMemoryRoleAttributionCache`, `RagCorpusMetadata._cache` (unbounded), and others each
   re-implement TTL + invalidation in isolation. None of them survive process restart, none
   share state across nodes.
5. **Reference = Intent is a hard requirement.** A user who adds `Koan.Cache.Adapter.Redis`
   should get distributed L2 *and* cross-node coherence with zero further code.
6. **Coherence transport must be pluggable.** The framework already ships `Koan.Messaging.Core`
   with an `IMessageBus`; forcing users to stand up Redis pub/sub purely for cache invalidation
   would be friction. Redis pub/sub, `Koan.Messaging`, in-process, and future NATS/Kafka must
   be interchangeable.
7. **Defense in depth.** Coherence is best-effort. The cache must be correct even when the
   broadcast layer is silent (network partition, missed pub/sub messages, cold reconnect).

---

## Decision

Build the Koan.Cache pillar around **four orthogonal concerns**, each with its own contract
and package boundary:

| Pillar | Contract | Responsibility |
|---|---|---|
| **Storage** | `ICacheStore` | Pure K/V verbs on bytes |
| **Coherence** | `ICacheCoherenceChannel` | Cross-node invalidation broadcast |
| **Topology** | `LayeredCache` + `CoherenceCoordinator` | L1/L2 wiring, applying remote invalidations |
| **Policy** | `[Cacheable]` / `[CachePolicy]` | Declarative per-entity/per-method intent |

### Key decisions

1. **`ICacheStore` does not publish.** `PublishInvalidation` is removed from the storage contract.
   Storage is storage. Coherence is its own concern with its own interface.

2. **`ICacheCoherenceChannel` is transport-agnostic.** Three verbs (`Publish`, `Subscribe`,
   `CatchUp`) carry a single payload type `CacheInvalidation`. Implementations: Redis pub/sub,
   Redis Streams (catch-up), `Koan.Messaging.Core.IMessageBus`, in-process. Channels declare
   `[ProviderPriority(N)]` (framework canon attribute from `Koan.Data.Abstractions`) to control
   precedence when multiple are registered.

3. **`LayeredCache` is composition, not inheritance.** It does not implement `ICacheStore`. It
   has explicit verbs (`Read`, `Write`, `Evict`, `EvictByTag`, `EvictAll`) and one internal
   method `ApplyRemoteInvalidation` reserved for the coordinator. This prevents the original
   bug (a layered store implementing publish-and-route-locally on the same interface as the
   K/V store, leading to feedback loops and tier confusion).

4. **`CoherenceCoordinator` is a single hosted service in `Koan.Cache`.** It owns the per-process
   `NodeId` (origin filter), subscribes every registered channel, and routes received
   invalidations to `LayeredCache.ApplyRemoteInvalidation`. The coordinator activates iff at
   least one channel is registered (default `CoherenceMode = AutoDetect`). Production
   deployments can demand `CoherenceMode = Required` for fail-fast on missing coherence.

5. **Consistency model: writer write-through, peer evict, always broadcast EvictKey.** The
   writer's local L1 and L2 hold the new value (warm cache on the originator). Peers receive
   an EVICT message (not the value), forcing their next read to refetch from L2 or DB. DB is
   the single resolver for concurrent-write races. This is the canonical pattern used by
   Caffeine, Hazelcast Near Cache, and Couchbase's automatic invalidation; values-in-flight
   on the wire can be overtaken by reality, so we don't ship them.

6. **`ApplyRemoteInvalidation` is L1-only, by design.** The receiving node has no reason to
   touch L2 (it's shared and already evicted by the writer). The method name is explicit to
   prevent the next contributor from accidentally reusing the generic `Evict` and creating
   a publish/receive feedback loop.

7. **Defense in depth via L1 TTL ≤ L2 TTL.** `L1AbsoluteTtl` defaults to
   `max(30s, L2Ttl / 2)` when unspecified. Worst-case L1 staleness is bounded even when
   coherence is silent. Validator rejects `L1Ttl > L2Ttl` at boot.

8. **Writes always invalidate, including under `CacheBehavior.Bypass`.** Per-request opt-out
   affects reads + cache-populating only. A developer "just doing a quick admin fix" under
   `EntityContext.NoCache()` still triggers eviction across all nodes — otherwise bypass
   becomes a foot-gun that silently desyncs the cluster.

9. **`[Cacheable(int ttlSeconds)]` is the entity surface.** Thin subclass of
   `CachePolicyAttribute` with int-second setters (C# attribute syntax cannot take `TimeSpan`
   literals). Defaults: TTL=300, Tier=Layered, Strategy=GetOrSet, tags=[`{TypeName}`],
   key=`{TypeName}:{Partition}:{Id}`. Auto-discovered by `CachePolicyBootstrapper` because it
   inherits from `CachePolicyAttribute`.

10. **HTTP middleware in `Koan.Web` maps `Cache-Control` to `CacheBehavior`.** Standard
    semantics: `no-cache` → Refresh, `no-store` → Bypass. Opt-in via `app.UseKoanCacheControl()`.

11. **Decorator ordering: cache outer, CQRS inner.** Both `CacheRepositoryDecorator` and
    `CqrsRepositoryDecorator` (in `Koan.Data.Cqrs`) implement `IDataRepositoryDecorator`. Cache
    sits outermost so hits short-circuit before the read pipeline runs. Enforced via
    `[ProviderPriority]` on each decorator class.

12. **Out-of-band write evict API.** `Koan.Data.Direct` and batch jobs bypass decorators.
    The pillar exposes `Cache.Evict<TEntity, TKey>(id, ct)` (broadcasts coherence) plus the
    existing `EntityCacheExtensions.Uncache()` / `EntityCache<T,K>().Flush(id)`. Documented
    as the canonical "I wrote out-of-band, invalidate me" surface.

13. **`InMemoryMediaTransformCache` pilot migration (M11).** Proves the pillar's reach beyond
    entities by replacing one bespoke cache. Smallest blast radius (single consumer,
    well-defined key shape, no entity coupling). The other five bespoke caches are tracked
    as follow-on PRs, not blockers.

14. **Test scaffolding survives.** Existing `tests/Suites/Cache/Unit/` has 12 spec files
    covering the surface that survives. Extended, not replaced. New test projects sit under
    `tests/Suites/Cache/{Abstractions,Coherence.InMemory,Coherence.Messaging,Web,Performance}/`.

---

## Consequences

### Positive

- **Reference = Intent works end-to-end.** Adding `Koan.Cache.Adapter.Redis` activates both
  the Remote store and the coherence channel automatically. No DI calls, no Program.cs config.
- **Coherence transport is genuinely pluggable.** Users with `Koan.Messaging` already
  configured don't need Redis pub/sub.
- **Bespoke caches have a home.** Six existing in-pillar caches can ride this pillar instead
  of re-implementing TTL + invalidation each time. Massive multiplier on framework cohesion.
- **Out-of-band writes have a documented escape valve.** `Cache.Evict<T,K>(id)` is the canonical
  pattern for callers that bypass `Entity<T>` (Direct SQL, batch jobs).
- **Defense in depth.** Three layers (coherence + L1 TTL + DB-wins-on-writes) keep the cache
  correct under any single-layer failure.
- **~85% code reuse from existing scaffolding.** Singleflight, scope, serializers, key
  template, instrumentation, builders, registries, pillar manifest all survive intact.

### Negative

- **Breaking change to current (unreleased) `Koan.Cache.*` contracts.** `ICacheStore.PublishInvalidation`
  removed; `RedisInvalidationListener`/`RedisInvalidationMessage` deleted. Acceptable per
  greenfield licence; no migration doc needed.
- **Two new packages** to maintain: `Koan.Cache.Coherence.InMemory`, `Koan.Cache.Coherence.Messaging`.
- **Distributed concerns are inherently harder to test.** Mitigated by the `InMemoryCoherenceChannel`
  pattern that lets us run cross-instance integration tests in one process.

### Risks

- **Coherence message volume on hot writes** could overwhelm the transport. Mitigated by
  configurable coalescing buffer (`CoherenceCoalescingMs`, default `0` = off).
- **Missed pub/sub messages during transient disconnects** leave L1 stale until TTL. Mitigated
  by L1 TTL ceiling and opt-in `RedisStreamsCoherenceChannel` for catch-up.
- **Decorator ordering ambiguity** with CQRS could cause subtle bugs. Mitigated by
  `[ProviderPriority]` on both decorators and an integration test verifying observed order.
- **M11 pilot (`MediaTransformCache`)** changes eviction semantics from byte-size to count/TTL.
  Documented as a trade-off; tunable via `L1AbsoluteTtl`.

### Open

- Whether `RedisStreamsCoherenceChannel` (catch-up flavour) ships in v1 or later. Recommend
  defer unless production hardening is required day one.
- Catch-up cursor persistence backend (in-memory vs file vs L1 key). Recommend in-memory + L1
  flush on restart for v1.
- The other five bespoke caches (Roles, RAG, Mcp, Distillation, Embeddings) — order of follow-on
  migration PRs.

---

## References

- Implementation plan: `docs/proposals/caching_implementation_plan.md`
- ARCH-0059 (initial proposal, superseded)
- ARCH-0060 (cache-control HTTP surface)
- ARCH-0057 (KoanLog facade — used in boot reports)
- ARCH-0071 (partition context provider — `EntityContext` extension pattern)
- ARCH-0074 (framework gap analysis — greenfield principles)
- `Koan.Data.Abstractions/ProviderPriorityAttribute.cs` (canonical priority attribute)
- `Koan.Messaging.Core/IMessagingProvider.cs` (`IMessageBus` surface used by `MessagingCoherenceChannel`)
- `Koan.Data.Core/EntityContext.cs` (AsyncLocal ambient pattern for `CacheBehavior`)
- `Koan.Data.Cqrs/CqrsRepositoryDecorator.cs` (decorator ordering canon)
- `src/Koan.Media.Web/Caching/InMemoryMediaTransformCache.cs` (M11 pilot target)
