# Koan.Cache — Reference

**Status:** initial release · v0.7.0
**Architecture:** ARCH-0075 · ARCH-0076 (decorator order)

The cache pillar provides transparent L1/L2 caching for `Entity<T>` with cross-node coherence. Reference = Intent: adding `Koan.Cache.Adapter.Redis` activates Redis-as-L2 and coherence broadcasting with zero user code.

This is the first stop for using the cache. The architecture overview lives in [koan-cache-module.md](../../architecture/koan-cache-module.md); the canonical decision record is [ARCH-0075](../../decisions/ARCH-0075-koan-cache-pillar.md).

---

## TL;DR — five-minute integration

```csharp
// 1. Reference Koan.Cache (and optionally adapters / coherence transports)
// <ProjectReference Include="...\src\Koan.Cache\Koan.Cache.csproj" />
// <ProjectReference Include="...\src\Koan.Cache.Adapter.Redis\Koan.Cache.Adapter.Redis.csproj" />

// 2. Annotate the entity
[Cacheable(300)]                              // 5-minute TTL
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
}

// 3. Use it normally
var todo = await Todo.Get(id);                 // first call hits DB, populates cache
var again = await Todo.Get(id);                // L1 hit, sub-ms
todo.Title = "updated";
await todo.Save();                             // cache write-through; peers get evict broadcast
```

Boot report (with Redis):
```
Koan.Cache
  Topology    : layered (L1=memory, L2=redis)
  Coherence   : active (transports=[redis-pubsub])
  Policy:Todo : tier=Layered, ttl=300s, l1=150s, strategy=GetOrSet, tags=[Todo], broadcast=yes [OK]
```

---

## Reference = Intent — what each package gets you

| Reference | Effect |
|---|---|
| `Koan.Cache` | L1 = built-in Memory store, no L2, coherence inactive. Single-node ready. |
| `+ Koan.Cache.Adapter.Sqlite` | L1 = SQLite (preempts Memory by `[ProviderPriority(50)]`). Persists across restart. |
| `+ Koan.Cache.Adapter.Redis` | L2 = Redis, **coherence auto-activates** (`RedisCoherenceChannel` registers). Cross-node L1 invalidation works out of the box. |
| `+ Koan.Cache.Coherence.Messaging` | Rides existing `Koan.Messaging` bus for coherence (preempts Redis pub/sub via `[ProviderPriority(150)]`). |
| `+ Koan.Cache.Coherence.InMemory` | In-process channel — primary use case is tests / single-process verification. |

No `services.AddX()` calls needed in `Program.cs`. The framework's `KoanAutoRegistrar` discovery wires everything.

---

## The `[Cacheable]` attribute

The 90% entry point. Apply to a class deriving from `Entity<T>`.

```csharp
[Cacheable(ttlSeconds: 300)]                  // L2 TTL = 300s; L1 derived = 150s
public sealed class Todo : Entity<Todo> { }

[Cacheable(600, L1TtlSeconds: 60)]           // explicit L1 override
public sealed class Product : Entity<Product> { }

[Cacheable(0)]                                // no expiration (sentinel)
public sealed class Config : Entity<Config> { }

[Cacheable(60, SlidingTtlSeconds: 30, AllowStaleForSeconds: 10)]
public sealed class HotKey : Entity<HotKey> { }
```

### Defaults (when not overridden)

| Field | Default | Notes |
|---|---|---|
| `Tier` | `Layered` | L1 + L2. Effective tier is `LocalOnly` if no Remote store registered. |
| `Strategy` | `GetOrSet` | Read-through with write-back. |
| `Key template` | `{TypeName}:{Partition}:{Id}` | Partition-aware. `{Partition}` resolves to `"_"` outside a partition scope. |
| `Tags` | `["{TypeName}"]` | Materialized to the actual type name at boot. Enables `EntityCache<T>.Flush()`. |
| `ForceCoherenceBroadcast` | `true` | Writes broadcast `EvictKey` to peers. |
| `L1AbsoluteTtl` | `min(L2Ttl, max(30s, L2Ttl/2))` | Defense-in-depth: L1 staleness capped even if coherence is silent. |

