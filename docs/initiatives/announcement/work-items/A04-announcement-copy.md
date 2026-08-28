---
type: GUIDE
domain: framework
title: "A04 - Announcement copy pack"
audience: [maintainers, ai-agents]
status: draft
last_updated: 2026-08-27
framework_version: v1.0.12
validation:
  date_last_tested: 2026-08-27
  status: reviewed
  scope: announcement copy work-item specification
---

# A04 — Announcement copy pack

- Tranche: `T1 — Artifacts`
- Status: `draft`
- Depends on: A02 (the measured number); A06 (the honesty link)
- Unlocks: A09
- Owner: maintainer

## Meaningful outcome

The complete launch copy exists as reviewable drafts: the Show HN post, the r/dotnet
"I built this, tear it apart" post, a dev.to/Medium article outline, and a one-paragraph YouTube
pitch email. Every claim links to its receipt; every artifact links the "when not to use" material.

## Why now

Copy written before the receipt exists is marketing; copy written from the receipt is evidence.
This card turns A02's report and A03's recording into channel-native language.

## Per-channel notes

- **Show HN:** lead with the honest novelty — an agent-first, MCP-native meta-framework for .NET
  with measured agent-productivity numbers. Title carries "Koan .NET framework" phrasing for the
  name-collision hazard. First comment from the author covers motivation, limits, and the
  when-not-to-use link.
- **r/dotnet:** humble, evidence-forward, invitation to critique; the maintainer commits to
  answering every top-level comment for 48 hours. Never cross-post the HN text.
- **Article:** the boilerplate-objection paragraph appears verbatim — "AI writes boilerplate
  cheaply now, which is why we did not build a boilerplate framework. Koan is a semantic
  vocabulary your agent can hold in its head, verify against facts, and cannot get wrong." —
  followed by the A02 numbers.
- **YouTube pitch email:** one paragraph, the demo GIF linked, no attachments.

## Evidence to read first

- [`../CHARTER.md`](../CHARTER.md) — audiences, hazards, constraints.
- [`../ACCEPTANCE.md`](../ACCEPTANCE.md) — §0 and §1 apply to every sentence.

## Acceptance criteria

- [ ] All four drafts exist; every quantitative claim links to `REPORT.md`.
- [ ] Every draft links the when-not-to-use material (A06).
- [ ] No unlinked superlative anywhere; measured numbers only.
- [ ] A reviewer session reads each draft against the ACCEPTANCE gate before publication.

## Proof

Draft texts committed under `work-items/artifacts/` (or a launch folder) and linked from
`PROGRESS.md`.
