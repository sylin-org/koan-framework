---
type: ARCHITECTURE
domain: framework
title: "Koan V1 Initiative Charter"
audience: [architects, maintainers, ai-agents]
status: draft
last_updated: 2026-07-16
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-16
  status: reviewed
  scope: product mission, invariants, privacy, and session protocol
---

# Koan V1 Initiative Charter

Read this file in full before claiming any initiative work item. A session may have no access to
the conversation that created this initiative; this charter is the portable contract.

## Mission

Move Koan toward a stable, coherent V1 in which applications grow from V0 to V1 through meaningful,
small steps. Required application code should communicate business intent rather than framework
plumbing.

Koan does not eliminate complexity. It takes responsibility for infrastructure complexity, gives it
stable conventions, and makes its decisions inspectable.

## Product thesis

### Entity is the semantic spine

`Entity<T>` is Koan's first-class citizen and the natural attachment point for application semantics.
Data, relationships, events, context, jobs, caching, embeddings, access, HTTP, and agent behavior
should form one coherent language around the entity where the entity or entity type is genuinely the
subject.

### Reference, language, discovery, explanation

- **Reference = Intent** — referencing a module changes composition.
- **Entity = Language** — application behavior is expressed through a small business-readable grammar.
- **IntelliSense = Discovery** — referenced modules reveal relevant entity affordances where developers
  naturally look.
- **Startup = Explanation** — runtime reporting states which modules, providers, semantics, and
  guarantees became active.

### Meaningful steps

Every required application change must produce an observable business capability. Preparatory
architecture, generated layers, and disposable scaffolding do not count as progress from V0 to V1.

V0 code should remain valid as production concerns are added. A local-to-production transition may
change references and configuration; it should not rewrite business rules.

## Invariants

1. **Business-signal density.** Entities, policies, relationships, actions, and business tests dominate
   visible application code.
2. **Progressive complexity.** A new concern introduces the fewest concepts required for its actual
   guarantees.
3. **One canonical path per intent.** Alternate paths are explicit escape hatches, not competing
   front doors.
4. **Thin semantic facades.** Entity extension methods and facets remain small; implementation lives in
   host-scoped pipelines, providers, and services.
5. **Capability honesty.** Provider differences are declared, negotiated, inspectable, and never
   represented as guarantees the provider cannot meet.
6. **Projection parity.** Application, HTTP, job, and agent surfaces reuse applicable rules, context,
   and access policy.
7. **Safe removal.** Removing a module has predictable compile-time, runtime, data, and operational
   consequences.
8. **Stable foundations before breadth.** No new capability ring graduates while its dependencies fail
   their acceptance gate.
9. **Ecosystem collaboration.** Koan integrates specialist platforms and standards instead of owning
   every layer.
10. **Evidence before claims.** Public maturity follows reproducible repository evidence, not private
    use or implementation existence.
11. **Intent-to-code mapping.** Capability design begins with the business sentence and smallest honest
    application expression. Each visible concept maps to a real decision or guarantee; internal
    composition machinery does not become application ceremony.
12. **One decision owner.** Intrinsic complexity is centralized at the correct chokepoint. A slice
    finishes by deleting superseded owners rather than preserving parallel paths.
13. **Specificity cascade.** Mechanics rise only while their meaning remains identical; framework law,
    capability-family mechanics, pillar policy, adapter realization, and application policy stay distinct.
14. **Compile once, execute plans.** Structural composition is host-owned and memoized. Runtime paths
    execute immutable plans rather than rediscovering contributors or renegotiating providers.
15. **One model, truthful projections.** Startup, facts, health, errors, agent surfaces, and tests
    project canonical decisions without becoming alternate decision owners.

## Non-goals

The initiative does not make Koan:

- a replacement for ASP.NET Core, .NET Aspire, OpenTelemetry, or general agent orchestration;
- a mandatory repository/DTO/application-service architecture;
- a scaffolding generator whose output becomes application structure;
- a universal abstraction that hides every provider difference;
- a reason to attach unrelated global operations to `Entity<T>`;
- a collection of features copied from adjacent frameworks for parity.

## V0 and V1 working definitions

