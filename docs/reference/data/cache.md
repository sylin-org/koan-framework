# Koan Cache reference

**Status:** current development generation
**Architecture:** [ARCH-0075](../../decisions/ARCH-0075-koan-cache-pillar.md)

Koan Cache keeps application code business-shaped: annotate an Entity, then keep using normal Entity verbs.
Storage topology, policy materialization, peer invalidation, health, and startup facts are framework concerns.

```csharp
[Cacheable(300)]
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
}

var todo = await Todo.Get(id);       // miss fills the elected cache topology
var again = await Todo.Get(id);      // may be served from L1/L2
todo.Title = "done";
await todo.Save();                   // normal business verb; cache policy follows

var policy = Todo.Cache.Explain();   // read-only materialized policy facts
```

`Todo.Cache` is contributed by the Cache module. Remove the module reference and the facet disappears
at compile time.

## Shortest path

Reference `Sylin.Koan.Cache`, call the normal application-level `AddKoan()`, and add `[Cacheable]` to an
Entity. No cache registration belongs in `Program.cs`.

| Reference | Result |
|---|---|
| `Sylin.Koan.Cache` | Built-in process-memory L1; local-only topology; no peer invalidation is needed. |
| `Sylin.Koan.Cache.Adapter.Sqlite` | Eligible local persistent store; normal store election selects or pins it. |
| `Sylin.Koan.Cache.Adapter.Redis` | Redis remote tier plus a layered Redis every-node broadcast capability. When Redis is the active L2, peer invalidation activates automatically. |
| `Sylin.Koan.Communication.Connector.RabbitMq` | A directly intended Communication mesh may carry peer invalidation instead; Cache code does not change. |

Communication always supplies a process-local broadcast floor. External providers replace it only through
direct provider intent or an active layered capability. A named provider pin that does not exist fails boot;
Koan does not silently select a different store or weaken network reach.

## Policy

`[Cacheable]` is the normal entry point:

```csharp
[Cacheable(300)]
public sealed class Todo : Entity<Todo> { }

[Cacheable(600, L1TtlSeconds = 60)]
public sealed class Product : Entity<Product> { }

[Cacheable(60, AllowStaleForSeconds = 10)]
public sealed class HotKey : Entity<HotKey> { }
```

The default policy is layered, read-through, partition-aware, tagged by Entity type, and configured to
publish peer invalidations after writes. When no L2 exists, the effective topology is local-only.

L1 expiry never exceeds L2 expiry. With no explicit L1 TTL, Koan derives a shorter L1 lifetime; this is
the bounded-staleness fallback when a best-effort invalidation is lost. Invalid `L1 > L2` policies fail boot.

Fresh-or-null is the default. Stale-while-revalidate exists only when a policy or call explicitly sets
`AllowStaleFor`:

```csharp
var report = await Cache.WithJson<UsageReport>("report:" + tenantId)
    .WithAbsoluteTtl(TimeSpan.FromHours(1))
    .AllowStaleFor(TimeSpan.FromMinutes(10))
    .GetOrAdd(ct => BuildReport(tenantId, ct), ct);
```

Use `[CachePolicy]` when a custom key template, tier, strategy, tags, or provider pin is genuinely needed.
Provider pins match `ICacheStore.Name` exactly (case-insensitive) and fail closed when unavailable. Within
each Local/Remote tier, provider priority and then stable provider identity make automatic selection
deterministic; DI registration order does not decide. Startup facts report the selected `cache:local` and
`cache:remote` receipts as well as the resulting topology.

## Request-scoped behavior

```csharp
using (EntityContext.NoCache())
{
    var fresh = await Todo.Get(id);
}

using (EntityContext.RefreshCache())
{
    var rebuilt = await Todo.Get(id);
}

using (EntityContext.WithCacheBehavior(CacheBehavior.ReadOnly))
{
    var cached = await Todo.Get(id);
}
```

`Koan.Web` can project cache-control headers into these same request-scoped behaviors through
`app.UseKoanCacheControl()`.

## Explicit inspection and eviction

