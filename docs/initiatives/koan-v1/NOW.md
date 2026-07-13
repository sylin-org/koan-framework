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
  scope: R04-02 first host-ownership increment
---

# Koan V1 Reorganization Current Handoff

Replace this file at every handoff. It is a restart point, not a diary.

## Active work

- Work item: [R04 — Harden the framework foundation](work-items/R04-foundation-hardening.md)
- State: `in-progress`
- Active child: [R04-02 — Establish host-scoped runtime ownership](work-items/r04/R04-02-host-scoped-runtime.md)
- Objective: make repeated hosts and tests unable to inherit disposed providers, registrations, or
  other host-owned runtime state.
- Foundation: R01 passed through [ARCH-0105](../../decisions/ARCH-0105-product-constitution.md) and the
  canonical [product constitution](../../architecture/product-constitution.md).
- Current state: R02 and R03 passed. All 13 surfaces are classified in
  [`CAPABILITIES.md`](CAPABILITIES.md); [ARCH-0106](../../decisions/ARCH-0106-entity-semantics-contract.md)
  and the canonical [Entity Semantics Contract](../../architecture/entity-semantics-contract.md)
  now bound implementation choices. The dependency-ordered [`R04-BACKLOG.md`](R04-BACKLOG.md) is
  established, R04-01 passed, and R04-02's first host-ownership increment is green. No capability
  maturity label changed.

## Next safe actions

1. Treat the leased host binding and late Data.AI logger resolution as the stable R04-02 base.
2. Run the `explore` skill before the next production increment.
3. Classify `VectorModelGuard._confirmed` first: prove whether its backend-dependent confirmation can
   cross hosts, then move or remove it without changing the public Entity/AI vocabulary.
4. Continue owner-by-owner through runtime registration sets, relationship/lifecycle metadata,
   `AppHost.Identity`, and the non-hosted `StartKoan()` path; keep immutable reflection facts static.
5. Do not mark R04-02 passed until sequential and parallel ownership probes cover every named owner and
   missing/disposed host behavior is corrective.

## Expected working tree

R04-01 should be committed. The R04-02 first increment should contain the host lease, Data.AI capture
repair, focused/repeated-host tests, and matching documentation as one reviewable unit.
Treat every unrelated pre-existing change as user-owned.

## Verification at handoff

- Core host-binding and Data.AI projects build with zero errors;
- the Core self-executing suite passes 195/195 and the Data.AI suite passes 80/80;
- the repeated-host probe proves different DI markers and in-memory storage across two sequential hosts;
- runtime and consumer tests for R04-02 must cover repeat hosts and disposed-state negative paths;
- documentation metadata, links, TOC, privacy scan, and `git diff --check` pass;
- no private downstream detail enters evidence or examples.

## Do not infer

- Do not broaden R04-02 into package, bootstrap, explanation, or Entity-facet work.
- Do not migrate public Entity vocabulary before false-success and host-lifetime hazards are bounded.
- Do not use a passing source build to claim package installation is fixed.
- Do not accept a zero test-command exit code without a discovered/executed test count.
- Do not promote maturity until external clean-room and failure evidence earns it.