**V0** is the first meaningful vertical slice: a real business operation with an entity, a business
rule, persistence, an entry point, a test, and visible runtime composition.

**V1** is that same application made responsibly operable for its actual commitments: durable storage,
schema evolution, access control, isolation where applicable, health and telemetry, actionable errors,
recovery, governed agent access when enabled, repeatable deployment, and an upgrade path.

V1 does not mean using every Koan module.

## Private downstream boundary

Private applications may pressure-test Koan's mental models. They are an internal learning mechanism,
not public testimonials or repository evidence.

Hard rules:

- Refer only to a **private downstream application** or **private downstream evidence**.
- Never record names, repositories, paths, URLs, organizations, personas, domain vocabulary, data, or
  recognizable workflows.
- Never search for, enumerate, or infer private applications during initiative work.
- Reduce every observation to an anonymous minimal reproduction before it enters Koan.
- Do not claim public support from private validation alone.
- Public evidence must be independently reproducible inside the Koan repository.

The loop is:

```text
framework hypothesis
  -> private real-world pressure
  -> structural finding
  -> anonymous minimal reproduction
  -> general Koan contract
  -> repository guard
  -> private re-verification
```

## Ecosystem posture

Ecosystem exploration asks who should own a responsibility, not who Koan should defeat. Each finding
must be classified as:

- **Adopt** — use the standard or project directly;
- **Adapt** — borrow a proven principle but express it through Koan's grammar;
- **Integrate** — provide an explicit bridge;
- **Complement** — solve a different layer without coupling;
- **Decline** — state that Koan will not own the responsibility.

ABP and Rails are design-mining sources. ASP.NET Core, Aspire, agent frameworks, MCP, OpenTelemetry,
and provider ecosystems are primarily collaboration surfaces. Feature-count parity is not a roadmap.

## Session protocol

1. Read this charter, [`NOW.md`](NOW.md), [`PROGRESS.md`](PROGRESS.md), and the selected work card.
2. Verify prerequisites against the repository, not only the ledger.
3. Claim exactly one row in `PROGRESS.md`; record date and agent/model.
4. State the user's business sentence, smallest honest application expression, guarantee, and
   corrective failure before selecting internal types.
5. Complete the focused discovery/coalescence assessment: current owner, repeated mechanics,
   specificity, target owner, state lifetime, hot path, and delete/retain disposition.
6. Re-derive every load-bearing claim before editing.
7. Keep the diff within the card's declared scope. If reality invalidates the card, use STOP rather
   than forcing the planned work.
8. Run the card's verification and the applicable acceptance layers.
9. Update capability evidence only when the gate actually improves.
10. Close the progress row and record divergences or operator gates.
11. Replace `NOW.md` with a concise handoff that points to authoritative artifacts instead of copying
   their contents.
12. End with the files changed, commands run, results, remaining risks, and exact next safe action.

## Status and decision vocabulary

- `pending` — not started; prerequisites may be unmet. Deliberate deferral records a reason and revisit
  condition while remaining pending.
- `in-progress` — claimed by one active session.
- `blocked` — attempted but cannot proceed within the approved scope.
- `passed` — acceptance contract satisfied and evidence recorded.
- `stopped` — closed because evidence invalidated or superseded the approach; learning is preserved.

Decisions within a card are either:

- **DECIDED** — closed by the architect; changing it requires explicit approval;
- **DEFAULT** — recommended and changeable with a recorded justification;
- **OPEN** — the work item must produce a decision before implementation proceeds.

## Outcomes

- **PASS** — the scoped outcome and applicable acceptance layers hold.
- **BLOCK** — a fixable failure remains within scope; do not mark done.
- **STOP** — evidence or assumptions are stale, private boundaries would be crossed, or the correct
  action requires expanded authority. Record the divergence and re-plan.

## Change discipline

- One work item should produce one reviewable logical change.
- Do not mix foundation work with opportunistic feature additions.
- Do not commit or push unless the operator requests it.
- Preserve unrelated user changes.
- Architecture or public-policy changes update the appropriate ADR and canonical documentation when
  the work card graduates them; initiative drafts do not silently become canon.
