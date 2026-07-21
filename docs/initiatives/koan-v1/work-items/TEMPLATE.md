---
type: GUIDE
domain: framework
title: "Koan V1 Work-Item Template"
audience: [maintainers, ai-agents]
status: draft
last_updated: 2026-07-16
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-16
  status: reviewed
  scope: reusable initiative work-item structure
---

# RXX — Outcome-oriented title

- Tranche:
- Status: `pending`
- Depends on:
- Unlocks:
- Owner:

## Meaningful outcome

State what becomes usefully true for an application developer, coding agent, operator, or reviewer.

## Why now

Name the evidence or dependency that makes this the next smallest meaningful step.

## Evidence to read first

- Code:
- Tests:
- Documentation / decisions:
- Relevant external primary sources:

## Focused discovery and coalescence assessment

- User's business sentence:
- Smallest honest C# expression:
- Complete user action surface: references | code | decorations | configuration | context | runtime prerequisites
- Guarantee and corrective failure:
- Additional public concepts and semantic justification:
- Current owner, consumers, state lifetime, and hot-path cost:
- Repeated mechanics / closest pattern:
- Specificity decision: framework | capability family | pillar | adapter | application
- Disposition: keep | absorb | rebuild | delete
- One target owner and exact code placement:
- State compilation/cache boundary:
- Human, IntelliSense, and coding-model ergonomics:
- Cognitive branches and hidden ceremony removed/retained:
- Red proof and deletion list:
- Stop/redirect evidence:

## Decisions

### DECIDED

- Invariants this card may not reopen.

### DEFAULT

- Reversible working assumptions.

### OPEN

- Questions that must be answered with evidence.

## Scope

### In

- Concrete deliverables owned by this card.

### Out

- Adjacent work deliberately deferred.

## Business-code proof

Show the smallest before/after application code or observable workflow. The after state must remain a
meaningful application state, not generated scaffolding. Include every required reference, decoration,
configuration, context, and runtime prerequisite; optimize application concepts, not physical lines.
For behavior changes, name the focused compile/run fixture that proves the complete surface.

## Execution plan

1. Complete the focused assessment and confirm the public semantics before production edits.
2. Interrogate the closest pattern; decide what to keep, absorb, rebuild, and delete.
3. Capture baseline evidence.
4. Implement the smallest coherent change with its first meaningful consumer.
5. Add failure and inspection behavior.
6. Delete superseded ownership paths.
7. Verify and update support claims.

## Verification

- Focused tests:
- Broader regression tests:
- Documentation / sample checks:
- Manual or observable proof:
- Privacy check:

## Acceptance additions

List criteria beyond [`../ACCEPTANCE.md`](../ACCEPTANCE.md), or state `none`.

## Stop conditions

- Name evidence that would invalidate or materially redirect this work.
- Stop before any operator-gated or destructive action.

## Session close

Update [`../PROGRESS.md`](../PROGRESS.md), replace [`../NOW.md`](../NOW.md), and attach the acceptance
record before marking the card `passed`, `blocked`, or `stopped`.