### Validation at boot

`L1AbsoluteTtl > AbsoluteTtl` throws at startup with a clear diagnostic. The materializer enforces the L1 ≤ L2 invariant.

### Stale-while-revalidate is opt-in (ARCH-0078)

The cache contract is **fresh-or-null by default**. Reads past `AbsoluteTtl` return `null`, not a stale value — the cache's identity is "freshness through coherence," not "serve stale while we refresh."

SWR is available as an **explicit, per-policy or per-call opt-in** via `AllowStaleFor`:

```csharp
// Entity-level opt-in: any Get of HotKey past its TTL but within the staleness window
// returns the prior value instead of null.
[Cacheable(60, AllowStaleForSeconds: 10)]
public sealed class HotKey : Entity<HotKey> { }

// Per-call opt-in: ad-hoc cache use.
var report = await Cache.WithJson<UsageReport>("report:" + tenantId)
    .WithAbsoluteTtl(TimeSpan.FromHours(1))
    .AllowStaleFor(TimeSpan.FromMinutes(10))     // ← explicit SWR opt-in
    .GetOrAdd(async ct => await BuildReportAsync(tenantId, ct), ct);
```

Callers that don't set `AllowStaleFor` never see stale data, regardless of any adapter configuration. There are no global "enable SWR" toggles — the per-call opt-in is the only switch.

The boot report surfaces SWR opt-in on a per-policy basis:

```
Policy:HotKey   tier=Layered, ttl=60s, l1=30s, ..., swr=10s [opt-in] [OK]
Policy:Todo     tier=Layered, ttl=300s, l1=150s, ..., [OK]
```

---

## The `[CachePolicy]` attribute — power-user surface

For controller-action caching, method-scoped policies, custom key templates, or multiple policies per type, drop to the underlying `[CachePolicy]`:

```csharp
[CachePolicy(
    Scope = CacheScope.Entity,
    KeyTemplate = "tenant:{Entity.TenantId}:product:{Id}",
    Tier = CacheTier.Layered,
    LocalProvider = "memory",         // pin by ICacheStore.Name
    RemoteProvider = "redis",
    Strategy = CacheStrategy.GetOrSet,
    Tags = new[] { "Product", "tenant:{Entity.TenantId}" },
    ForceCoherenceBroadcast = true)]
public sealed class Product : Entity<Product>
{
    public string TenantId { get; set; } = "";
}
```

Key-template tokens:
- `{Id}` / `{Key}` — entity identifier
- `{TypeName}` — declaring type's simple name
- `{Partition}` — `EntityContext.Current?.Partition ?? "_"`
- `{Source}` — `EntityContext.Current?.Source ?? "_"`
- `{Entity.PropertyName}` — reflective property access

Missing tokens at format time → cache is skipped for that call (logged at Debug; DB hit proceeds).

C# attribute syntax can't construct `TimeSpan` literals — use `CacheableAttribute`'s integer-second setters for TTL fields, or set them programmatically via DI configuration.

---

## Per-request opt-out — `EntityContext.CacheBehavior`

Push an ambient override for the duration of a scope. Mirrors the existing `EntityContext.Partition` pattern.

```csharp
using (EntityContext.NoCache())                 // skip read, hit DB, no populate
{
    var fresh = await Todo.Get(id);
}

using (EntityContext.RefreshCache())            // skip read, hit DB, repopulate
{
    var rebuilt = await Todo.Get(id);
}

using (EntityContext.WithCacheBehavior(CacheBehavior.ReadOnly))
{
    var cached = await Todo.Get(id);            // serves L1/L2 if present; falls through to DB on miss but does NOT populate
}
```

**Writes always invalidate** regardless of override — peer L1 entries are evicted via coherence even when bypass mode suppresses the local cache populate. This prevents a developer's quick admin fix from silently desyncing other nodes.

### HTTP integration

`Koan.Web` ships `app.UseKoanCacheControl()` middleware (opt-in) that maps HTTP cache headers onto `EntityContext.CacheBehavior` for the request:

