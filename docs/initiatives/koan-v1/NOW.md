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
  scope: R02 capability-baseline handoff
---

# Koan V1 Reorganization Current Handoff

Replace this file at every handoff. It is a restart point, not a diary.

## Active work

- Work item: [R02 — Build the capability truth baseline](work-items/R02-capability-baseline.md)
- State: `in-progress`
- Objective: distinguish what Koan specifies, demonstrates, verifies, supports, deprecates, and does
  not support using current repository evidence.
- Foundation: R01 passed through [ARCH-0105](../../decisions/ARCH-0105-product-constitution.md) and the
  canonical [product constitution](../../architecture/product-constitution.md).
- Current state: every row in [`CAPABILITIES.md`](CAPABILITIES.md) remains intentionally unassessed.

## Next safe actions

1. Freeze the assessed commit and inventory application-facing capability entry points.
2. Assess bootstrap/composition, Entity/data, and backend negotiation first because later surfaces
   depend on them.
3. For each surface, link code, tests, maintained samples, startup/error behavior, support limits, and
   compatibility expectation.
4. Assign the lowest maturity label fully supported by evidence.
5. Correct only materially unsafe front-door claims during assessment; rank other gaps for R04.

## Expected working tree

R01 closure may add the constitution and ADR, align public architecture prose, and update initiative
artifacts. Treat every other pre-existing change as user-owned.

## Verification at handoff

- every assessed label is reproducible from linked repository artifacts;
- unsupported and untested scenarios are explicit;
- documentation metadata, links, TOC, and `git diff --check` pass;
- no private downstream detail enters evidence or examples.

## Do not infer

- Do not promote a capability to match README language or roadmap preference.
- Do not treat a sample, project, or private deployment as a compatibility guarantee.
- Do not infer that private downstream success is repository evidence.
- Do not fix runtime gaps inside R02; record and rank them.
