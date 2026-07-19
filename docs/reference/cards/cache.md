---
type: REF
domain: data
title: "Cache — pillar map"
audience: [developers, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-16
  status: verified
  scope: Entity entry eviction, type control plane, repository key convergence, topology, and coherence
---

# Cache — pillar map

> One-screen map of the Cache pillar — transparent L1/L2 caching for `Entity<T>` with cross-node coherence. Full detail: [data/cache.md](../data/cache.md).

**What it does** — Annotate an entity `[Cacheable(...)]` and its reads become transparently cached (L1 in-process, L2 remote) with writes evicting through every node. The same `Todo.Get(id)` / `todo.Save()` code is unchanged; the decorator short-circuits reads and broadcasts evicts. **Reference = Intent** chooses the topology: reference `Koan.Cache.Adapter.Redis` and Redis becomes L2 with coherence auto-activating ([ARCH-0075](../../decisions/ARCH-0075-koan-cache-pillar.md)) — no `services.AddX()`. Reads are **fresh-or-null** by default; bounded stale serving is explicit and does not claim background revalidation.

## The one canonical pattern

Put `[Cacheable(ttlSeconds)]` on an `Entity<T>`. The static + instance verbs (**Save / Get / Remove**) are unchanged — caching is transparent.

```csharp
[Cacheable(300)]                              // 5-minute L2 TTL; L1 derived = max(30s, ttl/2)
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Done { get; set; }
}

var todo = await Todo.Get(id);                // first call hits the store + populates cache
var again = await Todo.Get(id);               // L1 hit, sub-ms
todo.Title = "updated";
await todo.Save();                            // write-through; peers get an evict broadcast

var policy = Todo.Cache.Explain();            // read-only materialized policy facts
```

Bounded stale serving is **opt-in**: `[Cacheable(300, AllowStaleForSeconds = 60)]`. Past `AbsoluteTtl` a default read returns `null`, not stale data.

## ≤5 attributes you'll use

| Attribute | What it does |
|---|---|
| `[Cacheable(ttlSeconds)]` | The 90% entry point. Opts an `Entity<T>` into Layered L1/L2 caching with sane defaults (`L1TtlSeconds`, `SlidingTtlSeconds`, `AllowStaleForSeconds` for bounded stale reads). |
| `[CachePolicy(scope, keyTemplate)]` | Power-user base. Class / method / controller-action scope, custom key templates (`{Id}`, `{TypeName}`, `{Partition}`), `Tags`, `Region`, multiple policies per type. |

## The escape hatch

Drop below the transparent decorator with a per-request scope or an out-of-band evict — both registered with `Koan.Cache`:

```csharp
using (EntityContext.NoCache())               // bypass cache for this scope (RefreshCache() forces re-fill)
{
    var fresh = await Todo.Get(id);           // straight from the store
}

await todo.Cache.Evict();                     // one entry after an out-of-band write
await todos.Cache.Evict();                    // finite/stream pointwise form, bounded and sequential
await Todo.Cache.Flush();                     // evict every entry tagged for this type
```

`Todo.Cache` exists only when `Koan.Cache` is referenced; Data.Core no longer advertises unavailable
cache behavior. Writes always invalidate regardless of `EntityContext`
([CacheBehavior](../../decisions/ARCH-0075-koan-cache-pillar.md)). `Todo.Cache.Count()` reports
tagged-entry counts, while `Todo.Cache.Explain()` inspects policy without cache I/O. Instance/set/stream
`Cache.Evict()` is a distinct entry plane with fixed-size removed/absent/skipped outcomes; it shares the
repository's policy, custom-template, partition, and managed-scope plan.

## The sample that shows it

[`TaskGraph`](../../../samples/fundamentals/TaskGraph/README.md) — `Entity<Todo>` over a minimal web UI; reference `Koan.Cache` to see transparent read-through and write-through evicts in the boot report.
