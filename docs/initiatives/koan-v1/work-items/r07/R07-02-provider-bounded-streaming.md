---
type: SPEC
domain: framework
title: "R07-02 - Make Entity Streams Genuinely Bounded"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-15
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-15
  status: reviewed
  scope: provider-bounded query paging beneath the existing Entity stream surface
---

# R07-02 — Make Entity streams genuinely bounded

- Tranche: `T6 — semantic capability ring`
- Status: `passed`
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

At this slice's opening, the release mechanism did not mint that breaking reverse-dependent closure
automatically. R07-02 therefore fixed the independent lower boundary while Lifecycle source changes
remained stopped. R07-03 subsequently passed the packaging gate; this historical sequencing did not
add a compatibility alias or weaken the accepted greenfield Lifecycle target.

## Defect this slice repairs

Before this slice:

- `AllStream` and `QueryStream` called the materializing query-and-count path and then enumerated a list;
- `batchSize` was accepted but ignored;
- documentation and agent guidance consequently alternated between overstating constant-memory
  behavior and understating the intended Entity grammar;
- ordinary `Page` requests also paid for count work they did not require; and
- natural calls such as `AllStream(ct)` did not compile because the optional batch argument preceded
  the cancellation token.

The implementation, executable consumer proofs, and public contract below repair these defects in the
working tree. The final regression, build, documentation, compatibility, diff, and privacy evidence is
recorded in this card's closure.

## Decisions

### DECIDED

- Keep `IAsyncEnumerable<TEntity>` as the only public streaming substrate.
- Do not add a public Pager, cursor, continuation, Flow, or provider-specific stream type.
- Add one precise adapter capability: provider-bounded paging. It means the adapter faithfully
  executes the coordinator-supplied pushable candidate filter, enforces `PageSize = N` before
  candidates are materialized into application memory, and reports provider-handled pagination plus
  the complete total order.
- Compose that proven primitive into a lazy async sequence in one internal Data.Core coordinator.
- `batchSize` is a positive maximum candidate-page size. The existing unbounded-loop page size is the
  default until configuration evidence earns another value.
- Enumeration performs no repository I/O until its first move, never calls the query-and-count path,
  and requests no later page after early disposal.
- Unsupported adapters, incomplete provider handling of pagination or the total order, invalid bounds,
  and dishonest execution metadata reject correctively before yielding. There is no materializing
  fallback.
- Filter fragments that a provider cannot push may remain residual only after the provider has enforced
  the candidate-page bound and complete total order. Koan evaluates those residual predicates
  pointwise; empty output from one candidate page does not end the stream.
- A stable total order is mandatory. The adapter must handle the complete order; Koan does not sort an
  unbounded source in memory.
- Caller-requested stream sorting has one exact initial semantic floor: a top-level, non-nullable
  `bool`, `byte`, `sbyte`, `short`, `ushort`, or `int` member. Every other caller sort, including an
  explicit Entity-identifier sort, rejects before provider I/O.
- After caller-order validation, Koan appends only the usual string Entity identifier as an opaque
  provider-stable page tie-breaker. Custom identifier shapes reject before provider I/O. The key is
  not a CLR or
  cross-provider collation promise.
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
- A capability token is necessary but not sufficient: the adapter must faithfully execute the
  coordinator-supplied pushable candidate filter, and each returned page must report that pagination
  and the complete requested order were handled.
- The coordinator buffers no more than the current candidate page and yields no item from a page whose
  execution metadata violates the plan.
- `batchSize` bounds Koan-visible candidates; it does not make unproved claims about opaque driver
  buffers.
- Numbered paging is not snapshot isolation. Concurrent writes can cause skips or duplicates; facts
  and documentation state that limit.
- Facts contain safe type/provider/reason identities, never Entity values, predicates, credentials, or
  logical-context payloads.

## Red/green plan

1. **Complete.** Core fake-repository proofs cover lazy first yield, exact page requests, no count, early
   disposal, cancellation, invalid bounds, empty residual pages, stable ordering, rejection before I/O,
   and enumeration-stable source, partition, and registered logical context.
2. **Complete.** `ProviderBoundedPaging` and one typed corrective rejection follow the existing
   relationship-query execution pattern.
3. **Complete.** One internal coordinator serves `Data` and `Entity`; the existing repository facade
   and cache decorator forward the same query/capability contract without creating another application
   path.
4. **Complete.** Count strategy is explicit, and stream/page requests do not compute totals
   accidentally.
5. **Complete.** Natural cancellation overloads compile and execute through the same path.
6. **Complete.** SQLite, PostgreSQL, CockroachDB, SQL Server, MongoDB, and Couchbase pass the shared
   qualification cell. InMemory, JSON, and Redis do not advertise the capability and pass the shared
   fail-closed cell.
