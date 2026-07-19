# JOBS-0007: Dispatch-Time Gate Resolution for Runtime Resource Pools

**Status**: Accepted
**Pillar**: Jobs
**Depends on**: JOBS-0005 §6 (gate keys), JOBS-0005 §18 (runtime gate resolution), ARCH-0079 (integration tests are canon)

> **Implementation update (R11-05, 2026-07-18):** `IJobPoolResolver` remains the single live-pool
> seam, but the redundant `AddJobPoolResolver<T>()` alias is removed. Register implementations through
> standard .NET DI: `services.AddSingleton<IJobPoolResolver, AiServerPoolResolver>()`.

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
| `DataJobLedger` | Pool-aware `ClaimNext` + `ElectTarget` + member-slot helpers (see the 2026-06-13 addendum for the paging fix) |
| `RoutingJobLedger` | Passes `pools` through to both delegated ledgers |
| `JobOrchestrator` | Injects `IEnumerable<IJobPoolResolver>`; calls `ResolvePoolContextsAsync` before each claim |
| standard .NET DI | register one `IJobPoolResolver` implementation per distinct pool name |

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
services.AddSingleton<IJobPoolResolver, AiServerPoolResolver>();

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

---

## Addendum (2026-06-13): claim-scan head-of-line starvation

**Status**: Accepted · **Severity**: high (silent total pipeline stall)

### Symptom (observed in a downstream consumer)

A `[JobPool("ai-servers")]` type backed up **1070 queued jobs** while the pool had **zero healthy members**. 517 runnable *non-pool* jobs (other work-types) were queued behind them with later `VisibleAt`. With a healthy worker and **no logged errors**, nothing settled for ~18h: every poll issued the claim `FIND … sort {VisibleAt, FirstSubmittedAt} limit 64`, returned 64 rows that were **all** unclaimable pool jobs, and emitted **zero** writes. `running = 0`; the whole pipeline was stalled.

### Root cause

`DataJobLedger.SelectCandidates` pushed the ready query down with `LIMIT ClaimScanBatch` (default 64) ordered by `(VisibleAt, FirstSubmittedAt)`, then applied the lane / gate / exclusive / **pool** unclaimable filter **in memory, over that already-truncated window**. Pool-member availability was consulted only *after* selection (in `ElectTarget` / the CAS loop). So when the count of unclaimable rows at the FIFO head exceeded one window, the window filtered to **zero claimable**, `ClaimNext` returned `null`, and the orchestrator's poll loop re-fetched the **same** unclaimable window forever — `WithPagination(1, …)` never advanced. Runnable work behind the head was never reached.

This was **not pool-specific**: the `saturatedLanes` filter and the `Exclusive && busy` (per `(WorkType, WorkId)`) filter were the same shape of in-memory-after-`LIMIT` filter and could starve the same way (a persistently saturated lane; a backlog of queued exclusive duplicates of a running work-item). The `InMemoryJobLedger` was **never** affected — it full-scans the ordered ready set (no `LIMIT`) and skips unclaimable rows, always reaching the runnable tail.

### Decision — page past unclaimable windows (not predicate-pushdown)

`SelectCandidates` now **pages forward**: it advances `WithPagination(page, batchSize)` over the same index-served predicate + `(VisibleAt, FirstSubmittedAt)` order, applying the unclaimable filter per page, until it has gathered a full batch of claimable candidates **or** a short page signals end-of-ready-set. Pool exhaustion is hoisted *into* selection: one `Running` snapshot (taken before selection) now feeds the `busy` exclusivity set, the per-member slot tally, and the set of **`claimablePools`** (pools with ≥1 open member); a queued pool job is excluded at selection time unless its `PoolKey` is claimable. This converges `DataJobLedger` with the in-memory ledger's full-scan semantics — both "scan past unclaimable until a claimable row or end-of-ready."

**Why paging, not pushing the exclusions into the store predicate** (which was the obvious alternative, and `IN`/`NOT IN` *is* reliably pushed on all four durable adapters):
1. **It closes all four vectors uniformly** — pool, lane, gate, and the `(WorkType, WorkId)` **exclusive/busy tuple** set. The tuple set cannot be a scalar store-side filter, so predicate-pushdown alone would leave that symmetric vector open — a partial fix.
2. **No adapter-semantic risk.** Pushing `!exhaustedPools.Contains(PoolKey)` walks into the relational `NULL NOT IN` trap (every non-pool row has `PoolKey = NULL`, and `NULL NOT IN (…)` is `unknown` → excludes all of them), which the FilterConvergence suite does not cover for null columns. Paging reuses the existing, battle-tested tight predicate — zero new pushdown surface.
3. **Convergence.** It makes the durable and in-memory ledgers behave identically (ARCH-0079 cross-tier contract), rather than introducing a durable-only predicate path.

### Cost & observability

In the healthy case the first page contains claimable rows → no extra round-trips. Extra paging happens **only while a large backlog is genuinely unclaimable** (the degenerate operational state), is bounded by the ready-set size, stays `O(batch)` and index-served per page, and is the same condition the §19.4 active-row guardrail already surfaces (a ballooning active set logs the job-per-row warning on the archival sweep). No artificial page cap is imposed: a fixed cap that returned `null` past the cap would reintroduce starvation for backlogs deeper than the cap — *loop-until-short-batch* is necessary and sufficient.

### Helper changes (supersede the table above)

- `BuildMemberSlotsAsync` (its own `Running` query) → `BuildMemberSlots(pools, running)` (sync, over the shared snapshot) + new `ClaimablePools(pools, memberSlots)`. The duplicate `Running` query (formerly one in `SelectCandidates` for `busy`, one in `BuildMemberSlotsAsync`) is consolidated to one per claim.
- New `IsClaimable(...)` factors the lane/pool/gate/exclusive predicate (preserving the original lane/gate/exclusive semantics exactly, adding the pool check).
- `ClaimNext`'s Ticket / CAS / optimistic election paths are unchanged; the interface signature is unchanged (fix is internal to `DataJobLedger`).

### Guard

Six cross-tier specs in `JobBehaviorSuite` (run on every tier — in-memory, SQLite, Mongo, SqlServer, Postgres): `exhausted_pool_does_not_starve_other_work_in_the_claim_scan`, `saturated_lane_does_not_starve_claims_for_other_lanes`, `active_gate_does_not_starve_ungated_claims`, `busy_exclusive_work_item_does_not_starve_other_work_items`, `claim_preserves_fifo_among_claimable_jobs_behind_an_unclaimable_head`, `exhausted_pool_backlog_larger_than_scan_window_returns_null_without_claiming`. Each sets a tiny `ClaimScanBatch` and seeds more than one window of unclaimable rows ahead of the runnable work; they fail on the windowed durable ledger before this fix and pass after.
