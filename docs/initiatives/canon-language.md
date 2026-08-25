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
   operations (`Stage`, `Promote`, `Rebuild`) return results and end the line.
3. **Ownership precedence:** the model's own override speaks first, then composition-registered
   gateway lambdas in registration order, then classic `ICanonPipelineContributor<T>` implementations.
4. **Phases are verbs** in pipeline order; every hook, enum value, and doc phrase uses the identical word.

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

## Hooks

| Hook | Kind | Shape | Status |
|---|---|---|---|
| `OnIntake` | intervene | `(T candidate) → T`; mutate in place, return it; null/different-instance fails correctively | **shipped** (virtual on `CanonEntity<T>` + `Person.Canon.OnIntake(lambda)`) |
| `OnCommitted` | observed | `CanonizationResult<T>` | shipped |
| `OnParked` / `OnFailed` | observed | result envelope | shipped |
| `OnMatched` | observed | match decision (new / merged-into / identity-union detail) | **reserved** — payload design pending |
| `OnReconciled` | observed | per-field decisions stream | reserved |
| `OnProjection` / `OnDistributed` | intervene/observed | view registry + fan-out contracts | reserved |

Reserved words may not be reused for other meanings; implement them with the documented intent or
amend this document first.

## Surfaces

- `person.Canonize()` — arrival through the funnel (immediate)
- `Person.Canon.*` — type-scoped gateway: hook registration (chaining), staged payloads, promotion,
  rebuild (terminal operations grow here)
- `ICanonPipelineContributor<T>` — classic contributor seam for logic needing services, diagnostics,
  or reuse across models

## Glossary (MDM bridge)

MatchKey ≈ blocking key · Reconcile ≈ conflict resolution · Keep.* ≈ survivorship rules ·
CanonIndex ≈ cross-reference table · identity-union ≈ merge/purge · CanonStage ≈ staging table.