| Header | Maps to |
|---|---|
| `Cache-Control: no-cache` | `RefreshCache()` |
| `Cache-Control: no-store` | `NoCache()` |
| `X-Koan-Cache: refresh` | `CacheBehavior.Refresh` |
| `X-Koan-Cache: bypass` (or `no-cache`/`no-store`) | `CacheBehavior.Bypass` |
| `X-Koan-Cache: readonly` (or `read-only`) | `CacheBehavior.ReadOnly` |
| `X-Koan-Cache: default` | `CacheBehavior.Default` |

`X-Koan-Cache` wins over `Cache-Control` when both are present.

---

## Out-of-band evict — `Koan.Data.Direct` and batch jobs

When code mutates state outside `Entity<T>.Upsert/Delete` (raw SQL via `Koan.Data.Direct`, batch jobs, external workers), the cache decorator never sees the change. Use the canonical evict surface:

```csharp
// Canonical key constructor — eliminates stringly-typed concatenation
var key = CacheKey.For<Todo>(id, partition: "archive");

// Evict locally + broadcast to peers via coherence
await todo.Uncache();                                 // instance form (entity → key by convention)
await EntityCacheExtensions.Cache<Todo, string>().Flush(id);   // typed handle
```

---

## Topology semantics

The `LayeredCache` orchestrator composes (L1, L2) stores and exposes four explicit verbs:

| Verb | Behavior |
|---|---|
| `Read(key, opts)` | L1 hit returns immediately. L1 miss → L2 hit triggers L1 backfill (fire-and-forget). Both miss → caller's factory invoked under singleflight. |
| `Write(key, value, opts)` | L1 and L2 written in parallel. Coherence broadcast (if active) publishes `EvictKey` to peers. |
| `Evict(key)` | L1 and L2 both removed. Coherence broadcast publishes `EvictKey`. |
| `ApplyRemoteInvalidation(msg)` | **L1 only, never L2, never republishes.** Used internally by `CoherenceCoordinator` to apply peer broadcasts. |

Resolution order for the L1/L2 picks (per startup):

1. Config pin (`Koan:Cache:LocalProvider` / `RemoteProvider` matched on `ICacheStore.Name`).
2. Highest `[ProviderPriority]` among stores with matching `Placement`.
3. First store with matching `Placement`.
4. Null (single-tier or empty deployment).

If `CoherenceMode = Required` and a Remote tier is registered but no `ICacheCoherenceChannel` is, the coordinator fails fast at boot.

---

## Coherence semantics — the consistency model

**Writer write-through, peer evict, always broadcast `EvictKey`.**

```
Writer (Node A):
  1. DB write commits (cache never ahead of truth)
  2. L1 (A) + L2 (shared)  Set with new value
  3. Publish EvictKey(key, origin=A) on all registered channels

Peer (Node B):
  4. Channel delivers EvictKey
  5. Coordinator filters origin (skip if origin==self.NodeId)
  6. LayeredCache.ApplyRemoteInvalidation evicts L1 (B) only
  7. Next read on B fetches from shared L2 (which has A's new value)
```

| Property | Guaranteed | Notes |
|---|---|---|
| DB always wins | ✅ | Cache writes follow DB commit. |
| Peer L1 freshness | ✅ (best-effort transport) | Bounded staleness window = L1 TTL even if a broadcast is lost. |
| Cross-node read-your-writes | ❌ in milliseconds | Sub-second on healthy network. L1 TTL caps worst case. |
| Strict serialization across nodes | ❌ | Use DB transactions for that — cache is not a coordination primitive. |

### Failure modes

| Failure | Behavior | Recovery |
|---|---|---|
| Channel publish fails | Logged, write succeeds; peers may be stale up to L1 TTL | L1 TTL bounds damage. |
| Channel disconnect | Subscriber misses messages | Reconnect; if channel supports catch-up, replay from cursor. Otherwise wait for L1 TTL. |
| Node restart | L1 cleared (memory); L2 unchanged | Cold L1, first read on each key backfills from L2. |
| `CoherenceMode = Required` with no channel | Boot fails fast | Reference a coherence-capable adapter or change mode. |

---

## Configuration

