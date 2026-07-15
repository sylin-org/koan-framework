---
type: SPEC
domain: framework
title: "R07-02 - Make Entity Streams Genuinely Bounded"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-15
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-15
  status: in-progress
  scope: provider-bounded query paging beneath the existing Entity stream surface
---

# R07-02 — Make Entity streams genuinely bounded

- Tranche: `T6 — semantic capability ring`
- Status: `in-progress`
- Depends on: R07-01 and ARCH-0113
- Unlocks: truthful lazy Entity selections and the later scalar/set/stream capability substrate
- Owner: Data.Core query coordination with adapter-owned execution claims

## Meaningful outcome

Application code keeps one ordinary .NET stream shape:

```csharp
await foreach (var order in Order.QueryStream(x => x.Ready, ct))
{
    await Process(order, ct);
}
```

Koan no longer loads the complete result before yielding the first Entity. A supported adapter enforces
the requested candidate-page bound before application materialization, Koan keeps at most one candidate
page, consumer pace controls later page requests, and cancellation or early disposal stops further work.

This is the lower-boundary proof required before a later expression such as
`Order.QueryStream(...).Transport.Send()` can honestly remain lazy and bounded.

## Why this slice is separate

Provider streaming and Lifecycle expose different risk:

- bounded streaming is an additive execution correction beneath an existing public call shape; and
- the accepted `Events` to `Lifecycle` rebuild removes shipped public 0.17 APIs and requires a complete
  0.18 reverse-dependent publication wave.

The release mechanism does not yet mint that breaking reverse-dependent closure automatically. This
slice therefore fixes the independent lower boundary now and leaves Lifecycle source changes stopped
until the packaging gate can carry them safely. It does not add a compatibility alias or weaken the
accepted greenfield Lifecycle target.

## Current defect

- `AllStream` and `QueryStream` call the materializing query-and-count path and then enumerate a list.
- `batchSize` is accepted but ignored.
- documentation and agent guidance have consequently alternated between overstating constant-memory
  behavior and understating the intended Entity grammar.
- ordinary `Page` requests also pay for count work they do not require.
- natural calls such as `AllStream(ct)` do not compile because the optional batch argument precedes the
  cancellation token.

## Decisions

### DECIDED

- Keep `IAsyncEnumerable<TEntity>` as the only public streaming substrate.
- Do not add a public Pager, cursor, continuation, Flow, or provider-specific stream type.
- Add one precise adapter capability: provider-bounded paging. It means that, when the adapter reports
  the complete filter and total ordering as handled, `PageSize = N` is enforced by the provider before
  candidates are materialized into application memory.
- Compose that proven primitive into a lazy async sequence in one internal Data.Core coordinator.
- `batchSize` is a positive maximum candidate-page size. The existing unbounded-loop page size is the
  default until configuration evidence earns another value.
- Enumeration performs no repository I/O until its first move, never calls the query-and-count path,
  and requests no later page after early disposal.
- Unsupported adapters, incomplete filter/sort pushdown, invalid bounds, and dishonest execution
  metadata reject correctively before yielding. There is no materializing fallback.
- Residual predicates may be evaluated pointwise after a bounded candidate page. Empty output from one
  candidate page does not end the stream.
- A stable total order is mandatory. The adapter must handle the complete order; Koan does not sort an
  unbounded source in memory.
- Provider/source/partition and logical context are fixed for one enumeration.
- Add natural cancellation overloads without removing the existing signatures.
- Adapter claims are earned through shared conformance. A paging flag alone is not evidence because a
  resident or key-value adapter may slice only after a complete scan/materialization.

### DEFAULT

- Foundation qualification starts with SQLite. PostgreSQL, CockroachDB, SQL Server, MongoDB, and
  Couchbase may declare the same capability only after their provider command, ordering, and
  cancellation paths pass the shared cells.
- InMemory, JSON, and Redis initially reject provider-bounded streaming rather than claiming their
  current full-source key-value query path is bounded. A later resident-incremental capability must
  earn a distinct honest contract if the default local provider needs it.

### OPEN

- Whether an adapter later adds a native cursor or keyset implementation is an internal optimization
  and separate capability. It must not change application grammar.
