---
type: ARCHITECTURE
domain: framework
title: "R01 - Ratify the Product Constitution"
audience: [architects, maintainers, ai-agents]
status: draft
last_updated: 2026-07-13
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-13
  status: reviewed
  scope: product constitution work-item specification
---

# R01 — Ratify the product constitution

- Tranche: `T1 — product constitution`
- Status: `passed`
- Depends on: R00
- Unlocks: R02
- Owner: maintainer

## Meaningful outcome

Contributors can make consistent product and architecture decisions without rediscovering Koan's
identity each session. The promise is concrete enough to reject attractive but misaligned work.

## Why now

Capability assessment needs agreed criteria. Without a constitution, existing breadth, samples, and
external-framework comparisons cannot be classified as core, extension, experiment, or debt.

## Evidence to read first

- [`../CHARTER.md`](../CHARTER.md).
- [`../../../architecture/principles.md`](../../../architecture/principles.md).
- Current architecture decisions and public getting-started material.
- Code and tests for references, bootstrap, module discovery, Entity extensions, and startup reporting.

## Decisions

### DECIDED

- Koan aims to be an opinionated meta-framework for agentic .NET applications.
- Progress is V0 to V1 in meaningful, small steps, with no scaffolding as an end state.
- Application code should read as business; framework complexity must remain inspectable.
- `Entity<T>` is the first-class semantic and IntelliSense spine.
- `Reference = Intent`, `Entity = Language`, `IntelliSense = Discovery`, and
  `Startup = Explanation` are candidate constitutional principles.

### DEFAULT

- Express the constitution as a concise canonical architecture document plus focused decision records.
- Keep product positioning separate from capability maturity claims.

### OPEN

- Which existing architecture principles are constitutional, tactical, obsolete, or overstated?
- What measurable signals define `meaningful`, `business-centric`, and `inspectable`?
- Which responsibilities remain explicitly application-owned?

## Scope

### In

- Reconcile the charter with current code-backed architecture principles.
- Define constitutional principles, product non-goals, decision tests, and responsibility boundaries.
- Identify superseded or conflicting documentation.
- Record unresolved architecture choices rather than hiding them in prose.

### Out

- Declaring existing capabilities supported.
- Runtime API redesign.
- Marketing launch copy or a V1 release date.

## Deliverables

1. A canonical, concise Koan product constitution linked from architecture documentation.
2. A decision test contributors and agents can apply to proposals.
3. An explicit map of retained, revised, and superseded principles.
4. Updates to the charter when ratification changes the draft direction.

## Verification

- Every constitutional claim has current code evidence or is clearly labeled directional.
- At least three plausible proposals can be consistently accepted or rejected using the decision test.
- Documentation lint, links, and `git diff --check` pass.
- Review confirms the constitution does not imply unverified support.

## Acceptance additions

- A maintainer explicitly ratifies the constitutional document.
- `CHARTER.md`, canonical architecture docs, and public overview language do not conflict.

## Stop conditions

- Stop if R00 has not reached a safe disposition.
- Stop if a principle requires unsupported capability claims to sound coherent.

## Proposal decision probes

| Proposal | Result | Constitutional reason |
|---|---|---|
| Add a machine-readable composition fact model shared by startup, health, tests, and agents | Continue | It makes framework-owned complexity inspectable through one fact model. |
| Add a scaffolding generator so applications begin with framework layers and placeholders | Reject | Generated preparation is not a meaningful business step; agent guidance and conventions should produce business code directly. |
| Add a global provider-backup operation as an `Entity<T>` extension for discoverability | Redirect | Backup is provider/application operations, not an intrinsic entity semantic; expose it through an explicit operational surface. |

## Acceptance result

- Outcome: PASS
- Date and commit: 2026-07-13; recorded by the R01 closure commit
- Evidence: canonical `docs/architecture/product-constitution.md`; accepted ARCH-0105; current
  `AddKoan`, registry, runtime fallback, composition, startup-report, Entity, and prior assessment
  evidence reviewed.
- Tests / validation: documentation lint reported zero errors across architecture, decisions, and
  getting-started; TOC parsed independently; links and `git diff --check` passed.
- Unsupported scenarios: this decision does not assert that current modules comply or upgrade any
  capability maturity.
- Follow-up work: R02 capability evidence baseline; R03 Entity Semantics Contract.
- Reviewer: operator-authorized initiative direction
