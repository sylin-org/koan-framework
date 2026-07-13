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
  scope: R01 product-constitution handoff
---

# Koan V1 Reorganization Current Handoff

Replace this file at every handoff. It is a restart point, not a diary.

## Active work

- Work item: [R01 — Ratify the product constitution](work-items/R01-product-constitution.md)
- State: `in-progress`
- Objective: give maintainers and coding agents a concise, evidence-aware constitution for deciding
  what belongs in Koan and what does not.
- Foundation: R00 passed with all live published branch tips clean and an explicit acceptance of
  retained historical residue.
- Draft input: [`CHARTER.md`](CHARTER.md) expresses the intended product reality but is not yet
  canonical architecture.

## Next safe actions

1. Read the R01 card, charter, current architecture principles, public README, getting-started overview,
   and the code-backed composition entry points.
2. Classify each candidate principle as constitutional, directional, tactical, obsolete, or overstated.
3. Define observable decision tests for meaningful steps, business-code density, Entity-centered
   semantics, inspectability, and ecosystem ownership.
4. Draft the smallest canonical constitution and identify superseded or conflicting prose.
5. Apply the R01 acceptance additions before changing public positioning.

## Expected working tree

R00 closure updates initiative artifacts only. Treat every other pre-existing change as user-owned.

## Verification at handoff

- R01 claims cite current code or remain explicitly directional;
- canonical architecture and public overview do not conflict;
- documentation metadata, links, TOC, and `git diff --check` pass;
- no private downstream detail enters evidence or examples.

## Do not infer

- Do not reopen the accepted history disposition without evidence of actual privacy harm.
- Do not promote the draft charter into a support claim.
- Do not infer that private downstream success is repository evidence.
- Do not begin runtime changes until the capability baseline and Entity contract establish their need.
