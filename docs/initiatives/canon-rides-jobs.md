---
type: PLAN
domain: framework
title: "Canon rides the job engine — holds are phase-specific, recovery is a pipeline"
audience: [maintainers, framework-authors]
status: proposed
last_updated: 2026-08-25
framework_version: v1.0.0
---

# Canon rides the job engine — holds are phase-specific, recovery is a pipeline

> Third revision. History: (1) bridge package — dropped, a bridge made "Canon without Jobs" a
> supported trap; (2) machine-deferred vs human-held — dropped, still transport-thinking. Leo's
> correction: the real axis is **the funnel**. Jobs is transport; Canon owns phases, holds, and
> recovery.

## Problem

`CanonStage<TModel>` is a hand-rolled queue without an engine, and its single `Parked` state
conflates every reason a record stops moving. A missing match key and a disputed identity are the
same bit today — and neither has a way back into the funnel. The docs apologize: *"the queue is
yours to work."*

## Decision

**`Koan.Canon` depends on `Koan.Jobs`** (module auto-discovery composes it; the ledger grades by
host — in-memory floor keeps zero-infrastructure coherent). Jobs is **transport and engine**;
Canon owns the funnel: phases, holds, and recovery.

### The funnel model

| Concept | Meaning | Mechanism |
|---|---|---|
| **Deferred arrival** (`StageOnly`) | Async intake — processing happens later, but it is *processing*: `OnIntake` still applies, the user can modify the candidate | Enqueued as a canonization job (retries, lanes, multi-node = Jobs engine) |
| **Phase hold** | The record stopped at a specific phase for a specific, fixable reason — preserved durably in Canon's `Entity<>` partitions | `CanonStage` gains an explicit **parked-phase**; examples: missing aggregation/match keys → *Onboarding hold*; mismatched identifiers / reconciliation conflict → *Matching hold*; failed verification → its phase's hold |
| **Recovery** | Phase-aware re-entry: apply a fixer, re-enter **at the phase that parked the record** — not from the top | `Person.Canon.Recover(...)` — operation with an optional fixer hook; bulk variant sweeps a phase |

### Recovery — the `Hold` namespace

`Hold` is a namespace under the gateway, not a method prefix: phases become typed members,
counts become walkable surface, and the closed type removes casts.

```csharp
// Counts — index-served, per phase and per reason category:
Person.Canon.Hold.Counts.Onboarding        // int
Person.Canon.Hold.Counts.Refused             // business-rule vetoes, any phase (reason-category view)
Person.Canon.Hold.Counts.All

// Triage — the held receipts themselves, paged:
Person.Canon.Hold.Onboarding.Records()
Person.Canon.Hold.Records()

// Triage-and-release — Records is a namespace; every walk names its scope explicitly:
Person.Canon.Hold.Records.All()                  // paged triage, no side effects
Person.Canon.Hold.Records.Onboarding()           // one phase's holds
Person.Canon.Hold.Records.Refused()                // reason-category view
Person.Canon.Hold.Records.All(i =>
{
    if (i.Step == CanonPhase.Onboarding) i.Model.Name = "Lalala";
    return i.Step == CanonPhase.Onboarding ? i : null;   // non-null ⇒ Recover; null ⇒ stays held
})
Person.Canon.Hold.Records.Onboarding(fix)        // async fixers take the same name — never an Async suffix
Person.Canon.Hold.Recover(id, fix)               // single known receipt
// A cursor walk returns the summary: attempted / recovered / re-parked — the walk IS the telemetry.
// A fixer that throws leaves that record held with the error on its receipt; the sweep continues.
```

`HoldContext<T>` carries `Model` (already `T` — the closed gateway pays the cast), `Step`
(parked phase), `Reason` (category), `Justification`, `Attempts`; the fixer mutates `Model`
in place, Canon-style, and may be async (a CRM lookup lives there).

