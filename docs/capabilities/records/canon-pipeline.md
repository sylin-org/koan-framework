---
type: REFERENCE
domain: canon
title: "Canon pipeline"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-25
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-26
  status: passed
  scope: docs/capabilities/records/canon-pipeline.md - cold subagent journey on published feed
    (Sylin.Koan.Canon 1.0.12 / App 1.0.8 / Sqlite 1.0.14): StageOnly → engine → Completed;
    OnRule veto → Refused hold at Distribution with justification; Counts; Recover-with-fix →
    Completed. PASS 6m00s.
    Declarations on this page use the current language ([MatchKey] / [Reconcile] / OnIntake);
    Canon 1.0.12+ ships the current spellings; older published 1.0.x packages spelled them [AggregationKey] / [AggregationPolicy(Kind)] with identical
    semantics until the next release. Multi-channel claims state the shipped surface, not aspiration -
    see Channels.
---

# Canon pipeline

How arrivals become one trusted record: the funnel every channel feeds, the six phases that decide,
the three checkpoints that commit, and the provenance that explains the result.

## You need

| Piece | Package | Note |
|---|---|---|
| Canonical model + runtime | `Sylin.Koan.Canon` | `CanonEntity<T>`, `[MatchKey]`, `[Reconcile]`, `.Canonize()` |
| HTTP arrival surface (optional) | `Sylin.Koan.Canon.Web` | projects `/api/canon/{model}` writes through the runtime |
| Data provider | one connector | canonical records, stages, and indexes are ordinary entities |

Verified against: `Sylin.Koan.Canon` 1.0.7 or newer (patch releases compatible).

**Copy from here** (verified exemplar, kept compiling by the repo):
`samples/applications/CustomerCanon/` — model, contributors, host, and README.

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
var result = await person.Canonize(configure: o => o.WithOrigin("crm-sync"), ct: ct);
```

Shipped channels:

- **HTTP** — `Sylin.Koan.Canon.Web` routes `POST /api/canon/{model}` through the runtime (`CanonEntitiesController`). Discovery of the projected models lives at `GET /api/canon/models`.
- **In-process** — the `.Canonize()` extension resolves the host-owned runtime.

Everything else is application wiring around the same verb: a Messaging handler calls `.Canonize()`
with `WithOrigin("mq:{queue}")`; a Jobs worker ingests a batch the same way. For arrivals that must be
durable *before* processing, stage them instead of canonizing immediately:

```csharp
var parked = await person.Canonize(configure: o => o.WithStageBehavior(CanonStageBehavior.StageOnly), ct: ct);
// result.Outcome == Parked; the receipt is enqueued and the engine processes it at-least-once.
```

A staged receipt is a job (the receipt *is* the work item): the engine claims it, re-enters the funnel at Intake — `OnIntake` and business rules apply — and settles the receipt by outcome. A business-rule veto (`ctx.Hold(why)` or `OnRule`) parks it as **Refused** at the business checkpoint (Distribution); a mechanical block parks it as **Stalled**. Held records wait in `Person.Canon.Hold`. Recovery re-enters at Intake — a fix is a hypothesis, not a pass.

The hold surface, in full:

```csharp
// Register a business rule (chaining; runs at the business checkpoint, post-Projection):
Person.Canon.OnRule(async candidate =>
    await Crm.Exists(candidate.Email) ? null : "user not found in CRM");

// Inside a pipeline contributor — deterministic veto (exceptions remain the transient channel):
return ctx.Hold("user not found in CRM");

// The scoreboard — held totals, index-served:
_ = await Person.Canon.Hold.Counts.All();
_ = await Person.Canon.Hold.Counts.Refused();      // business vetoes, any phase
_ = await Person.Canon.Hold.Counts.Intake();       // per ratified phase

