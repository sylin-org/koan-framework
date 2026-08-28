---
type: GUIDE
domain: framework
title: "A06 - When not to use Koan"
audience: [maintainers, ai-agents]
status: draft
last_updated: 2026-08-27
framework_version: v1.0.12
validation:
  date_last_tested: 2026-08-27
  status: reviewed
  scope: honesty-material work-item specification
---

# A06 — When not to use Koan

- Tranche: `T1 — Artifacts`
- Status: `draft`
- Depends on: none
- Unlocks: A04, A09
- Owner: maintainer

## Meaningful outcome

A maintained "when not to use Koan" statement — README section and/or `docs/getting-started/`
page — that names, plainly, who should not adopt: teams needing commercial support SLAs or a
module marketplace; enterprises standardizing on an ABP-style module economy; greenfield scripts
where plain minimal APIs are genuinely enough; non-.NET stacks. Every launch artifact links it.

## Why now

The .NET community's reflexive objection to any meta-framework is "is this ABP again?" and "magic
I cannot debug." Naming the disqualifications up front is the single highest-trust move available,
and it costs nothing.

## Content contract

- Each disqualification states the need and the honest reason Koan does not serve it today.
- Each names the alternative a reader should evaluate instead, respectfully.
- Reviewed against the capability map so nothing contradicts recorded maturity.

## Acceptance criteria

- [ ] Statement exists, linked from the README's "Go further" area.
- [ ] A04's drafts and A09's runbook all reference it.
- [ ] ACCEPTANCE §1 passes.

## Proof

Path linked from `PROGRESS.md`.
