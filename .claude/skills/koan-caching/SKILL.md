---
name: koan-caching
description: Transparent L1/L2 caching for Entity<T>, [Cacheable] attribute, cross-node coherence, per-request opt-out, out-of-band evict, stale-while-revalidate opt-in
pillar: cache
card: docs/reference/cards/cache.md
status: current
last_validated: 2026-06-18
---

# Koan Caching

## Trigger this skill when you see

- `[Cacheable(...)]` or `[CachePolicy(...)]` on an `Entity<T>`
- `EntityContext.NoCache()` / `RefreshCache()` / `WithCacheBehavior(...)`
- `CacheKey.For<T>(...)`, `Cache.WithJson<T>(...)`, `Cache.Tags(...)`, `entity.Uncache()`
- References to `Koan.Cache` / `Koan.Cache.Adapter.Redis` / `Koan.Cache.Adapter.Sqlite` / `Koan.Cache.Coherence.*`
- HTTP `Cache-Control` headers or the `X-Koan-Cache` header, `app.UseKoanCacheControl()`
- "cache invalidation", "L1/L2", "cache coherence", "write-through", "TTL", "tags", "stampede", "stale-while-revalidate", "cross-node staleness"

## Core principle

**Reference = Intent.** Add `Koan.Cache` to project references and `[Cacheable]` to an `Entity<T>` — that's the whole 90% case. Reads become transparently cached (L1 in-process, L2 remote when a remote adapter is present); writes evict through every node via coherence. The same `Todo.Get(id)` / `todo.Save()` verbs (canonical **Save / Get / Remove**) are unchanged — the decorator short-circuits reads and broadcasts evicts. Reads are **fresh-or-null** by default; stale-while-revalidate is **opt-in only** ([ARCH-0078](../../../docs/decisions/ARCH-0078-stale-while-revalidate-opt-in.md)). No `services.AddX()` cache wiring.

<!-- validate -->
```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Policies;
using Koan.Data.Core.Model;

[Cacheable(300)]                              // 5-minute L2 TTL; L1 derived = max(30s, ttl/2)
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Done { get; set; }
}

public sealed class TodoService
{
    public async Task<Todo?> Read(string id, CancellationToken ct = default)
    {
        var todo = await Todo.Get(id, ct);    // first call hits the store + populates cache
        var again = await Todo.Get(id, ct);   // L1 hit, sub-ms
        return again ?? todo;
    }

    public async Task<Todo> Rename(string id, string title, CancellationToken ct = default)
    {
        var todo = await Todo.Get(id, ct)
            ?? throw new InvalidOperationException($"Todo {id} not found");
        todo.Title = title;
        return await todo.Save(ct);           // write-through; peers get an evict broadcast
    }

    public async Task<Todo?> Fresh(string id, CancellationToken ct = default)
    {
        using (EntityContext.NoCache())       // CacheBehavior.Bypass — skip read, hit DB, no populate
            return await Todo.Get(id, ct);
    }
}
```

## Reference = Intent activation

| Add this reference | Effect |
|---|---|
| `Koan.Cache` | L1 = in-memory, single-node, no coherence. `[Cacheable]` is live. |
| `+ Koan.Cache.Adapter.Sqlite` | L1 = SQLite (persistent across restart; priority 50, preempts memory). |
| `+ Koan.Cache.Adapter.Redis` | L2 = Redis **and coherence auto-activates** (`RedisCoherenceChannel`). |
| `+ Koan.Cache.Coherence.Messaging` | Rides the existing `Koan.Messaging` bus (preempts Redis pub/sub). |
| `+ Koan.Cache.Coherence.InMemory` | In-process channel — tests / single-process verification. |
| `Koan.Web` + `app.UseKoanCacheControl()` | Maps `Cache-Control` / `X-Koan-Cache` headers onto `EntityContext.CacheBehavior` (opt-in middleware). |

Critical invariant ([ARCH-0075](../../../docs/decisions/ARCH-0075-koan-cache-pillar.md)): the store contract has **no** publish methods. Coherence is its own pillar (`ICacheCoherenceChannel`). `LayeredCache.ApplyRemoteInvalidation` touches **L1 only** — never L2 (shared, already evicted by the writer), never republishes (would feed back).

