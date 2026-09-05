---
type: PLAN
domain: framework
title: "AE-02 - Repeatable application evolution"
audience: [maintainers, ai-agents]
status: current
last_updated: 2026-09-05
framework_version: v1.0.0
validation:
  status: reviewed
  scope: work specification; change and productivity results pending
---

# AE-02 — Repeatable application evolution

Read the [charter](../README.md); claim and report work in [PROGRESS](../PROGRESS.md).
Dependency: AE-01's reproducible consumers. This card owns evolution task contracts and their
receipts; the existing evaluation campaign continues to own its original experiments.

## Outcome and existing evidence

Evaluate meaningful second changes: add semantic search, change the shared approval policy,
and add a durable background task. Define an appropriate existing business need for each and
preserve declared routes, permissions, tenant boundaries, and stored data.

Read [agent skills](../../../guides/agent-skills.md), the
[existing evaluation protocol](../../../../evals/agent-race/README.md),
[skill rubric](../../../../evals/koan/rubric.md), and the current
[recipe index](../../../recipes/index.md). Identify reusable runners and graders before edits.

## Deliver and prove

1. Commit task contracts and acceptance checks before attempting the changes. Name starting
   revisions, dependencies, expected behavior, preserved contracts, and allowed assistance.
2. Implement the tasks as reproducible application steps; capture facts showing the selected
   provider actually participated. Search needs a meaningful ranking check, not just a 200.
   The durable job task must exercise the durability boundary it claims.
3. Run bounded agent attempts using applicable existing infrastructure. Isolate starting state
   and record model, harness, guidance, tools, task revisions, and failed attempts.
4. Record time to accepted change, review minutes, interventions, regressions, and available
   token/cost data. Distinguish automated success from human acceptance and first-use setup.
5. If making a comparison, use equivalent tasks and conditions and retain both arms' evidence.
   Follow the existing protocol's requirements before making any quantitative public claim.

## Acceptance and limits

- Every task has a reproducible baseline, acceptance checks, result, and preserved-contract proof.
- Missing measurements and incomplete runs are visible; no success is inferred from a build.
- Maintainer rehearsals and agent attempts are reported separately with their limitations.
- Guidance defects and framework defects have identified owners and focused reproductions.

Redirect if a task requires an unproved capability or if comparison conditions are confounded.
Repair or narrow the experiment explicitly. This card produces evidence; it promises no speed
multiplier and does not authorize publication of the existing campaign's private results.
