---
type: GUIDE
domain: framework
title: "Koan V1 Work-Item Template"
audience: [maintainers, ai-agents]
status: draft
last_updated: 2026-07-13
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-13
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
meaningful application state, not generated scaffolding.

## Execution plan

1. Inspect the closest current pattern and confirm the owning layer.
2. Capture baseline evidence.
3. Implement the smallest coherent change.
4. Add failure and inspection behavior.
5. Verify and update support claims.

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
