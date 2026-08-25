---
type: REFERENCE
domain: canon
title: "Canon pipeline"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-24
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-24
  status: passed
  scope: docs/capabilities/records/canon-pipeline.md - mechanics verified by code read of
    src/Koan.Canon (runtime, aggregation, policy, persistence, stage) against the live journey in
    canon.md (Sylin.Koan.Canon 1.0.7): create, same-key merge, replay idempotency, 422 refusal.
    Multi-channel claims state the shipped surface, not aspiration - see Channels.
---

# Canon pipeline

How arrivals become one trusted record: the funnel every channel feeds, the six phases that decide,
the three checkpoints that commit, and the provenance that explains the result.

## You need

| Piece | Package | Note |
|---|---|---|
| Canonical model + runtime | `Sylin.Koan.Canon` | `CanonEntity<T>`, `[AggregationKey]`, `[AggregationPolicy]`, `.Canonize()` |
| HTTP arrival surface (optional) | `Sylin.Koan.Canon.Web` | projects `/api/canon/{model}` writes through the runtime |

Verified against: `Sylin.Koan.Canon` 1.0.7 or newer (patch releases compatible).

## The constraint box

> **Canon is in-process.** It provides no distributed locking, transport delivery, durable replay, or
> recovery. Channels deliver arrivals *to* the pipeline; they are not part of it. Concurrency and
> durability come from your selected Data provider, and the commit sequence below is deliberately not
> atomic.

## Channels converge at one verb

Whatever delivers an arrival — a web request, a message handler, an import job, in-process code —
ends at the same funnel:

```csharp
var result = await person.Canonize(ct: ct);                       // immediate
var result = await person.Canonize(o => o.WithOrigin("crm-sync"), ct: ct);
```

Shipped channels:

- **HTTP** — `Sylin.Koan.Canon.Web` routes `POST /api/canon/{model}` through the runtime (`CanonEntitiesController`). Discovery of the projected models lives at `GET /api/canon/models`.
- **In-process** — the `.Canonize()` extension resolves the host-owned runtime.

Everything else is application wiring around the same verb: a Messaging handler calls `.Canonize()`
with `WithOrigin("mq:{queue}")`; a Jobs worker ingests a batch the same way. For arrivals that must be
durable *before* processing, stage them instead of canonizing immediately:

```csharp
var parked = await person.Canonize(o => o.WithStageBehavior(CanonStageBehavior.StageOnly), ct: ct);
// result.Outcome == Parked; payload persisted as a CanonStage receipt with transition history
```

A staged payload waits as data — `Pending → Processing → Completed | Parked | Failed`, full transition
log, correlation id — until something promotes it back through `.Canonize()` and marks the stage
completed. There is no built-in sweeper or retry clock; the queue is yours to work.

## The six phases

`Intake → Validation → Aggregation → Policy → Projection → Distribution`, each phase's contributors
ordered by `Order`, then type name. **The first Failed or Parked event terminates the phase and the
operation** — later contributors and phases do not run, and nothing commits.

Two phases always have framework occupants:

- **Aggregation** — builds `Prop=value` tokens from `[AggregationKey]` properties (all keys empty is a
  corrective `InvalidOperationException`), looks up the aggregation index, and aligns identity:
  no match → new canonical id; match → re-point the arrival at the existing record and load its
  snapshot for the Policy phase; multiple distinct ids behind one key → **identity union** — the
  ordinal-first id survives, the others are marked superseded in lineage under reason
  `identity-union` and accumulated in the `identity:merged-from` tag.
- **Policy** — applies each property's conflict rule (`[AggregationPolicy]`, defaulting to
  newest-wins) against the loaded snapshot, writing a footprint per field: winner, arrival token,
  timestamps, evidence (`incoming` / `existing` / `selected`).

Projection and Distribution have no built-in occupants — they are application seams for read-model
updates and downstream fan-out. `result.ReprojectionTriggered` and `DistributionSkipped` report what
the caller requested (`ForceRebuild`, `RequestedViews`, `SkipDistribution`); enforcing it is contributor
work.

## Three checkpoints, reported not atomic

Successful commit order:

1. persist the canonical Entity;
2. upsert aggregation indexes;
3. write audit entries.

A failure throws `InvalidOperationException` naming the checkpoint, with the provider exception inner:

- canonical write fails → *"no index or audit write was attempted."*
- index write fails → *"Canonical state is durable; zero or more aggregation indexes may be durable.
  Audit was not attempted. Do not assume rollback or blindly retry with a new arrival."*
- audit fails → *"Canonical state and aggregation indexes are durable; audit completion is unknown."*

Both storage shapes are ordinary Koan entities — the canonical model itself, plus `CanonIndex`
(`EntityType + Key → CanonicalId`, carrying origin/channel/seenAt attribution attributes) — lowered
through `DefaultCanonPersistence`. Replace `ICanonPersistence` to own all three stores together;
replace `ICanonAuditSink` for audit alone.

## Reading the result

Every operation returns a `CanonizationResult<T>`: outcome (`Canonized` / `Parked` / `Failed`), the
canonical entity, cloned metadata, the phase-event log, and the two projection flags. The committed
record's `Metadata` carries the explainable history — sources with channel attribution,
per-property footprints, policy snapshots, lineage changes, lifecycle and readiness state.

## Correction box

- All aggregation keys empty → "requires at least one aggregation key value", naming the declared keys.
- Validation-phase rejection (contributor sets Withdrawn/Degraded + Failed) surfaces over HTTP as 422
  with the canonical echo and failed event detail; the store keeps exactly what was already true.
- Checkpoint failures name their checkpoint and explicitly forbid blind retry — replay safety comes
  from resubmitting the *same* arrival, which reconciles instead of duplicating.

## Do not, at this level

- Do not call entity Save/Delete beside the pipeline for canonical models — writes belong through
  `Canonize()` so indexes and provenance stay truthful.
- Do not treat Parked stages as processed; someone (or some job) must promote them.
- Do not assume a fuzzy-matcher exists: identity is exactly your declared keys.

## Leaves

- **Build steps:** [reconcile messy arrivals](../../recipes/reconcile-messy-arrivals.md)
- **Node:** [Canon reconciliation](canon.md)
- **Pipeline contract:** [Canon how-to](../../guides/canon-capabilities-howto.md)
