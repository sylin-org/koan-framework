---
type: PLAN
domain: framework
title: "Canon–Jobs bridge: stages ride the job engine"
audience: [maintainers, framework-authors]
status: proposed
last_updated: 2026-08-25
framework_version: v1.0.0
---

# Canon–Jobs bridge: stages ride the job engine

## Problem

`CanonStage<TModel>` is a hand-rolled queue without an engine. It carries a status lifecycle
(`Pending → Processing → Completed | Parked | Failed`), a transition log, correlation ids, and
durable persistence — and then stops: no retry clock, no sweeper, no claiming, no multi-node
dispatch, no health projection. The docs are honest about it: *"There is no built-in sweeper or
retry clock; the queue is yours to work"* and *"someone (or some job) must promote them."*

Meanwhile the Jobs pillar owns a proven engine for exactly these concerns: CAS claims, guarded
renewal with lost-lease abandon, retry/backoff, lanes/pools/gates, schedules, the wake stamp,
health and facts projection, and a 85+-spec behavioral contract proven across five stores.

This is the "two implementations converging" case the house laws name: collapse them rather than
grow a second queue engine inside Canon — and rather than bolt per-feature sweepers onto Canon
forever (the original stage-promote plan would have been the first bolt).

## Decision

**A bridge package, `Sylin.Koan.Canon.Jobs`** — the `Koan.Data.AI` precedent: a seam package that
activates when both pillars are referenced. No new capability interface inside Canon (the
`IStoreSignalChannel` lesson: don't invent an adapter surface where one package boundary will do).

- **Canon alone** (no Jobs): today's behavior, unchanged — `Canonize()` runs in-process; staged
  receipts wait as data; promotion is application code. Zero regression surface.
- **Canon + Jobs + bridge**: parked and staged receipts become canonization **jobs**.

### The semantic split: stage = receipt, job = execution attempt

`CanonStage` keeps what *arrived*: payload, origin, correlation, transition story, triage state.
`JobRecord` owns what *happened during processing*: attempts, retries, lease, lane, owner. The
bridge's job handler is deliberately tiny:

```csharp
load stage → await stage.Payload.Canonize(...) → stage.MarkCompleted(...)
```

At-least-once execution is safe here in a way it is not everywhere: Canon's core promise is
convergence — re-canonizing an arrival merges by match key rather than duplicating. A retried
canonization job is idempotent by the pillar's own contract, so the Jobs floor and Canon's
semantics reinforce instead of fighting.

### What each hard problem becomes

| Today's gap (Canon solo) | Becomes (with bridge) |
|---|---|
| No promote operation | `Person.Canon.Promote(stageId)` — submits the canonization job (naming authority: `canon-language.md` owns `Promote`) |
| No sweeper / retry clock | A scheduled job (`MyJob.Jobs.Schedule` / `[JobAction(Schedule=…)]`) sweeping `Pending` stages — code-first, opt-in, ordinary Jobs |
| No retry policy | `RetryBaseDelay` backoff + `[JobAction(MaxAttempts=…)]` |
| No throughput control | Lanes, pools, gates |
| No multi-node processing | CAS claims + wake stamp (JOBS-0009) |
| No queue observability | `WithStatus`, `JobsHealthContributor`, facts |

### Public expression

```csharp
// Reference the bridge; nothing else changes in application code.
builder.Services.AddKoan();   // Canon + Jobs + Sylin.Koan.Canon.Jobs referenced

var parked = await person.Canonize(o => o.WithStageBehavior(CanonStageBehavior.StageOnly), ct: ct);
// Outcome Parked; the bridge has enqueued a canonization job for the receipt.
// Retries, lanes, multi-node dispatch, and health are Jobs-owned from here.

// Human-in-the-loop: a parked receipt that should NOT auto-process can be held
// (bridge option or [JobPersistence]-style gate), then released explicitly:
await Person.Canon.Promote(stageId);   // submits the canonization job
```

**Guarantee**: with the bridge referenced, every staged receipt is processed at-least-once with
Jobs' full operational surface, and promotion is one explicit verb. Without it, Canon behaves
exactly as documented today. **Correction**: the bridge refuses correctively if Jobs' durable
ledger is unavailable while staged receipts exist (same posture as `[JobPersistence(DataStore)]`),
rather than silently leaving receipts unprocessed.

### Non-goals

- Jobs never learns canon semantics; Canon never learns queueing. The bridge owns the seam.
- Canon's default in-process pipeline does not become asynchronous by magic — asynchrony arrives
  only with the bridge reference (Reference = Intent in both directions).
- The koan-review surface (`Koan.AI.Review`) stays decoupled; a review approval *may* call
  `Promote` in application code, but no framework coupling is introduced.

## Rollout slices

1. **Bridge core** — package, job handler (load → canonize → mark-completed), enqueue-on-stage
   hook, corrective refusal without durable ledger. Specs: park→auto-process→completed; retry on
   canonize failure converges (no duplicate canonical); bridge absent → today's behavior byte-for-byte.
2. **Promote verb** — `Person.Canon.Promote(stageId)` gateway op (+ optional Web route
   `POST /api/canon/{model}/stages/{id}/promote` following `CanonEntitiesController` conventions),
   corrective on unknown/terminal stage. This closes the Jobs-gateway pilot's final item.
3. **Sweeper recipe + docs** — scheduled-sweep recipe in the canon and jobs docs; capability leaf
   rows updated; `canon-pipeline.md`'s "queue is yours to work" paragraph rewritten to teach the
   bridge.

## Status

Proposed 2026-08-25, replacing the promote-only design direction. Ratification pending Leo.
