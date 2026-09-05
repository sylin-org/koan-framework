---
type: PLAN
domain: framework
title: "AE-04 - Explainable change review"
audience: [maintainers, architects, ai-agents]
status: current
last_updated: 2026-09-05
framework_version: v1.0.0
validation:
  status: reviewed
  scope: work specification; review usefulness pending
---

# AE-04 — Explainable change review

Read the [charter](../README.md); claim and report work in [PROGRESS](../PROGRESS.md).
Dependencies: AE-02 and AE-03 change receipts. This card owns the review format and its trial.

## Outcome and existing evidence

A reviewer can identify what changed, its required dependencies, actual runtime participation,
verified behavior, and remaining unknowns from one concise report.

Start with [composition lockfiles](../../../guides/composition-lockfile.md),
[proof and observability](../../../capabilities/operations/proof-and-observability.md), and
[the read-only explanation skill](../../../../.agents/skills/koan-explain/SKILL.md).

## Deliver and prove

1. Produce a worked report from the existing experiment receipts: requested outcome, before
   and after revisions, static composition change, observed runtime selections, behavior checks,
   and unknowns. Link every material assertion to its evidence.
2. Label the run, configuration context, exact resolved versions, and evidence freshness.
   A build lock describes availability; runtime facts describe that run; behavior checks
   establish only their tested boundary. A missing proof is not a successful comparison.
3. Give the report to a reviewer who did not implement the change. Ask them to identify the
   changed capability, participating provider, preserved contracts, and unsupported assumptions.
   Record answers, evidence lookups, review minutes, and unresolved questions.
4. Try a deliberately incomplete or mismatched evidence set and verify the report exposes the
   uncertainty. Automate recurring assembly only if the worked format earns it.

## Acceptance and limits

- The report is reproducible from named receipts and helps assess a real application change.
- Material claims distinguish declaration, observed runtime state, and executed behavior.
- Secrets and business data are excluded; a report needs no write access to production.
- Reviewer findings are retained, including confusion and additional inspection required.

If no independent reviewer is available, retain a maintainer rehearsal and leave usefulness
unvalidated for AE-06. Redirect if the report becomes a parallel diagnostic model or requires
manual duplicate truth. A new dashboard, CLI, or mandatory PR gate is not the initial deliverable.
