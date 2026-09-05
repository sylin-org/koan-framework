---
type: GUIDE
domain: framework
title: "A11 - Terseness receipt (application LoC)"
audience: [maintainers, ai-agents]
status: draft
last_updated: 2026-08-28
framework_version: v1.0.30
validation:
  date_last_tested: 2026-08-28
  status: reviewed
  scope: terseness-receipt work-item specification
---

# A11 — Terseness receipt (application LoC)

- Tranche: `T1 — Artifacts`
- Status: `draft`
- Depends on: A03 (the demo app provides the Koan side)
- Unlocks: A04 (copy may quote the table)
- Owner: maintainer

## Meaningful outcome

The charter's claim 3 — terse, legible applications — carries a receipt anyone can check: the
flagship outcome implemented twice, once as the Koan demo app and once as an equivalent plain
ASP.NET Core application, with a stated line-counting method, the resulting table, and a
one-command reproduction. The table is the only number launch copy may quote from this card.

## Why now

The standing rule needs a receipt for the terseness claim, and none of the blind assessments
recorded codebase LoC. The comparison is cheap — both apps exist or are small to write — needs
no benchmark rig, no GPU window, and no agent, and it is the launch's most direct expression of
the positioning: zero-to-POC legibility, modules owning the necessary concerns, and a prototype
that carries to production in the same terse model.

## Method (stated in the receipt, not negotiated in comment threads)

- The plain twin implements the same observable behavior list as the demo app — endpoints,
  persistence, validation, and whatever slice the demo showcases (semantic search or a job) —
  built the way current stock guidance teaches: standard templates, no exotic minification, no
  deliberate golf. It is a competent developer's honest first version, not a strawman.
- Count hand-written application code: non-blank, non-comment lines in `.cs` files (plus any
  `.cshtml`/static assets the demo serves), excluding `obj/`, `bin/`, generated code, and
  framework-authored packages. Schema or migration code the plain twin must write counts; the
  schema Koan elects does not, because the application never writes it. Every exclusion is
  stated beside the table.
- Recorded under the same rules: files touched to first working endpoint, commands from empty
  directory to running app, and configuration keys required at first run.
- The counting command ships beside the apps and reproduces the table from a fresh checkout
  (ACCEPTANCE §2).

## Content

- `docs/case-studies/` (or beside the demo app): the plain twin, the counting script or
  command, and `LOC.md` with the table, the method, the exclusions, and the reproduction.
- The table carries both counts, links to both trees, and nothing else — no ratio adjectives,
  no superlatives (ACCEPTANCE §0).

## Evidence to read first

- [`../CHARTER.md`](../CHARTER.md) — positioning and claim 3's receipt type.
- [`../../../getting-started/quickstart.md`](../../../getting-started/quickstart.md).

## Acceptance criteria

- [ ] Both apps committed and runnable from documented commands.
- [ ] `LOC.md` states the method, lists every exclusion, and reproduces the table with one
      command from a fresh checkout.
- [ ] ACCEPTANCE §0 and §1 pass.

## Proof

`LOC.md` path and the reproduction command's output, linked from `PROGRESS.md`.
