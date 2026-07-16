# Koan Cache

Koan Cache adds transparent, policy-driven caching to Koan Entities. Reference
`Sylin.Koan.Cache`, keep the normal `AddKoan()` application boot, and declare intent on the model:

```csharp
using Koan.Cache.Abstractions.Policies;
using Koan.Data.Core.Model;

[Cacheable(300)]
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
}

var todo = await Todo.Get(id, ct); // read-through under the elected topology
todo.Title = "done";
await todo.Save(ct);               // cache state follows the normal business write
```

No cache service belongs in application code. The module supplies a process-memory local floor;
direct adapter references can add a persistent local tier, a remote tier, and peer invalidation
without changing Entity operations.

## Entity language

Ordinary `Save` and `Delete` operations already maintain cache state. Explicit entry eviction exists
for writes that deliberately bypass the Entity repository:

```csharp
var one = await todo.Cache.Evict(ct);
var many = await todos.Cache.Evict(ct);
var stream = await Todo.QueryStream(x => x.Done, ct).Cache.Evict(ct);

var policy = Todo.Cache.Explain();
var flushed = await Todo.Cache.Flush(ct);
```

Instance/set/stream `Cache.Evict()` is the entry plane. It preserves source order and multiplicity,
captures Data and registered Koan context once, applies sequential backpressure, and returns a
fixed-size `EntityCacheEviction`. `Removed` means an entry existed in the selected topology;
`Absent` is a successful idempotent removal whose peer invalidation was still requested; `Skipped`
means an unset identifier could never have been cached.

`Todo.Cache` is the type/control plane: policy explanation and tag-based `Flush`, `Count`, and `Any`.
It is deliberately not a pointwise entry API.

## Policy and topology

`[Cacheable]` is the normal policy. `[CachePolicy]` is the power-user form for strategy, tier, tags,
provider pins, or a custom key template. One host-owned Entity cache plan selects the effective
policy, template, partition/source tokens, and managed equality-axis scope for repository caching and
explicit eviction, so the two paths cannot drift.

| Direct reference | Effect |
|---|---|
| `Sylin.Koan.Cache` | Process-memory local floor |
| `Sylin.Koan.Cache.Adapter.Sqlite` | Eligible persistent local store |
| `Sylin.Koan.Cache.Adapter.Redis` | Eligible remote tier; its peer-broadcast capability activates only when Redis owns L2 |
| `Sylin.Koan.Communication.Connector.RabbitMq` | Direct Communication intent may carry peer invalidation |

Provider pins fail when unavailable. External intent does not silently fall back to local reach.
Startup facts and health report effective L1/L2 election, coherence posture, provider assurance,
policy count, every resolved Entity entry plan or safety exclusion, and the L1 TTL safety bound.

## Failure and non-claims

Source and removal failures throw `EntityCacheEvictionException`; cancellation throws
`EntityCacheEvictionCanceledException`. Both carry the confirmed fixed-size prefix. Cache-tier
removal and peer publication are not atomic, so a failing current item may already be partly removed
even though it is not counted as confirmed.

Koan does not claim batch atomicity, durable invalidation replay, remote handler settlement, or
cross-store transactions. Use type/tag flush for broad maintenance; pointwise stream eviction emits
one key invalidation per Entity and is intentionally sequential.

See the [Cache reference](../../docs/reference/data/cache.md) and
[technical notes](TECHNICAL.md).
