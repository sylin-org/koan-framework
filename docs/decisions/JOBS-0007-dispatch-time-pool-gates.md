# JOBS-0007: Dispatch-Time Gate Resolution for Runtime Resource Pools

**Status**: Accepted
**Pillar**: Jobs
**Depends on**: JOBS-0005 §6 (gate keys), JOBS-0005 §18 (runtime gate resolution), ARCH-0079 (integration tests are canon)

---

## Problem

The Jobs pillar supports `[JobGate]` for static resource contention (one slot per key, key resolved at submit from a work-item property). This covers static topologies. It does not cover **runtime resource pools** -- sets of resources (e.g. AI servers registered in a database) whose membership changes while jobs are queued.

The consumer's translation pipeline needs "at most N in-flight jobs per registered AiServer row." AiServer rows are added, paused, and removed by admins at runtime. Queued jobs must not be permanently bound to a server chosen at submit (a paused server would block the job forever). The gate key must be elected at claim time from the live member set.

---

## Decision

Add a `[JobPool("name")]` attribute and an `IJobPoolResolver` interface. The orchestrator resolves the live pool state before each claim attempt and passes it as `IReadOnlyDictionary<string, PoolDispatchContext>` into `ClaimNext`. The ledger elects the first member with open capacity and stamps it as `GateKey` atomically with the `Queued -> Running` transition.

Key properties:
- **Submit time**: `PoolKey = poolName`, `GateKey = null` (no frozen assignment).
- **Claim time**: the ledger reads Running rows to count per-member slots, elects the first free member, stamps `GateKey = member`.
- **Capacity**: `IJobPoolResolver.CapacityPerMember` (default 1, strict serial per server).
- **Dynamic membership**: resolvers are called on every claim attempt; members absent from the returned list receive no new work.
- **Mutual exclusion**: `[JobPool]` and `[JobGate]` cannot appear together on one type (enforced at bootstrap, `InvalidOperationException`).
- **Backward compatibility**: `ClaimNext` gains `pools` as a last optional parameter (default `null`); all existing call sites compile unchanged.

---

## Design

### New types

| Type | Location | Purpose |
|------|----------|---------|
| `[JobPool(name)]` | `JobAttributes.cs` | Declares pool participation on a job type |
| `IJobPoolResolver` | `IJobPoolResolver.cs` | Provides live member list + capacity |
| `PoolDispatchContext` | `PoolDispatchContext.cs` | Snapshot passed from orchestrator into ClaimNext |

### Modified types

| Type | Change |
|------|--------|
| `JobTypeBinding` | `PoolName` property; bootstrap validates `[JobPool]` + `[JobGate]` exclusion |
| `JobRecordFactory` | Sets `PoolKey = binding.PoolName`, leaves `GateKey = null` for pool jobs |
| `IJobLedger` | `ClaimNext` gains `pools` optional parameter |
| `InMemoryJobLedger` | Pool-aware `ClaimNext`: member slot counting + election |
| `DataJobLedger` | Pool-aware `ClaimNext` + `ElectTarget` + `BuildMemberSlotsAsync` helpers |
| `RoutingJobLedger` | Passes `pools` through to both delegated ledgers |
| `JobOrchestrator` | Injects `IEnumerable<IJobPoolResolver>`; calls `ResolvePoolContextsAsync` before each claim |
| `JobsServiceCollectionExtensions` | `AddJobPoolResolver<T>()` extension |

### Invariants preserved

- **Ledger is the single writer**: member election happens inside `ClaimNext` under the ledger's atomicity guarantee (lock for in-memory, CAS for durable).
- **Restart correctness**: Running rows survive restart; slot counting reads them from the store, not from in-process state.
- **No new coordinator**: no background pool-sweep service. The claim loop already polls; pool resolution piggybacks on that cadence.
- **Admission stays a claim predicate**: pool slot blocking is a skip-this-candidate condition inside the existing scan loop, not a separate queue.

### CAS behavior for pool jobs

On the durable tier with `ConditionalReplace`, a CAS loss on a pool candidate aborts the entire claim (return null). The next drain iteration re-resolves the pool with fresh slot counts. This is intentional: retrying other pool candidates with a stale slot snapshot risks electing an already-taken member.

---

## Consequences

**Positive**
- Runtime pool membership changes take effect on the next claim attempt with no job migration.
- No new persistence schema: `PoolKey` is a nullable string on `JobRecord` (same schema migration path as any new field).
- Fully transparent to existing job types (all existing tests pass unchanged).

**Negative / deferred**
- Resolvers are called once per drain iteration (not once per pool). A pool with 100 members queried every 50 ms should be cheap (the resolver is expected to cache its list).
- Batch-claim optimization (claim multiple pool jobs at once, amortizing the slot-count query) is deferred.

---

## Registration pattern

```csharp
// App startup
services.AddJobPoolResolver<AiServerPoolResolver>();

// The resolver
public sealed class AiServerPoolResolver : IJobPoolResolver
{
    private readonly IMemoryCache _cache;
    private readonly IAiServerRepository _repo;

    public AiServerPoolResolver(IMemoryCache cache, IAiServerRepository repo)
        => (_cache, _repo) = (cache, repo);

    public string PoolName => "ai-servers";
    public int CapacityPerMember => 1;

    public async Task<IReadOnlyList<string>> GetMembersAsync(CancellationToken ct)
        => await _cache.GetOrCreateAsync("ai-server-pool", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
            return await _repo.GetActiveServerIdsAsync(ct);
        }) ?? Array.Empty<string>();
}

// The job type
[JobPool("ai-servers")]
public sealed class TranslationJob : Entity<TranslationJob>, IKoanJob<TranslationJob>
{
    public static Task Execute(TranslationJob job, JobContext ctx, CancellationToken ct)
    {
        // ctx.Record.GateKey is the elected server id (stamped at claim time)
        ...
    }
}
```
