---
name: koan-caching
description: Transparent L1/L2 caching for Entity<T>, [Cacheable] attribute, cross-node coherence, per-request opt-out
---

# Koan Caching

## Trigger this skill when you see

- `[Cacheable]` or `[CachePolicy]` attributes
- `EntityContext.NoCache()` / `RefreshCache()` / `WithCacheBehavior(...)`
- `CacheKey.For<T>(...)`, `Cache.WithJson<T>(...)`, `Cache.Evict<T,K>(...)`
- References to `Koan.Cache` / `Koan.Cache.Adapter.Redis` / `Koan.Cache.Adapter.Sqlite` / `Koan.Cache.Coherence.*`
- HTTP `Cache-Control` headers or `X-Koan-Cache`
- Performance discussions about repeated entity reads / cross-node staleness / cache stampedes
- "Cache invalidation", "L1/L2", "cache coherence", "write-through", "TTL", "tags"

## Core principle

**Reference = Intent.** Add `Koan.Cache` to project references and `[Cacheable]` to an entity — that's the whole 90% case. The framework handles wiring, topology resolution, coherence, eviction, and instrumentation automatically.

```csharp
[Cacheable(300)]                     // 5-minute TTL, L1=150s derived, Layered, GetOrSet
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
}

// Use normally — caching is transparent
var todo = await Todo.Get(id);       // first hit DB, populates cache
var fast = await Todo.Get(id);       // L1 hit, sub-ms
todo.Title = "updated";
await todo.Save();                   // cache write-through + broadcasts EvictKey to peers
```

## Reference = Intent activation table

| Add this reference | Effect |
|---|---|
| `Koan.Cache` | L1=Memory, single-node, no coherence |
| `+ Koan.Cache.Adapter.Sqlite` | L1=SQLite (persistent across restart) |
| `+ Koan.Cache.Adapter.Redis` | L2=Redis + **coherence auto-activates** |
| `+ Koan.Cache.Coherence.Messaging` | Rides existing `Koan.Messaging` bus (preempts Redis pub/sub) |
| `+ Koan.Cache.Coherence.InMemory` | In-process channel — tests / single-process verification |

## Architecture: four pillars

| Pillar | Owns | Contract |
|---|---|---|
| **Storage** | K/V verbs | `ICacheStore` (Memory/SQLite/Redis) |
| **Coherence** | Cross-node invalidation | `ICacheCoherenceChannel` (Redis pub/sub, Koan.Messaging, in-memory) |
| **Topology** | L1/L2 wiring + write/evict orchestration | `LayeredCache` (internal) + `CoherenceCoordinator` (`IHostedService`) |
| **Policy** | Per-entity declarative intent | `[Cacheable]` / `[CachePolicy]` + `CachePolicyMaterializer` |

Critical invariant: `ICacheStore` has NO publish methods. Coherence is its own pillar with its own contract. `LayeredCache.ApplyRemoteInvalidation` touches **L1 only** — never L2 (shared, already evicted by writer), never republishes (would create feedback loops).

## Anti-patterns to flag

| If you see | Suggest |
|---|---|
| `services.AddMemoryCache()` + manual cache wrapper | `[Cacheable]` on the entity instead. |
| `IMemoryCache` injected into a service for entity caching | Same — entity caching is policy-driven, not DI-imperative. |
| Manual `redis.GetDatabase().StringGetAsync(...)` | `Cache.WithJson<T>(key).GetOrAdd(...)` for arbitrary values; `[Cacheable]` for entities. |
| Custom cache-invalidation pub/sub | `[Cacheable]` writes broadcast automatically. For non-entity cases, use `ICacheCoherenceChannel` directly. |
| String concatenation for cache keys | `CacheKey.For<TEntity>(id, partition)` — canonical, partition-aware. |
| Cache-bypass via `if (skipCache) ...` branches in business code | `EntityContext.NoCache()` / `RefreshCache()` scopes — declarative, doesn't pollute call sites. |
| L1 TTL > L2 TTL | Boot-time validator throws. The materializer enforces the invariant. |

## Per-request opt-out

```csharp
using (EntityContext.NoCache())              // CacheBehavior.Bypass
{
    var fresh = await Todo.Get(id);          // skip read, hit DB, no populate
}

using (EntityContext.RefreshCache())         // CacheBehavior.Refresh
{
    var rebuilt = await Todo.Get(id);        // skip read, hit DB, repopulate
}

using (EntityContext.WithCacheBehavior(CacheBehavior.ReadOnly))
{
    var cached = await Todo.Get(id);         // cache if present, no populate on miss
}
```

**Writes always invalidate** regardless of the scope — peer L1 entries are evicted via coherence even when bypass mode suppresses the local populate. Prevents silent multi-node desync.

### HTTP integration

`app.UseKoanCacheControl()` middleware (opt-in, in `Koan.Web`) maps standard HTTP cache headers onto `EntityContext.CacheBehavior`:

- `Cache-Control: no-cache` → `RefreshCache()`
- `Cache-Control: no-store` → `NoCache()`
- `X-Koan-Cache: refresh|bypass|readonly|default` (wins over `Cache-Control`)

## Out-of-band write evict

`Koan.Data.Direct` and batch jobs bypass the cache decorator. Use the canonical evict surface:

```csharp
await Cache.Evict<Todo, string>(id, ct);                          // single key + broadcast
await EntityCacheExtensions.Cache<Todo, string>().FlushAll();     // all entries tagged with "Todo"
await todo.Uncache();                                             // instance form
```

## Power-user: non-entity caching

For computed/expensive values that aren't `Entity<T>`:

```csharp
var report = await Cache.WithJson<UsageReport>($"report:{tenantId}")
    .WithAbsoluteTtl(TimeSpan.FromHours(1))
    .WithTags("reports", $"tenant:{tenantId}")
    .GetOrAdd(async ct => await BuildReportAsync(tenantId, ct), ct);

await Cache.Tags($"tenant:{tenantId}").Flush(ct);                 // bulk invalidation
```

## Production hardening checklist

- [ ] Health check `app.MapHealthChecks("/health")` — picks up `"koan-cache"` automatically
- [ ] OpenTelemetry: `AddMeter("Koan.Cache")` + `AddSource("Koan.Cache")`
- [ ] Set `Koan:Cache:CoherenceMode = "Required"` in production to fail fast if no coherence channel is registered with a Remote tier present
- [ ] For a stuck-stale key in prod: set `KOAN_CACHE_TRACE_KEY=Todo:_:abc-123` env var and restart — every touch on that key logs at Information

## See also

- [Reference: cache.md](../../docs/reference/data/cache.md)
- [Architecture: koan-cache-module.md](../../docs/architecture/koan-cache-module.md)
- [ADR-0075 — pillar architecture](../../docs/decisions/ARCH-0075-koan-cache-pillar.md)
- [ADR-0076 — decorator order](../../docs/decisions/ARCH-0076-repository-decorator-order.md)