- Mutation-safe iteration, snapshot consistency, resumability, and cross-process continuation are not
  implied by this slice.

## Architecture guardrails

```text
Entity<T>.QueryStream / AllStream
              |
      Data.Core stream coordinator
              |
   capability + pushdown preflight
              |
 existing repository Query(page N)
              |
 adapter enforces bound before materialization
```

- Data.Core owns orchestration and semantic guarantees; adapters own physical execution claims.
- A capability token is necessary but not sufficient: each returned page must also report that
  pagination and the requested filter/order were handled.
- The coordinator buffers no more than the current candidate page and yields no item from a page whose
  execution metadata violates the plan.
- `batchSize` bounds Koan-visible candidates; it does not make unproved claims about opaque driver
  buffers.
- Numbered paging is not snapshot isolation. Concurrent writes can cause skips or duplicates; facts
  and documentation state that limit.
- Facts contain safe type/provider/reason identities, never Entity values, predicates, credentials, or
  logical-context payloads.

## Red/green plan

1. Add core fake-repository proofs for lazy first yield, exact page requests, no count, early disposal,
   cancellation, invalid bounds, empty residual pages, stable ordering, and rejection before I/O.
2. Add the provider-bounded-page capability and one typed corrective rejection following the existing
   relationship-query execution pattern.
3. Add the internal stream coordinator and route `Data`, `Entity`, repository facade, and cache
   decorator calls through it without introducing a second application path.
4. Make count strategy explicit so stream/page requests do not compute totals accidentally.
5. Add natural cancellation overload consumer probes.
6. Qualify SQLite, then each remote adapter independently; do not advertise InMemory, JSON, or Redis
   until a bounded implementation exists.
7. Exercise the real Backup consumer over multiple pages, cancellation, and early failure.
8. Rewrite public, connector, sample, and agent guidance from executable evidence only.
9. Run focused, adapter, build, docs, diff, compatibility, and privacy gates.

## Verification

- Core fake provider: exact page sizes and sequence; first item before later requests; no count; early
  break; zero-output residual page; cancellation before and during work; unsupported and overclaim
  rejection before output; context/source stability.
- Adapter conformance: every static row exactly once in deterministic order; candidate output never
  exceeds the requested bound; filter/order are provider-handled; cancellation reaches provider work;
  generated command or SDK behavior proves limit/offset before materialization.
- Negative conformance: adapters using the current complete key scan do not advertise the capability
  and reject without starting that scan.
- Consumer proof: `StreamingBackupService` crosses multiple pages and stops on cancellation.
- Documentation: strict site build plus a structural sweep for false Pager/cursor and
  non-materialization claims.
- Broader regression: affected Data.Core and connector suites, then a warning-reviewed solution build.
- Privacy: no private downstream identity, path, persona, or workflow enters tracked artifacts.

## Acceptance additions

- A supported stream yields before its tail has been queried.
- The maximum Koan-owned candidate buffer never exceeds the effective batch size.
- A paused consumer causes no unbounded provider advancement.
- Cancellation and disposal prevent later page requests.
- No supported stream invokes `QueryWithCount` or performs a hidden complete-source sort.
- Unsupported execution fails with Entity, adapter, missing capability/reason, and corrective action.
- Boot/composition facts state only capabilities known at composition; runtime facts state the selected
  or rejected per-Entity execution without pretending lazy repositories were elected at startup.
- Current limitations explicitly exclude snapshot consistency, mutation-safe iteration, resumability,
  and constant-memory claims about opaque provider internals.

## Stop conditions

- Stop if the design requires a second public stream/container abstraction.
- Stop if unsupported execution silently returns to full-result materialization.
- Stop if stable ordering or provider-side bounds cannot be proved for an advertised adapter.
- Stop if streaming forces the blocked Lifecycle public break into this package wave.
- Stop before Communication implementation, publication, push, tag, or release.

## Session close

Update the parent R07 card, [`../../PROGRESS.md`](../../PROGRESS.md), and
[`../../NOW.md`](../../NOW.md) with exact conformance results and the next safe action before marking
this child passed, blocked, or stopped.
