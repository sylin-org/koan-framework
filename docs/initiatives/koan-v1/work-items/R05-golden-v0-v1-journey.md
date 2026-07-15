---
type: SPEC
domain: framework
title: "R05 - Prove the Golden V0-to-V1 Journey"
audience: [architects, maintainers, ai-agents]
status: current
last_updated: 2026-07-15
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-15
  status: reviewed
  scope: golden-journey proof work-item specification
---

# R05 — Prove the golden V0-to-V1 journey

- Tranche: `T5 — golden V0-to-V1 proof`
- Status: `passed`
- Depends on: R04
- Unlocks: capability-ring expansion and V1 readiness assessment
- Owner: maintainer

## Meaningful outcome

From a new checkout, a developer or coding agent can build an anonymous but realistic application in
meaningful, small steps. At every step the application works, the code reads as business, and Koan can
explain the infrastructure it composed.

## Why now

The product promise becomes credible only when one maintained path proves it end to end. This proof
must rest on assessed and hardened foundations rather than an aspirational sample.

## Evidence to read first

- The ratified product constitution.
- R02 supported-foundation records.
- R03 Entity Semantics Contract.
- R04 clean-checkout, startup, error, negotiation, and inspection evidence.
- Current getting-started docs and representative maintained samples.

## Decisions

### DECIDED

- The domain is anonymous, repository-owned, and not derived recognizably from a private application.
- Each commit or documented step produces a meaningful result.
- Generated scaffolding is not counted as progress.
- The proof serves application developers, coding agents, operators, and reviewers.
- FirstUse remains the stable shortest result; R05 uses one cumulative `samples/GoldenJourney` app.
- The anonymous domain is a review request moving through assessment to a non-final recommendation.
- Checkpoints are executable business assertions, not sibling projects, scaffolds, or build variants.
- Business-named HTTP actions and bounded custom tools prevent raw mutation from bypassing rules.
- The dependency-ordered implementation cards live in [`R05-BACKLOG.md`](../R05-BACKLOG.md).

### DEFAULT

- Begin with one business entity and one observable business outcome.
- Add persistence, API, events/jobs, and one agentic capability only when each addition advances the
  same business story.
- Record both application code and Koan's composition explanation at every step.

### RESOLVED

- A fresh staged-package release reproduces both application contracts without retained artifacts:
  84 packages were compiled from one Git snapshot; FirstUse passed 8/8 and GoldenJourney 11/11 from
  the hydrated package-only feed.
- Independent readers and a context-free repeat reproduced the central journey, exposed two smaller
  truth queues, and verified their repairs. The maintainer accepted that evidence plus ongoing review
  and dogfeeding as sufficient; an extra identity-specific walkthrough would add ceremony, not a new
  acceptance signal.

## Scope

### In

- A clean-checkout script or documented command sequence.
- A minimal maintained golden application with stepwise history or equivalent checkpoints.
- Happy paths and representative failure/recovery paths.
- Human-readable and machine-readable composition evidence.
- Time, code-density, edit-surface, and failure-recovery observations.

### Out

- Exhaustive module coverage.
- A benchmark engineered only for the happy path.
- Public references to private downstream applications or their distinctive domain models.

## Required journey checkpoints

1. **V0:** one business rule, executable locally, with no external service prerequisite.
2. **Persisted:** the same business code gains durable Entity behavior by expressing intent through
   references and configuration.
3. **Reachable:** a useful web/API interaction exists without application-owned infrastructure glue.
4. **Reactive:** one event or job advances the business outcome and remains inspectable.
5. **Agentic:** one bounded AI- or agent-facing behavior adds business value with explicit backend
   negotiation and failure behavior.
6. **V1 proof:** clean checkout, tests, startup report, operational inspection, and recovery guidance
   are reproducible by independent readers and executable clean-room evidence.

## Verification

- Run from an environment without prior Koan build artifacts or hidden local services.
- Independent readers reproduce the supported path, record their coverage, and expose material
  confusion; the maintainer explicitly accepts the resulting evidence as sufficient.
- Every checkpoint has automated business assertions and captured composition facts.
- Forced provider/configuration failures produce actionable, redacted explanations.
- Reviewers can trace business behavior without reading framework bootstrap plumbing.
- Unsupported variations are explicit.

## Acceptance additions

- The complete path satisfies the meaningful-step definition in the charter.
- Results update the capability ledger; the golden sample does not over-promote adjacent capabilities.
- The public getting-started path is either aligned to this proof or explicitly distinguished from it.

## Stop conditions

- Stop if the sample requires undocumented setup, local residue, or application-owned infrastructure
  scaffolding to appear simple.
- Stop if the domain starts encoding identifying downstream details.

## Acceptance result

- Outcome: PASS
- Date and commit: 2026-07-15; evidence through `0e40b455`, closure recorded by the following
  documentation commit.
- Evidence: the stable FirstUse result; the cumulative GoldenJourney; fresh source/package proofs;
  converged operator/agent facts; forced backend rejection and recovery; independent evaluations and
  completed repair queues documented in `R05-BACKLOG.md` and R05-03.
- Tests / validation: external FirstUse 8/8 and GoldenJourney 11/11; current focused source journeys
  3/3; Jobs 76/76; MCP conformance 74/74; Streamable HTTP 18/18; affected warning-as-error builds;
  strict documentation validation.
- Unsupported scenarios: public package installation, distributed transports, exhaustive providers,
  production authorization/security design, and compatibility guarantees outside the stated proofs.
- Follow-up work: assess T6 capability-ring graduation, observe the first real `dev` publication, and
  handle bounded post-cycle items only through explicit compatibility decisions.
- Reviewer: maintainer and Codex; evidence sufficiency ratified by the maintainer on 2026-07-15.