## Anti-patterns to flag

| If you see | Suggest |
|---|---|
| `services.AddMemoryCache()` + a hand-rolled cache wrapper | `[Cacheable]` on the entity — caching is policy-driven, not DI-imperative. |
| `IMemoryCache` injected into a service for entity reads | Same — the decorator handles entity reads transparently. |
| `redis.GetDatabase().StringGetAsync(...)` for entity reads | `[Cacheable]` for entities; `Cache.WithJson<T>(key).GetOrAdd(...)` for arbitrary values. |
| `Cache.Evict<T,K>(id)` (does not exist) | `await entity.Uncache()` or `EntityCacheExtensions.Cache<Todo, string>().Flush(id)` / `.FlushAll()`. |
| String concatenation for cache keys | `CacheKey.For<TEntity>(id, partition)` — canonical, partition-aware. |
| `[Cacheable(60, AllowStaleForSeconds: 10)]` to enable stale-while-revalidate | The per-entity `AllowStaleForSeconds` / `L1TtlSeconds` / `SlidingTtlSeconds` setters are `init`-only and can't be used as attribute named args today (a framework gap) — opt into SWR **per call** via `.AllowStaleFor(TimeSpan)` on the fluent builder. |
| `if (skipCache) ...` branches threaded through business code | `EntityContext.NoCache()` / `RefreshCache()` scopes — declarative, no call-site pollution. |
| A custom cache-invalidation pub/sub | Writes broadcast automatically; for non-entity cases use `ICacheCoherenceChannel` directly. |
| `L1 TTL > L2 TTL` | The boot-time materializer throws — L1 may not outlive L2. |

## Escape hatches

- **Per-request opt-out**: `using (EntityContext.NoCache())` (Bypass — skip read, no populate), `using (EntityContext.RefreshCache())` (skip read, hit DB, repopulate), or `EntityContext.WithCacheBehavior(CacheBehavior.ReadOnly)` (`Koan.Cache.Abstractions.Policies`). **Writes always invalidate** regardless of scope — peer L1 entries evict via coherence even under Bypass, preventing silent multi-node desync.
- **Out-of-band evict** (after a `IDataService.Direct(...)` write — folded into `Koan.Data.Core`, ARCH-0090 — or a batch job that bypasses the decorator): `await todo.Uncache()` (single entity), `await EntityCacheExtensions.Cache<Todo, string>().Flush(id)` (one id), `.FlushAll()` (every entry tagged with the type name). There is **no** `Cache.Evict<T,K>`.
- **Non-entity caching**: `Cache.WithJson<T>(key).WithAbsoluteTtl(ttl).WithTags(...).AllowStaleFor(ts).GetOrAdd(factory, ct)` for computed/expensive values; `await Cache.Tags("tenant:42").Flush(ct)` for bulk tag invalidation. SWR here is opt-in per call via `AllowStaleFor` — omit it and the read is strict fresh-or-null.
- **SWR is opt-in only**: opt in **per call** via `.AllowStaleFor(TimeSpan)` on the fluent builder. There are **no** global SWR toggles in adapter options or app config — the per-call opt-in is the only switch. (The per-entity `[Cacheable(..., AllowStaleForSeconds = N)]` form is documented but does **not** compile today — the setter is `init`-only and so can't be an attribute named argument; tracked as a framework gap.)
- **Coherence mode**: set `Koan:Cache:CoherenceMode = "Required"` in production to fail fast if a remote tier is present with no coherence channel registered.

## See also

- [Reference card: cache.md](../../../docs/reference/cards/cache.md) — one-screen pillar map
- [Reference: cache.md](../../../docs/reference/data/cache.md) — full detail
- [Architecture: koan-cache-module.md](../../../docs/architecture/koan-cache-module.md)
- [`samples/S1.Web`](../../../samples/S1.Web/README.md) — `Entity<Todo>`; reference `Koan.Cache` to watch read-through + write-through evicts in the boot report
- [ARCH-0075 — cache pillar architecture](../../../docs/decisions/ARCH-0075-koan-cache-pillar.md)
- [ARCH-0078 — stale-while-revalidate opt-in](../../../docs/decisions/ARCH-0078-stale-while-revalidate-opt-in.md)
