---
id: DATA-0079
slug: data-0079-entity-transfer-helpers
domain: DATA
status: Proposed
date: 2025-10-02
---

# DATA-0079: Entity Move/Copy/Mirror Helpers for Context Transfers

Date: 2025-10-02

Status: Proposed

## Context

### Current State
- Entity routing depends on `EntityContext` (see `DATA-0077`), so developers wrap `using (EntityContext.Adapter(...))` or `EntityContext.Source(...)` around manual loops.
- Samples such as `samples/S14.AdapterBench/Services/BenchmarkService.cs` show repetitive patterns (query, enumerate, save in target context) with duplicated error handling and no shared ergonomics.
- Core APIs (`Save`, `All`, `AllStream`) offer great DX for intra-context work, but cross-context scenarios (copying between partitions, source migrations, archive restores) remain ad-hoc.

### Forces
- Teams need first-class tooling for **copy**, **move**, and **bi-directional synchronization** across sources/partitions while honoring the Source XOR Adapter rule introduced in `DATA-0077`.
- Operators want low-cognitive-load commands ("just run move") instead of re-implementing streaming/batching for every migration.
- We must keep Koan conventions: Entity-first, ambient context validation, async-only operations (no `*Async` suffix noise when everything is async).

## Decision

### 1. Entity Transfer DSL & Usage
Expose fluent builders from `Entity<T>` that cover the common transfer patterns:

```csharp
// Copy everything from the default context into the inactive partition
await Todo.Copy()
           .To(partition: "inactive")
           .Run(ct);

// Copy with inline predicate and audit hook
await Todo.Copy(p => !p.Active)
           .From(partition: "active")
           .To(source: "archive", partition: "inactive")
           .Audit(batch => logger.LogInformation("Copied {Count} todos", batch.Count))
           .Run(ct);

// Move with explicit delete strategy
await Todo.Move(p => p.Completed)
           .WithDeleteStrategy(DeleteStrategy.AfterCopy)
           .To(source: "archive")
           .Run(ct);

// Query-shaped overload for complex filters
await Todo.Copy(query => query.Where(i => i.Active && i.Due < DateTime.UtcNow))
           .To(adapter: "postgres", partition: "hot")
           .Run(ct);

// Mirror entire set using inline mode selection
await Todo.Mirror(mode: MirrorMode.Bidirectional)
           .From(source: "primary")
           .To(source: "reporting")
           .Run(ct);

// Mirror with predicate + mode parameters combined
await Todo.Mirror(p => p.Team == "ops", MirrorMode.Pull)
           .From(source: "reporting")
           .To(source: "primary")
           .Run(ct);
```

Key behaviour:
- `Copy` clones records into the destination context without removing the source entities; `Todo.Copy()` with no predicate transfers the entire set.
- `Move` performs a copy followed by deletion governed by the configured delete strategy (see below).
- `Mirror()` defaults to `MirrorMode.Push`; passing `MirrorMode` inline selects the directionality without extra fluent steps.
- The predicate parameter is optional. Helpers also expose query-shaped overloads accepting an `IQueryable<T>` projection for complex filters.
- `.From(...)` sets origin overrides; `.To(...)` defines the destination; `.Run(CancellationToken ct = default)` executes the transfer and returns a result summary (counts, conflicts, duration).

### 2. Context Selection Semantics
- `From`/`To` accept optional `source`, `adapter`, and `partition` parameters.
- Validators enforce **Source XOR Adapter** for both origin and destination. Supplying both throws `InvalidOperationException`.
- `partition` may be combined with either `source` or `adapter`, aligning with `DATA-0077` guidance.
- Omitting `From` uses the ambient/default context; omitting `To` throws because destination is required.

### 3. Move Delete Strategy Options
Introduce `DeleteStrategy` (default `AfterCopy`) to control when source deletions occur:
- `AfterCopy` – deletes occur once the copy phase completes successfully (safest default, avoids partial loss).
- `Batched` – deletes execute per successful batch; failures leave earlier batches intact and resumable.
- `Synced` – deletes happen immediately after each entity is persisted in the destination (minimal duplication, higher risk during failures).

`Todo.Move(...).WithDeleteStrategy(strategy)` configures the behaviour, and the resulting `TransferResult` reports counts of copied and deleted entities along with any skipped records.

### 4. Mirror Conflict Resolution
Bidirectional mirrors leverage Koan identity conventions:
- GUID v7 `Entity<T>.Id` acts as the stable identifier across contexts.
- If the model exposes a `[Timestamp]` property, it becomes the default tiebreaker (latest timestamp wins).
- When neither a resolver nor timestamp exists, conflicts are recorded in the `TransferResult` without automatic mutation; callers can inspect `result.Conflicts` and take corrective action.
- Custom conflict resolvers remain pluggable via dedicated callbacks in the mirror builder.

### 5. Audit & Telemetry Hooks
`Audit(Action<TransferAuditBatch> callback)` allows callers to receive per-batch telemetry while the framework also emits a final summary (totals, duration, conflicts). The default batch contract includes counts, context information, and elapsed time. Callers can opt into additional logging without implementing custom executors.

### 6. Execution & Resilience
- Transfers stream data (preferring `QueryStream` when available) with provider capability checks and backpressure-aware batching; adapters fall back to in-memory filters when necessary.
- `Run` returns a `TransferResult` containing entity counts, conflict information, delete statistics, audit summary, and duration. Results also surface warnings (e.g., mirror conflict without timestamp).
- Long-running or resumable transfers will rely on the Jobs pillar. The initial implementation focuses on single-run execution; future enhancements can layer checkpoint persistence on top of the existing `Koan.Jobs.Core` contracts.
- Dry-run/preview functionality is deferred to a follow-up ADR.
- APIs live in `Koan.Data.Core` (e.g., `Koan.Data.Core.Transfers`) to stay close to `EntityContext` and existing entity helpers.

## Consequences

### Positive
- Consistent DX for cross-context operations; reduces bespoke scripts and duplication.
- Honors Koan architectural invariants (Entity-first, provider transparency, Source XOR Adapter guardrails).
- Transfer results supply actionable telemetry (conflicts, counts, audit summaries) without additional plumbing.
- Delete strategy options and conflict policies give operators control over safety vs. speed.

### Negative / Risks
- Larger API surface in `Koan.Data.Core`; requires careful documentation to avoid misuse.
- `Move` requires well-defined retry semantics per strategy to prevent data loss.
- Mirror conflict handling introduces additional complexity; conflict surfacing must remain comprehensible.
- Without built-in checkpoints, extremely long transfers still need orchestration via Jobs.

### Mitigations
- Provide detailed docs and samples (update S14 and guides) illustrating usage patterns, delete strategy implications, and conflict handling.
- Implement safe defaults (`DeleteStrategy.AfterCopy`, timestamp tiebreaks) and capture skipped/conflicted entities in results.
- Emit telemetry/BootReport notes during transfers and surface optional `.Audit(...)` hooks for additional observability.
- Document that resumable behaviour lives in the Jobs pillar until future work adds first-class checkpointing.

## References
- `docs/decisions/DATA-0077-entity-context-source-adapter-partition-routing.md`
- `samples/S14.AdapterBench/Services/BenchmarkService.cs`
- `src/Koan.Jobs.Core` (jobs contract reference for future checkpoint work)