```jsonc
{
  "Koan": {
    "Cache": {
      // Tiering
      "DefaultTier": "Layered",                       // Layered | LocalOnly | RemoteOnly
      "DefaultTtlSeconds": 300,
      "DefaultL1TtlSeconds": null,                    // null = derive max(30, L2/2)
      "LocalProvider": null,                          // pin by ICacheStore.Name
      "RemoteProvider": null,

      // Coherence
      "CoherenceMode": "AutoDetect",                  // AutoDetect (default) | Required | Disabled
      "CoherenceTransport": null,                     // pin transport name (e.g. "redis-pubsub", "koan-messaging")
      "CoherenceCoalescingMs": 0,                     // 0 = immediate publish; >0 enables per-key debounce
      "CoherenceCoalescingMaxBuffered": 10000,
      "CoherenceStartupTimeoutMs": 10000,

      // Singleflight
      "DefaultSingleflightTimeout": "00:00:05",

      // Region / observability
      "DefaultRegion": "default",
      "EnableDiagnosticsEndpoint": true,

      // Memory adapter
      "Memory": {
        "TagIndexCapacity": 2048
      },

      // Redis adapter (referenced separately)
      "Redis": {
        "Configuration": "localhost:6379",
        "KeyPrefix": "cache:",
        "TagPrefix": "cache:tag:",
        "ChannelName": "koan-cache"
      }
    }
  }
}
```

---

## Production hardening

### Health check

`AddKoanCache()` registers a `CacheHealthCheck` named `"koan-cache"` with tags `cache`, `koan`. Probes L1 + L2 reachability and reports coherence state.

```csharp
app.MapHealthChecks("/health");                       // standard ASP.NET
```

Status mapping:
- `Healthy` — all configured tiers reachable
- `Degraded` — one tier unreachable (e.g., Redis briefly down) but the other works
- `Unhealthy` — no tiers reachable or none configured

K8s readiness probes pick this up automatically.

### Trace a specific key in production

```bash
export KOAN_CACHE_TRACE_KEY="Todo:_:abc-123"
# restart process — every hit/miss/set/evict on that exact key now emits
# Information-level log lines from the cache pillar.
```

Exact-string match (ordinal). For broader observability, use OpenTelemetry below.

### OpenTelemetry

```csharp
.WithMetrics(b => b.AddMeter("Koan.Cache"))
.WithTracing(b => b.AddSource("Koan.Cache"));
```

| Metric | Tags |
|---|---|
| `koan.cache.hits` / `.misses` / `.sets` / `.removes` / `.invalidations` | `provider` |
| `koan.cache.coherence.published` / `.received` / `.applied` | `transport`, `kind` |
| `koan.cache.tier.fetches` / `.hits` / `.misses` | `tier`, `result` |
| `koan.cache.read.duration` / `.write.duration` (ms) | `result` (read only) |

ActivitySource spans on every `LayeredCache.Read`/`Write`/`Evict` and coordinator hop, tagged with `cache.key`.

---

## Power-user tools

### Fluent client API

When you need to cache non-entity data (computed results, expensive transforms, external API responses):

```csharp
var report = await Cache.WithJson<UsageReport>("report:" + tenantId)
    .WithAbsoluteTtl(TimeSpan.FromHours(1))
    .WithTags("reports", $"tenant:{tenantId}")
    .AllowStaleFor(TimeSpan.FromMinutes(10))
    .GetOrAdd(async ct => await BuildReportAsync(tenantId, ct), ct);

// Bulk flush by tag
await Cache.Tags("tenant:42").Flush(ct);
```

### Manual eviction

```csharp
// By single key (also broadcasts)
await Cache.Evict<Todo, string>(id, ct);

// By type (uses the {TypeName} tag materialized by the policy)
await EntityCacheExtensions.Cache<Todo, string>().FlushAll(ct);
```

---

## See also

- [koan-cache-module.md](../../architecture/koan-cache-module.md) — full architecture
- [ARCH-0075](../../decisions/ARCH-0075-koan-cache-pillar.md) — accepted ADR
- [ARCH-0076](../../decisions/ARCH-0076-repository-decorator-order.md) — decorator priority canon
- [implementation plan](../../proposals/caching_implementation_plan.md) — milestones M1–M11