// Recovery — scope × decision; re-enters at Intake. Returns at release (receipt Pending);
// the engine settles it asynchronously to Completed / re-parked.
_ = await Person.Canon.Hold.Recover();                                   // everything, unrepaired
_ = await Person.Canon.Hold.Recover(CanonPipelinePhase.Intake);          // one phase
_ = await Person.Canon.Hold.Recover(stageId, h =>                        // one receipt, repaired
{
    h.Model.Email = "fixed@example.com";
    return h;                                                            // non-null ⇒ recover; null ⇒ stays held
});
```

**Bulk recovery sweep.** After fixing a systemic cause, resubmit a phase's holds on a cadence with an ordinary scheduled job:

```csharp
[JobAction("RecoverOnboarding", Schedule = "00:30:00")]
public sealed class OnboardingRecovery : Entity<OnboardingRecovery>, IKoanJob<OnboardingRecovery>
{
    public static async Task Execute(OnboardingRecovery job, JobContext ctx, CancellationToken ct)
        => _ = await Person.Canon.Hold.Recover(CanonPipelinePhase.Intake, ct);
}
```

The sweep's summary (attempted / recovered / re-parked) is the telemetry: records whose blocking
condition persists re-park with their reason — the loop is corrective, never silent.

## The six phases

`Intake → Validation → Matching → Reconcile → Projection → Distribution`, each phase's contributors
ordered by `Order`, then type name. **The first Failed or Parked event terminates the phase and the
operation** — later contributors and phases do not run, and nothing commits.

Two phases always have framework occupants:

- **Matching** — builds `Prop=value` tokens from `[MatchKey]` properties (all keys empty is a
  corrective `InvalidOperationException`), looks up the aggregation index, and aligns identity:
  no match → new canonical id; match → re-point the arrival at the existing record and load its
  snapshot for the Reconcile phase; multiple distinct ids behind one key → **identity union** — the
  ordinal-first id survives, the others are marked superseded in lineage under reason
  `identity-union` and accumulated in the `identity:merged-from` tag.
- **Reconcile** — applies each property's conflict rule (`[Reconcile(Keep.Latest)]` by default,
  newest-wins) against the loaded snapshot, writing a footprint per field: winner, arrival token,
  timestamps, evidence (`incoming` / `existing` / `selected`).

Projection and Distribution have no built-in occupants — they are application seams for read-model
updates and downstream fan-out. `result.ReprojectionTriggered` and `DistributionSkipped` report what
the caller requested (`ForceRebuild`, `RequestedViews`, `SkipDistribution`); enforcing it is contributor
work.

## Hooks: the model speaks first, then composition

Every canonical model can prepare its own arrivals by overriding one virtual — it runs at the very
front of Validation, before user validators and before identity keys are matched:

```csharp
public sealed class Person : CanonEntity<Person>
{
    public override Person OnIntake(Person candidate)
    {
        candidate.FullName ??= $"{candidate.FirstName} {candidate.LastName}".Trim();
        return candidate;
    }
}
```

Rules owned outside the model register on the type gateway and run right after the override, in
registration order:

```csharp
Person.Canon
    .OnIntake(p => p.Email = p.Email.Trim().ToLowerInvariant())
    .OnCommitted(result => auditTrail.Record(result.Metadata.CanonicalId!))
    .OnParked(result => reviewQueue.Enqueue(result));
```

Grammar: **base-form hooks intervene** (`OnIntake` may mutate the candidate); **past-participle hooks
observe** (`OnCommitted`, `OnParked`, `OnFailed` fire after the operation resolves, with the result
envelope). Registrations chain; operations terminate. Observer exceptions surface to the caller after
the durable state is recorded.

## Three checkpoints, reported not atomic

Successful commit order:

1. persist the canonical Entity;
2. upsert match-key indexes;
3. write audit entries.

A failure throws `InvalidOperationException` naming the checkpoint, with the provider exception inner:

- canonical write fails → *"no index or audit write was attempted."*
- index write fails → *"Canonical state is durable; zero or more match-key indexes may be durable.
  Audit was not attempted. Do not assume rollback or blindly retry with a new arrival."*
- audit fails → *"Canonical state and match-key indexes are durable; audit completion is unknown."*

Both storage shapes are ordinary Koan entities — the canonical model itself, plus `CanonIndex`
(`EntityType + Key → CanonicalId`, carrying origin/channel/seenAt attribution attributes) — lowered
through `DefaultCanonPersistence`. Replace `ICanonPersistence` to own all three stores together;
replace `ICanonAuditSink` for audit alone.

## Reading the result

Every operation returns a `CanonizationResult<T>`: outcome (`Canonized` / `Parked` / `Failed`), the
canonical entity, cloned metadata, the phase-event log, and the two projection flags. The committed
record's `Metadata` carries the explainable history — sources with channel attribution,
per-property footprints, reconcile decisions, lineage changes, lifecycle and readiness state.

## Correction box

- All match keys empty → "requires at least one match key value", naming the declared keys.
- Validation-phase rejection (contributor sets Withdrawn/Degraded + Failed) surfaces over HTTP as 422
  with the canonical echo and failed event detail; the store keeps exactly what was already true.
- Checkpoint failures name their checkpoint and explicitly forbid blind retry — replay safety comes
  from resubmitting the *same* arrival, which reconciles instead of duplicating.

## Do not, at this level

- Do not call entity Save/Delete beside the pipeline for canonical models — writes belong through
  `Canonize()` so indexes and provenance stay truthful.
- Do not treat held (Parked) stages as processed; Person.Canon.Hold.Recover(...) releases them back through the funnel at Intake.
- Do not assume a fuzzy-matcher exists: identity is exactly your declared keys.

## Glossary (MDM bridge)

MatchKey ≈ blocking key · Reconcile + Keep.* ≈ survivorship rules · CanonIndex ≈ cross-reference
table · identity-union ≈ merge/purge · CanonStage ≈ staging table.

## Leaves

- **Build steps:** [reconcile messy arrivals](../../recipes/reconcile-messy-arrivals.md)
- **Node:** [Canon reconciliation](canon.md)
- **Pipeline contract:** [Canon how-to](../../guides/canon-capabilities-howto.md)
