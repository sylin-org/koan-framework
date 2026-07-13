---
type: GUIDE
domain: framework
title: "Koan V1 Reorganization Current Handoff"
audience: [maintainers, ai-agents]
status: draft
last_updated: 2026-07-13
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-13
  status: reviewed
  scope: R04 foundation-hardening decomposition handoff
---

# Koan V1 Reorganization Current Handoff

Replace this file at every handoff. It is a restart point, not a diary.

## Active work

- Work item: [R04 — Harden the framework foundation](work-items/R04-foundation-hardening.md)
- State: `in-progress`
- Objective: convert R02 evidence gaps and R03 semantic deltas into dependency-ordered, independently
  reviewable foundation cards, then execute the smallest P0 repair with proportionate proof.
- Foundation: R01 passed through [ARCH-0105](../../decisions/ARCH-0105-product-constitution.md) and the
  canonical [product constitution](../../architecture/product-constitution.md).
- Current state: R02 and R03 passed. All 13 surfaces are classified in
  [`CAPABILITIES.md`](CAPABILITIES.md); [ARCH-0106](../../decisions/ARCH-0106-entity-semantics-contract.md)
  and the canonical [Entity Semantics Contract](../../architecture/entity-semantics-contract.md)
  now bound implementation choices. No surface is currently labeled supported.

## Next safe actions

1. Create a ranked R04 backlog whose cards state user-visible failure, personas, evidence, owner,
   smallest meaningful fix, failure behavior, tests, compatibility, and rollback.
2. Put package coherence, false-success behavior, disposed-host/static lifetime, and bounded bootstrap
   execution ahead of vocabulary/facet migration.
3. Separate cards that change public API/compatibility from cards that establish clean-room tests and
   machine-readable explanation foundations.
4. Run the `explore` skill before the first production-code card and stop at its implementation approval
   checkpoint if required.
5. Begin only a bounded P0 card whose proof can land independently without assuming later refactors.

## Expected working tree

R03 closure adds architecture/research/initiative documentation only. Treat every other pre-existing
change as user-owned.

## Verification at handoff

- every R04 card is bounded, dependency-ordered, and traces to R02/R03 evidence;
- the first production card uses the mandatory exploration workflow before edits;
- runtime and consumer tests cover the stated failure and important negative paths;
- documentation metadata, links, TOC, privacy scan, and `git diff --check` pass;
- no private downstream detail enters evidence or examples.

## Do not infer

- Do not combine package, host-lifetime, bootstrap, explanation, and Entity-facet changes into one
  foundation rewrite.
- Do not migrate public Entity vocabulary before false-success and host-lifetime hazards are bounded.
- Do not use a passing source build to claim package installation is fixed.
- Do not promote maturity until external clean-room and failure evidence earns it.
