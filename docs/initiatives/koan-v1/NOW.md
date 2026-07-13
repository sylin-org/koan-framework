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
  scope: R04-02 Entity lifecycle activation ownership
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
  established, R04-01 passed, and R04-02's host lease, durable vector-model confirmation, and AI
  discovery-registry classification increments are green. No capability maturity label changed.

## Next safe actions

1. Treat the leased host binding, late Data.AI logger resolution, uncached durable vector-model
   registry, and process-wide immutable AI type discovery as the stable R04-02 base.
2. Run the `explore` skill before the next production increment.
3. Map `KoanAutoRegistrar` lifecycle activation into `EntityEventRegistry`'s static closed-generic
   handler arrays. Establish whether activation should be host-owned, removable, or idempotent across
   sequential hosts before changing behavior.
4. Add a red repeated-host probe for the proven handler-duplication shape, then repair that one owner
   without moving immutable type/reflection facts into DI.
5. Continue owner-by-owner through relationship metadata, `AppHost.Identity`, and the non-hosted
   `StartKoan()` path.
6. Do not mark R04-02 passed until sequential and parallel ownership probes cover every named owner and
   missing/disposed host behavior is corrective.

## Expected working tree

R04-01 and R04-02's host-lease and vector-model confirmation increments should be committed. The AI
discovery-registry classification should remain one reviewable contract-and-evidence commit. Treat
every unrelated pre-existing change as user-owned.

## Verification at handoff

- Core host-binding and Data.AI projects build with zero errors;
- the Core self-executing suite passes 195/195 and the Data.AI suite passes 81/81;
- the AI unit project builds with zero errors and its self-executing suite passes 155/155;
- repeated-host probes prove different DI markers, Entity storage, and vector-model registry state;
- runtime and consumer tests for R04-02 must cover repeat hosts and disposed-state negative paths;
- documentation metadata, links, TOC, privacy scan, and `git diff --check` pass;
- no private downstream detail enters evidence or examples.

## Do not infer

- Do not broaden R04-02 into package, bootstrap, explanation, or Entity-facet work.
- Do not migrate public Entity vocabulary before false-success and host-lifetime hazards are bounded.
- Do not use a passing source build to claim package installation is fixed.
- Do not accept a zero test-command exit code without a discovered/executed test count.
- Do not promote maturity until external clean-room and failure evidence earns it.