Two dimensions on every hold: **`Phase`** (where the funnel stopped — the ratified `CanonPhase`
set drives the typed members) and **`Reason` category** (`Structural` | `Rule` | …) — a business
veto files under `Refused` no matter which phase raised it, so `Counts.Refused` is queryable without
every phase knowing every business rule.

### Recovery semantics

Recovery re-enters the funnel **at intake**. A fix is a hypothesis, not a pass: business rules get
a vote again, every gate re-applies, and convergence makes the re-run cheap. The parked phase is
therefore a **label for triage and queries** — "stopped at Matching" — never a resume cursor.

The hold records **which phase parked it** explicitly (today that lives only in transition
history), so triage and phase-scoped queries ("all Onboarding holds") are index-served.

### Hold observability

The `Counts` surface is index-served aggregation, projected three ways: the gateway members
above, a Canon health contributor onto `/health/ready` (the Jobs pattern), and composition facts.
The trend is the recovery signal: Onboarding holds 42 → 17 → 0 after the fix ships is the proof
the fix worked — and each `Recover` sweep's summary receipt updates it.

### Business-rule holds: `Hold(why)`

Data can be pipeline-correct and still fail a business rule ("user exists in CRM"). Steps get a
deliberate verb, and the engine gives the two failure modes different futures:

| Step outcome | Engine behavior |
|---|---|
| pass | funnel continues |
| **`ctx.Hold(justification)`** | **deterministic business veto → phase hold**, justification on the receipt; retrying is meaningless |
| thrown exception | transient failure → Jobs retry/backoff; exhaustion dead-letters |

A CRM outage retries; a user missing from the CRM holds. "Pipeline-correct but business-failed"
becomes a first-class outcome instead of an exception abused as control flow, and the
justification is what recovery triage reads.

### Jobs' role (transport, not semantics)

- Deferred arrivals ride the engine: at-least-once, retry/backoff, multi-node claims + wake.
- Bulk recovery sweeps are ordinary scheduled jobs.
- Single recovery and holds are data-at-rest operations — no job involved.
- At-least-once remains safe: re-canonization converges by match key.

### Naming — lexicon amendment required first

`canon-language.md` wins naming arguments and must be amended before implementation:
`Recover` (operation + fixer registration), phase-holds (`Onboarding hold`, `Matching hold`, …),
`CanonPhase` vocabulary, and whether `Promote` retires or survives as the Web-route verb for
release-without-modification. Hook grammar applies: a fixer registration **intervenes**
(base form); phase-hold observers **observe** (participle).

### Public expression

```csharp
builder.Services.AddKoan();   // Canon ⇒ Jobs composed; ledger grades by host

var r = await person.Canonize(o => o.WithStageBehavior(CanonStageBehavior.StageOnly), ct: ct);
// deferred: enqueued; OnIntake applies at processing time

// a reconciliation conflict parks the record as a Matching hold, durably, with its phase
await Person.Canon.Recover(stageId, fix: p => /* repair */ );
```

**Guarantee**: deferred arrivals process at-least-once with `OnIntake` applied; held records are
durable, phase-labeled, queryable, and recoverable — with re-entry at the parking phase.
**Correction**: recovery of a record whose blocking condition persists re-parks it at the same
phase with the failure reason on the receipt; nothing silently loops.

### Costs, stated plainly

- Canon consumers gain Jobs' worker surface (documented; near-free when idle).
- `CanonStage` gains a parked-phase field + index; specs asserting undifferentiated "stays parked"
  flip to phase-specific expectations.
- The lexicon amendment precedes code — no implementation may invent names.

### Non-goals

- Jobs never learns canon semantics; the pipeline stays in-process inside the handler.
- Immediate canonization does not become a job.
- `Koan.AI.Review` stays decoupled; an approval may call `Recover` in application code.

## Rollout slices

0. **Lexicon amendment** — `canon-language.md`: `Recover`, phase-holds, `CanonPhase`, `Hold(why)`
   (business veto vs transient exception), Promote's fate.