```csharp
var one = await todo.Cache.Evict();
var many = await todos.Cache.Evict();
var stream = await Todo.QueryStream(x => x.Done).Cache.Evict();

await Todo.Cache.Flush();
var explanation = Todo.Cache.Explain();
```

Ordinary `Save` and `Delete` already maintain cache state. Use pointwise `Evict()` after an out-of-band
write that bypassed the Entity repository. Scalar, finite, and stream forms preserve the same meaning:
one key removal and peer invalidation request per non-default Entity, in source order, with sequential
backpressure and no batch-atomicity claim.

The fixed-size `EntityCacheEviction` distinguishes entries actually `Removed`, idempotently `Absent`,
and `Skipped` because an unset id could never have been cached. Source/removal failures and cancellation
carry the confirmed prefix through typed exceptions. Store removal and peer carriage are not atomic;
the failing current item may already be partly removed.

`Todo.Cache` remains the type/control plane. `Explain()` does not read or mutate entries; `Flush`,
`Count`, and `Any` operate on policy-derived tags. Pointwise eviction and broad maintenance are not
aliases.

Repository caching and `Evict()` consume one host-owned Entity cache plan. Policy selection, custom
key template, partition/source values, and managed equality-axis scope therefore agree by construction.
Call eviction under the logical context that owns the entry; Koan captures that context once before a
finite or deferred source begins.

Tag flush enumerates matching keys through the elected store and removes each key. Each removal emits the
same peer-key invalidation contract; there is no separate, partially implemented “flush everything” wire command.

## Peer invalidation contract

The correctness boundary is deliberately small:

1. The writer updates the shared L2 and its own L1 through the normal Cache path.
2. Cache emits one internal key invalidation through Communication's `FrameworkBroadcasts` route.
3. Every active node within the elected provider's reach receives a copy.
4. The writer filters its own echo using a per-process origin identity.
5. Peers evict that key from L1 only; they never mutate L2 and never re-broadcast.
6. If a best-effort signal is lost, L1 TTL bounds stale residence.

This is not a public message bus. Cache owns the message meaning; Communication owns provider election,
bounded egress, wire validation, lifecycle, ingress, health, and facts.

`CoherenceMode` controls posture:

| Mode | Behavior |
|---|---|
| `AutoDetect` | Default. Layered topologies activate the elected broadcast route; non-layered topologies need no peer invalidation. |
| `Required` | A layered topology must elect a non-local node-broadcast provider or boot fails. |
| `Disabled` | Cache sends and applies no peer invalidations. |

There is no catch-up/replay claim, multi-channel duplicate publication, generic coherence SPI, or automatic
retry. Those mechanisms were removed because no current provider proved them. A future durable provider must
materialize a real use case and semantics before the surface grows.

## Startup, health, and agent inspection

Startup logs and Koan composition facts report:

- L1/L2 store election and topology;
- coherence mode and active/inactive posture;
- the elected `FrameworkBroadcasts` provider and assurance;
- L1-only receive behavior and the L1-TTL safety bound; and
- materialized policy count plus every resolved Entity entry plan, including its selected strategy/key
  template or its cache-safety exclusion reason.

The same facts appear through `/.well-known/Koan/facts` and `koan://facts` when those host surfaces are
present. `CacheHealthCheck` reports tier reachability plus coherence provider, assurance, active state, and
node identity.

## Deliberate limits

- Redis pub/sub and process-local delivery are best effort; they do not replay missed invalidations.
- RabbitMQ node subscriptions are ephemeral; confirmed publication is not remote handler settlement.
- Peer invalidation is key-scoped and L1-only.
- L1 TTL is part of the correctness posture, not optional decoration for layered caches.
- Store and Communication provider behavior remains provider-specific and visible in facts.
- Pointwise set/stream eviction is sequential and non-atomic; use type/tag flush for broad maintenance.
- Explicit entry eviction is not automatically exposed as an MCP mutation. Governed operational flush
  remains a separate audited control plane.

See the [architecture guide](../../architecture/koan-cache-module.md) for the internal boundary.