7. **Complete.** The real Backup consumer crosses SQLite pages of `2/2/1`, stops on caller
   cancellation, and rejects InMemory/JSON before query or archive publication.
8. **Complete.** Public, connector, sample, and agent guidance now states only the executable boundary.
9. **Complete.** Affected regression, warning-reviewed solution build, strict docs, structural claim
   sweep, compatibility, diff, and privacy gates are recorded below.

## Current implementation evidence

| Boundary | Result | What it proves |
|---|---|---|
| Data.Core stream coordinator | 42/42 focused; 325/325 full | first-yield laziness, exact bounded pages, no count, consumer-paced advancement, cancellation/disposal, residual continuation, exact sort-floor, explicit-Id rejection, custom-key rejection before provider I/O, total-order and overclaim rejection, natural cancellation overloads, stable routed source/partition/registered carrier context, and selected/rejected runtime facts |
| SQLite focused provider proof | 1/1 | capability declaration, bounded ordered output, count-free default, explicit exact count, and refusal to claim pagination for an unhandled nested sort |
| Qualified adapter shared cells | 6/6 cells, one each | SQLite, PostgreSQL, CockroachDB, SQL Server, MongoDB, and Couchbase each realize deterministic provider-bounded paging through a real `AddKoan()` host; the shared corpus exercises boundary ordering for all six admitted caller-sort types plus the opaque string-Id tie-break |
| Unqualified adapter shared cells | 3/3 cells, one each | InMemory, JSON, and Redis reject before yielding rather than beginning their complete-source scan path |
| Real Backup consumer | 5/5 acceptance; 7/7 full | SQLite crosses candidate pages `2/2/1` and publishes the complete archive; cancellation during page 2 prevents its completion and archive publication; InMemory and JSON reject before query/archive publication |
| Connector regression | 236/237 | all current connector suites pass except Mongo's existing ZenGarden URI-preference case; Mongo is 67/68 and Couchbase 17/17. Mixed-case filter convergence is repaired centrally; the remaining issue is isolated in PMC-012 |
| Filtering regression | 92/92 foundation; 19/19 convergence | exact-case-first canonical field paths retain unambiguous case-insensitive public binding across provider translators |
| Documentation and agent guidance | 22 strict-doc items; 20/20 skills; 5/5 examples | DocFX and skills report zero warnings/errors; all changed instructional examples compile |
| Final repository gates | solution 0 errors / 19 existing warnings | the warning-reviewed Release solution build, structural claim sweep, compatibility review, diff check, and privacy scan pass; all warning-bearing files are unchanged from `HEAD` |

The implementation, conformance, consumer, and public-truth portions are green. Maturity labels remain
unchanged: this proves a bounded adapter execution contract, not snapshot iteration, provider-fleet
production certification, or public package support.

## Verification

- Core fake provider: exact page sizes and sequence; first item before later requests; no count; early
  break; zero-output residual page; cancellation before and during work; unsupported and overclaim
  rejection before output; context/source stability.
- Adapter conformance: every static row exactly once in deterministic order; candidate output never
  exceeds the requested bound; pagination and the complete total order are provider-handled; any
  residual filter is evaluated pointwise from bounded candidates; cancellation reaches provider work;
  generated command or SDK behavior proves limit/offset before materialization.
- Negative conformance: adapters using the current complete key scan do not advertise the capability
  and reject without starting that scan.
- Consumer proof: `StreamingBackupService` crosses multiple pages and stops on cancellation.
- Documentation: strict site build plus a structural sweep for false Pager/cursor and
  non-materialization claims.
- Broader regression: affected Data.Core and connector suites, then a warning-reviewed solution build.
- Privacy: no private downstream identity, path, persona, or workflow enters tracked artifacts.

## Acceptance additions

- **Proven:** a supported stream yields before its tail has been queried.
- **Proven:** every qualified page accepted for yielding contains no more than the effective batch
  size; an overfull provider result rejects before yielding any candidate from that page.
- **Proven:** a paused consumer causes no unbounded provider advancement.
- **Proven:** cancellation and disposal prevent later page requests.
- **Proven:** no supported stream invokes `QueryWithCount` or performs a hidden complete-source sort.
- **Proven:** unsupported execution fails with Entity, adapter, missing capability/reason, and corrective
  action.
- **Proven:** boot/composition facts state only capabilities known at composition; runtime facts state
  the selected or rejected per-Entity execution without pretending lazy repositories were elected at
  startup.
- **Documented limitation:** current guarantees explicitly exclude snapshot consistency, mutation-safe
  iteration, resumability, and constant-memory claims about opaque provider internals.

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