1. **Engine + phases** — Canon→Jobs dependency; StageOnly enqueues (OnIntake preserved at
   processing); `CanonStage.ParkedPhase` + index; phase-specific parking from contributors;
   `ctx.Hold(justification)` verb with deterministic-vs-transient semantics; spec flips where
   "stays parked" becomes phase-labeled.
2. **Recovery + observability** — single-record operation with fixer (re-enters at intake);
   `HoldCounts()` query surface; Canon health contributor; corrective re-park on persistent
   failure.
3. **Bulk recovery + built-in sweep** — scheduled job sweeping a phase under a registered fixer
   (opt-out); docs pass: "the queue is yours to work" retires, funnel + holds + recovery taught in
   `canon-pipeline.md`, the howto, and the capability leaf.

## Status

Proposed 2026-08-25 (third revision — Leo's funnel correction). Ratification pending Leo;
slice 0 blocks the rest.

## Consolidated surface (the ratification artifact)

``csharp
Person.Canon.Hold                                    // Hold gateway — thin router, no state
├── .Counts                                          // ── the scoreboard ──
│     .All              → Task<int>
│     .Onboarding       → Task<int>                  // per ratified CanonPhase member
│     .Matching         → Task<int>
│     .Refused            → Task<int>                  // reason-category view (business vetoes, any phase)
│     .<phase>          → Task<int>                  // grows with the lexicon, never a string
├── .Records                                         // ── triage + release cursor ──
│     .All()               → Task<PageResult<CanonStage<T>>>    // paged, no side effects
│     .Onboarding()        → Task<PageResult<CanonStage<T>>>
│     .Refused()             → Task<PageResult<CanonStage<T>>>
│     .<phase>()
│     .All(fix)            → Task<HoldSweepSummary> // fix: HoldContext<T> → HoldContext<T>? (null ⇒ held)
│     .Onboarding(fix)     →                        // or HoldContext<T> → Task<HoldContext<T>?> — same name
│     .Refused(fix)          →
│     .<phase>(fix)
└── .Recover(id, fix = null)  → Task<HoldOutcome>    // one known receipt
``

Supporting types: HoldContext<T> (Model already T · Step · Reason · Justification ·
Attempts · StageId); HoldSweepSummary(Attempted, Recovered, ReParked, Skipped).

Rules baked into the shape: no scope-less walks (.All is the explicit all); no Async
suffixes (async by nature — overloads share the name); non-null return ⇒ Recover (re-enter at
intake), null ⇒ stays held; a throwing fixer leaves the record held with the error on its
receipt and the sweep continues; every sweep returns its summary; Counts and Records grow
with CanonPhase — no string parameters; everything is a thin router over the ambient runtime.

## Segment grammar (naming law for this surface)

In a noun-chain surface, segment grammar carries the semantics: **phases are proper nouns**
(Onboarding, Matching — place-filters), **reason categories are past participles**
(Vetoed — outcome-filters, read as adjectives). Records.Rules() was rejected: a plural noun
mid-chain parses as an owner ("the rules' records") and, sitting beside phase members, pretends
a reason category is a phase. Participles cannot be misread — and agents mapping natural-language
requests to members (""records blocked by business rules"" → Records.Refused()) get a
semantically tight token instead of a guessable one.

## All holds are vetoes (Leo's correction, 2026-08-25)

Vetoed was rejected as a member name: it describes every hold, so it discriminates nothing.
The reason categories are named by **who stopped the record** — Stalled (mechanical: the funnel
could not proceed) and Refused (business: a rule said no; house corrective-refusal vocabulary).
Both are participles per the segment-grammar law. ctx.Hold(why) keeps its name — the verb is
hold; the category is derived from context (engine files Stalled, business rules file
Refused). The Stalled echo in the PMC-056 jobs-events vocabulary is deliberate: both mean a
machine-side stop; the lexicon amendment owns the pairing.