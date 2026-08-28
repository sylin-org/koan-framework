---
type: GUIDE
domain: framework
title: "A05 - Coming from ASP.NET Boilerplate / ABP"
audience: [maintainers, ai-agents]
status: draft
last_updated: 2026-08-27
framework_version: v1.0.12
validation:
  date_last_tested: 2026-08-27
  status: reviewed
  scope: migration-guide work-item specification
---

# A05 — Coming from ASP.NET Boilerplate / ABP

- Tranche: `T1 — Artifacts`
- Status: `draft`
- Depends on: none (drafting); publication gated on A02
- Unlocks: A09 (launch copy links it)
- Owner: maintainer

## Meaningful outcome

A short, honest public doc, `docs/getting-started/coming-from-abp.md` (final location decided at
execution), that meets the displaced ASP.NET Boilerplate audience: classic ABP reached
end-of-support in May 2026, and teams are choosing between ABP vNext and leaving frameworks. The
doc maps ABP concepts to Koan equivalents, states what Koan does not have, and never disparages.

## Why now

This is unowned search traffic with direct intent. It is also the highest-trust format available:
a migration guide that admits gaps converts skeptics better than any launch post.

## Content contract

- A concept-mapping table: app services → entity grammar + controllers; repositories → `Entity<T>`
  verbs; module system → package-reference composition; multi-tenancy → `Sylin.Koan.Tenancy`;
  background jobs → `Sylin.Koan.Jobs`.
- An explicit "what you will not find here" section: no module marketplace, no commercial support,
  no auto-API for arbitrary service layers — link A06.
- Factual framing of the landscape (end-of-support dates) with sources; no speculation about
  quality or motives (ACCEPTANCE §0).
- One worked mini-migration: a small ABP-shaped CRUD slice expressed in Koan, runnable.

## Evidence to read first

- [`../../../docs/getting-started/adopt-existing-app.md`](../../../docs/getting-started/adopt-existing-app.md)
  — the existing adoption path this doc complements.
- [`../../../docs/reference/capability-map.md`](../../../docs/reference/capability-map.md) —
  maturity statements the doc must not overstate.

## Acceptance criteria

- [ ] Doc exists with the mapping table, the gaps section, and one runnable worked slice.
- [ ] Every maturity claim matches the capability map's current state.
- [ ] ACCEPTANCE §0 and §1 pass.

## Proof

Doc path linked from `PROGRESS.md`; recipe-style validation block filled after a cold run of the
worked slice.
