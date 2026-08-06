---
name: koan-caching
description: Transparent Entity caching, policy and topology negotiation, pointwise cache-entry eviction, request scopes, tags, coherence, health, and startup inspection
pillar: cache
card: docs/reference/data/cache.md
status: current
last_validated: 2026-07-16
---

# Koan Caching

## Trigger this skill when you see

- `[Cacheable(...)]` or Entity-scoped `[CachePolicy(...)]`
- `entity.Cache.Evict()` / `entities.Cache.Evict()` / `EntityType.Cache.*`
- `EntityContext.NoCache()`, `RefreshCache()`, or `WithCacheBehavior(...)`
- `Cache.WithJson<T>(...)`, `Cache.Tags(...)`, cache policy, topology, tiers, TTL, SWR, or singleflight
- Cache Memory/SQLite/Redis adapters, peer invalidation, or Communication `FrameworkBroadcasts`
- cache startup facts, health, MCP operational flush, or `UseKoanCacheControl()`

## The shortest path

**Reference = Intent.** Reference `Sylin.Koan.Cache`, keep the normal `AddKoan()` boot, and put
`[Cacheable]` on the Entity. Application code continues to read as persistence and business logic.

<!-- validate -->
```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Policies;
using Koan.Data.Core;
using Koan.Data.Core.Model;

[Cacheable(300)]
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
}

public sealed class TodoService
{
    public async Task<Todo> Complete(string id, CancellationToken ct = default)
    {
        var todo = await Todo.Get(id, ct)
            ?? throw new InvalidOperationException($"Todo {id} not found");
        todo.Title = "done";
        return await todo.Save(ct);
    }

    public async Task<Todo?> Fresh(string id, CancellationToken ct = default)
    {
        using (EntityContext.NoCache())
            return await Todo.Get(id, ct);
    }
}
```

The Cache module contributes its Entity language. Remove the reference and both static `Todo.Cache`
and instance/set/stream `.Cache` disappear at compile time.

## Entry plane versus control plane

Ordinary `Save` and `Delete` maintain cache state automatically. Use explicit entry eviction only
after an out-of-band write that bypassed the Entity repository:

```csharp
var one = await todo.Cache.Evict(ct);
var many = await todos.Cache.Evict(ct);
var stream = await Todo.QueryStream(x => x.Done).Cache.Evict(ct);
```

All three forms preserve pointwise meaning, source order, and multiplicity. Execution is sequential,
constant-memory, and context-stable. `EntityCacheEviction` reports `Removed`, idempotently `Absent`,
default-id `Skipped`, `Failed`, `Confirmed`, and `SourceCompleted`. Typed failure/cancellation errors
carry the confirmed prefix. There is no batch atomicity or automatic retry claim.

The static facet is the type/tag control plane:

```csharp
var policy = Todo.Cache.Explain();
var populated = await Todo.Cache.Any(ct);
var count = await Todo.Cache.Count(ct);
var flushed = await Todo.Cache.Flush(ct);
```

Do not substitute type/tag flush for a pointwise stream by accident: broad flush enumerates every
tagged key, while source eviction emits one removal/invalidation per supplied Entity.

## One canonical Entity cache identity

Repository caching and explicit eviction consume the same host-owned plan. It owns active policy
selection, safe-cache exclusion, custom key template, Entity type identity, partition/source tokens,
and managed equality-axis scope. Do not construct Entity eviction keys in application code.

Invoke `Evict()` under the logical scope that owns the entry. The operation captures Data routing
context and registered Core carriers before deferred enumeration, so a tenant-A operation addresses
only tenant A. A missing policy or a type that cannot safely have an equality-keyed cache fails before
the source is enumerated.

## Reference = Intent topology

| Direct reference | Effect |
|---|---|
| `Sylin.Koan.Cache` | Built-in process-memory local floor |
| `Sylin.Koan.Cache.Adapter.Sqlite` | Eligible persistent local store |
| `Sylin.Koan.Cache.Adapter.Redis` | Eligible L2; Redis broadcast activates only when Redis owns L2 |
| `Sylin.Koan.Communication.Connector.RabbitMq` | Direct Communication intent may carry peer invalidation |
| `Sylin.Koan.Web` + middleware | Optional request-header projection into `EntityContext.CacheBehavior` |

Adapters describe layered capability but do not activate it until the owning mechanism is active.
Explicit provider pins fail loud when unavailable; intended external reach never silently falls back
to process-local delivery.

Cache owns the invalidation meaning. Communication owns bounded carriage, provider election, wire,
lifecycle, ingress, facts, and health. Peers evict L1 only, never shared L2 and never rebroadcast.
Best-effort loss is bounded by L1 TTL.

## Escape hatches

- **Request behavior:** `EntityContext.NoCache()` bypasses read/population;
  `RefreshCache()` forces a provider read and refill; `ReadOnly` reads cache without filling.
- **Non-Entity values:** `Cache.WithJson<T>(key).WithAbsoluteTtl(ttl).WithTags(...)
  .AllowStaleFor(window).GetOrAdd(factory, ct)`.
- **Bulk maintenance:** `Cache.Tags("tenant:42").Flush(ct)` or `EntityType.Cache.Flush(ct)`.
- **SWR:** explicitly set `AllowStaleForSeconds` on `[Cacheable]` or `.AllowStaleFor(...)` on a
  fluent entry. Default reads are fresh-or-null.
- **Production posture:** `Koan:Cache:CoherenceMode = Required` rejects a layered topology that has
  only process-local invalidation reach.

## Anti-patterns to flag

| If you see | Prefer |
|---|---|
| Hand-rolled `IMemoryCache` for Entity reads | `[Cacheable]`; keep `Get/Save/Delete` business-shaped |
| Manual cache registration in application boot | Reference the package and keep `AddKoan()` |
| `Uncache()` or `EntityCacheExtensions.Cache<T,K>()` | `entity.Cache.Evict()`; the old split API is deleted |
| Application string-building of Entity cache keys | Let the host-owned Entity cache plan build them |
| A custom invalidation bus or old coherence package | Cache's internal every-node Communication route |
| A source loop that accumulates per-item outcomes | One `entities.Cache.Evict()` fixed-size terminal |
| Treating `Absent` as failure | It is idempotent success and still requests peer invalidation |
| Equating broker acceptance with peer settlement | Inspect reported provider assurance and L1 TTL bound |
| Projecting entry eviction directly to an agent | Use the governed/audited MCP type/global control plane |

## Non-claims

- no collection or cross-store atomicity;
- no durable invalidation replay/catch-up or automatic removal retry;
- no remote handler settlement;
- no global-clear wire primitive; and
- no guarantee beyond the elected provider/topology facts.

## See also

- [Cache capability](../../../docs/reference/data/cache.md)
- [Full reference](../../../docs/reference/data/cache.md)
- [Architecture](../../../docs/architecture/koan-cache-module.md)
- [Entity identity convergence](../../../docs/architecture/cache-scope-key-convergence.md)
- [ARCH-0075](../../../docs/decisions/ARCH-0075-koan-cache-pillar.md)
- [ARCH-0113](../../../docs/decisions/ARCH-0113-entity-capability-communication.md)
