---
type: GUIDE
domain: framework
title: "R04 - Harden the Framework Foundation"
audience: [architects, maintainers, ai-agents]
status: draft
last_updated: 2026-07-13
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-13
  status: reviewed
  scope: foundation-hardening program work-item specification
---

# R04 — Harden the framework foundation

- Tranche: `T4 — foundation hardening`
- Status: `in-progress`
- Depends on: R03
- Unlocks: R05
- Owner: maintainer

## Meaningful outcome

A clean checkout reaches a useful result predictably, and failures explain composition, configuration,
backend selection, and corrective action. Application developers and coding agents can trust the common
path before reaching for advanced capabilities.

## Why now

The framework should not prove breadth on top of uncertain installation, startup, package, error, or
inspection foundations. R02 and R03 supply the evidence and semantic boundaries needed to rank this
work responsibly.

## Evidence to read first

- R02 evidence gaps and public-claim corrections.
- R03 Entity Semantics Contract and ecosystem dispositions.
- Clean-checkout automation, package metadata, bootstrap tests, startup reports, configuration
  descriptors, provider negotiation tests, health/diagnostic surfaces, and agent instructions.

## Decisions

### DECIDED

- Stable foundation work precedes capability expansion.
- Complexity removed from application code must remain visible through explanation and inspection.
- Default behavior must be deterministic, testable, and overrideable.

### DEFAULT

- Rank by frequency, blast radius, V0-to-V1 leverage, evidence gap, and reversibility.
- Execute hardening as smaller implementation cards rather than one large rewrite.
- Prefer cross-cutting contracts shared by human and agent consumers over parallel diagnostic systems.

### OPEN

- Which gaps most threaten first use and meaningful iteration?
- What machine-readable composition report should underlie logs, health, tests, and agent inspection?
- Which package/version and backend-negotiation guarantees are V1 foundations?

## Scope

### In

- Produce and execute a ranked set of bounded hardening cards.
- Cover installation, first use, configuration errors, startup reporting, inspectability, package
  coherence, backend negotiation, and agent-facing behavior.
- Add repository-owned failure fixtures and clean-environment proofs.
- Update maturity labels only when evidence earns them.

### Out

- Broad feature work unrelated to the golden path.
- Cosmetic observability without an underlying stable contract.
- Hiding provider or configuration ambiguity behind silent fallback.

## Required backlog fields

Every child card states the user-visible failure, affected personas, current evidence, owning layer,
smallest meaningful fix, failure behavior, tests, compatibility impact, and rollback/removal path.
The dependency order and live child-card state are maintained in
[`R04-BACKLOG.md`](../R04-BACKLOG.md).

## Verification

- Clean-checkout tests run in a documented, reproducible environment.
- Negative tests cover missing, ambiguous, incompatible, and unavailable providers.
- Startup facts are available in human-readable and stable machine-readable forms.
- Error messages identify intent, chosen/defaulted capability, source, and safe correction without
  exposing secrets.
- Package and documentation paths use supported versions and entry points.

## Acceptance additions

- All R05 prerequisites are `supported-foundation` or have an explicit bounded exception.
- Remaining foundation risks have owners and cannot silently invalidate the golden journey.

## Stop conditions

- Split any child card whose blast radius or migration cost cannot be independently reviewed.
- Stop if a proposed convenience reduces deterministic behavior or inspectability.
