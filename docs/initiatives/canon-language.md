---
type: PLAN
domain: canon
title: "Canon language — nomenclature and extension grammar"
audience: [maintainers, framework-authors, ai-agents]
status: current
last_updated: 2026-08-25
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-25
  status: reviewed
  scope: normative lexicon for the Canon pillar, ratified by Leo 2026-08-25. This document wins any
    naming argument; new Canon surfaces must draw words from this table or amend it here first.
---

# Canon language

One word per moving part. Words come from plain English first, MDM/entity-resolution terms of art
second (recorded in the glossary so practitioners can map concepts inward).

## Grammar rules

1. **Base form intervenes, past participle observes.** `OnIntake` runs *before* its moment and may
   mutate the candidate; `OnMatched`, `OnCommitted`, `OnParked`, `OnFailed`, `OnDistributed` run
   *after* their moment and observe what happened.
2. **Registrations chain, operations terminate.** Hook methods on the type gateway return the gateway;
   operations (`Stage`, `Recover`, `Rebuild`) return results and end the line.
3. **Ownership precedence:** the model's own override speaks first, then composition-registered
   gateway lambdas in registration order, then classic `ICanonPipelineContributor<T>` implementations.
4. **Phases are verbs** in pipeline order; every hook, enum value, and doc phrase uses the identical word.
5. **Segment grammar on gateway namespaces:** phases are proper nouns (`Intake`, `Matching` —
   place-filters); reason categories are past participles (`Stalled`, `Refused` — outcome-filters
   read as adjectives). A plural noun mid-chain parses as an owner and is rejected.

## The funnel

`Intake → Validation → Matching → Reconcile → Projection → Distribution`

## Lexicon (old → new)

| Old | New | Term-of-art anchor |
|---|---|---|
| `[AggregationKey]` | `[MatchKey]` | blocking key |
| `[AggregationPolicy(First/Latest/Min/Max/SourceOfTruth)]` | `[Reconcile(Keep.First/Latest/Min/Max/From)]` | survivorship rule |
| `SourceOfTruth` (+ required sources) | `Keep.From(...)` (sources required; falls back to latest) | authoritative source |
| undeclared properties | implicit `Keep.Latest` (newest-wins) | survivorship default |
| phase `Aggregation` | phase **Matching** | blocking + matching |
| phase `Policy` | phase **Reconcile** | reconciliation |
| `DefaultAggregationContributor` | `DefaultMatchingContributor` | |
| `DefaultPolicyContributor` | `ReconcileContributor` | |
| `CanonPolicySnapshot` | `ReconcileDecision` | survivorship decision |
| `OnOnboarding` (virtual + gateway) | **`OnIntake`** | intake normalization |
| `Promote` (reserved operation) | **retired** — `Recover` releases held receipts (with optional repair) and re-enters at Intake | release of held work |
| `Vetoed` (proposed reason member) | **rejected** — every hold is a veto; categories are named by who stopped the record | |

## Holds and recovery (ratified 2026-08-25, with `canon-rides-jobs.md`)

| Term | Kind | Meaning |
|---|---|---|
| **Hold** | state + namespace | A record stopped in the funnel, preserved durably as a `CanonStage` receipt with its parked phase and reason. `Person.Canon.Hold.*` |
| **`CanonStage.ParkedPhase`** | field | The `CanonPipelinePhase` that parked the receipt — a triage label, never a resume cursor. Recovery always re-enters at **Intake**. |
| **`Stalled`** | reason category (participle) | Mechanical: the funnel could not proceed (missing match key, failed match, failed verification). Filed by the engine. |
| **`Refused`** | reason category (participle) | Business: a registered rule or a step deliberately said no (`ctx.Hold(why)`). Filed by the verb. |
| **`ctx.Hold(why)`** | intervene verb | On `CanonPipelineContext<TModel>`: deterministic veto — parks the receipt as `Refused` at the current phase with the justification on the receipt. Thrown exceptions remain the transient channel (Jobs retries them). |
| **Business checkpoint** | phase occupant | Post-Projection, pre-Distribution: framework-owned occupant running registered business rules (`OnRule`) as a set against the complete synthetic candidate. Position law: cheap checks early, business evaluation late. |
| **`OnRule`** | intervene registration | `Person.Canon.OnRule(async candidate => … ? null : "justification")` — chaining; runs at the business checkpoint in registration order. |
| **`Recover`** | operation | `Person.Canon.Hold.Recover(...)` — scope × decision matrix (all / phase / id × as-is / repaired). Re-enters at Intake through the Jobs engine; returns attempted / recovered / re-parked. |
| **`Counts`** | namespace | `Person.Canon.Hold.Counts.<phase | Refused | Stalled | All>` → held-record totals, index-served. |

`Promote` is retired: recovery with no repair *is* release, and one verb for release keeps the
surface honest. The stage-processing job is the receipt itself — `CanonStage<T>` implements
`IKoanJob<CanonStage<T>>`; deferred arrivals enqueue the receipt, recovery re-enqueues it.

## Hooks

| Hook | Kind | Shape | Status |
|---|---|---|---|
| `OnIntake` | intervene | `(T candidate) → T`; mutate in place, return it; null/different-instance fails correctively | **shipped** (virtual on `CanonEntity<T>` + `Person.Canon.OnIntake(lambda)`) |
| `OnCommitted` | observed | `CanonizationResult<T>` | shipped |
| `OnParked` / `OnFailed` | observed | result envelope | shipped |
| `OnRule` | intervene | `async candidate → string?` (null = pass; string = hold justification) | **ratified** — business checkpoint occupant |
| `OnMatched` | observed | match decision (new / merged-into / identity-union detail) | **reserved** — payload design pending |
| `OnReconciled` | observed | per-field decisions stream | reserved |
| `OnProjection` / `OnDistributed` | intervene/observed | view registry + fan-out contracts | reserved |

Reserved words may not be reused for other meanings; implement them with the documented intent or
amend this document first.

## Surfaces

- `person.Canonize()` — arrival through the funnel (immediate)
- `Person.Canon.*` — type-scoped gateway: hook registration (chaining), `Hold` namespace
  (`Counts`, `Recover`), rebuild (terminal operations grow here)
- `ICanonPipelineContributor<T>` — classic contributor seam for logic needing services, diagnostics,
  or reuse across models

## Glossary (MDM bridge)

MatchKey ≈ blocking key · Reconcile ≈ conflict resolution · Keep.* ≈ survivorship rules ·
CanonIndex ≈ cross-reference table · identity-union ≈ merge/purge · CanonStage ≈ staging table ·
Hold ≈ exception record (held for review) · Recover ≈ reprocess exception.
