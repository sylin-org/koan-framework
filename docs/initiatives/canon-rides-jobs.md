---
type: PLAN
domain: framework
title: "Canon rides the job engine (Jobs is not optional)"
audience: [maintainers, framework-authors]
status: proposed
last_updated: 2026-08-25
framework_version: v1.0.0
---

# Canon rides the job engine (Jobs is not optional)

> Supersedes the bridge-package proposal of earlier today (git history keeps it). The pivot: a
> bridge made "Canon without Jobs" a supported mode; the review question that killed it — *should
> Canon exist without Jobs at all?* — exposed that the mode was a trap, not a feature.

## Problem

`CanonStage<TModel>` is a hand-rolled queue without an engine: status lifecycle, transition log,
correlation, durable persistence — then nothing. No retry clock, no sweeper, no claiming, no
multi-node dispatch, no health. The docs apologize: *"the queue is yours to work."* Meanwhile the
Jobs pillar owns a proven engine for every one of those concerns, with an **in-memory floor** that
requires zero infrastructure.

A staging surface whose receipts rot unless the application builds its own sweeper is not
flexibility; it is a documented trap. The answer is not to gate the engine behind an optional
bridge — it is to make the engine part of Canon's composition.

## Decision

**`Koan.Canon` depends on `Koan.Jobs`.** Not a bridge package, not an optional mode. Module
auto-discovery composes Jobs wherever Canon is referenced; the Jobs ledger election grades itself
by the host (in-memory data floor → in-memory ledger; durable connector → durable ledger), so
Canon keeps a zero-infrastructure story end to end.

### The clarification the engine forces: machine-deferred ≠ human-held

Today `Parked` conflates two intents. With an engine they must be distinct:

| Intent | Trigger | Mechanism |
|---|---|---|
| **Machine-deferred** — "process this soon, durably" | `CanonStageBehavior.StageOnly` | Receipt created **and enqueued**: at-least-once processing, retry/backoff, lanes, multi-node CAS claims, wake |
| **Human-held** — "wait for approval" | Contributor park (`StageStatus.Parked`) | Receipt created, **no job**; waits for `Person.Canon.Promote(stageId)` |

This is the semantic cleanup staging always needed: the receipt table stops meaning both
"waiting for a machine" and "waiting for a human."

### What each gap becomes

| Canon solo today | With the engine |
|---|---|
| No promote operation | `Person.Canon.Promote(stageId)` — enqueues the held receipt (naming: `canon-language.md`) |
| No sweeper / retry clock | **Built-in**: a default scheduled sweep over `Pending` machine-deferred receipts (opt-out via options), plus ordinary Jobs retry policy |
| No throughput / multi-node control | Lanes, pools, gates, CAS claims, wake stamp (JOBS-0009) |
| No queue observability | `WithStatus`, `JobsHealthContributor`, facts |

### Public expression

```csharp
builder.Services.AddKoan();   // Canon referenced ⇒ Jobs composed; ledger grades by host

// Durable handoff: enqueued immediately, processed at-least-once.
var parked = await person.Canonize(o => o.WithStageBehavior(CanonStageBehavior.StageOnly), ct: ct);

// Human hold: contributor parks; nothing processes until release.
await Person.Canon.Promote(stageId);
```

**Guarantee**: every machine-deferred receipt is processed at-least-once with Jobs' full
operational surface; every human-held receipt is processed exactly once someone says so.
**Correction**: staging with no data adapter at all behaves as today's in-memory floors dictate;
a failed canonization inside a job retries under Jobs policy and converges by match key —
at-least-once is safe because Canon's core promise is idempotent convergence.

### Costs, stated plainly

- Every Canon consumer gains Jobs' worker thread and health/facts surface. The idle worker is
  near-free, but it is a behavior surface — documented, not hidden.
- Specs asserting receipts *stay parked* (`CanonParkAndSweepFlow` and friends) flip to expecting
  processing under StageOnly, or use explicit human-hold. The spec diff is the map of the
  semantic change.
- Release trains couple at the minor boundary (bounded dependency ranges already govern this).

### Non-goals

- Jobs never learns canon semantics; the canonization pipeline stays in-process inside the job
  handler (load receipt → canonize → mark completed — three lines).
- Immediate (synchronous) canonization does not become a job; only staged/deferred arrivals ride
  the engine.
- `Koan.AI.Review` stays decoupled; an approval may call `Promote` in application code.

## Rollout slices

1. **Dependency + enqueue** — Canon→Jobs reference; StageOnly enqueues; human-hold parks without
   a job; built-in sweeper job (opt-out). Spec updates where "stays parked" flips.
2. **Promote** — `Person.Canon.Promote(stageId)` gateway op (+ Web route
   `POST /api/canon/{model}/stages/{id}/promote` per `CanonEntitiesController` conventions),
   corrective on unknown/terminal stages. Closes the Jobs-gateway pilot's final item.
3. **Docs** — `canon-pipeline.md`'s "queue is yours to work" paragraph retires; capability leaf
   rows; howto staging sections teach the two intents; Jobs docs gain the canon-handler example.

## Status

Proposed 2026-08-25 (second revision — Leo's call: no Canon-without-Jobs mode). Ratification
pending Leo.
