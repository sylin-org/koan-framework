---
type: GUIDE
domain: framework
title: "A03 - Flagship demo artifact"
audience: [maintainers, ai-agents]
status: draft
last_updated: 2026-08-28
framework_version: v1.0.30
validation:
  date_last_tested: 2026-08-28
  status: reviewed
  scope: flagship demo work-item specification
---

# A03 — Flagship demo artifact

- Tranche: `T1 — Artifacts`
- Status: `draft`
- Depends on: none (the outcome's shape is defined by the validated recipes); feeds A04, A07, A11
- Unlocks: A07
- Owner: maintainer

## Meaningful outcome

One flagship recording, ≤5 minutes, that performs the charter's claims in order: the recipe-box
outcome (or an equivalent small outcome) built live — REST API from `Entity<T>` +
`EntityController<T>`; jobs added by package reference; `[Embedding]` semantic search; `[McpEntity]`
exposing the same model to an MCP client; closing on `/.well-known/Koan/facts` and
`koan.lock.json` as the anti-magic proof. A short GIF cutdown exists for README and posts.

## Why now

Every channel (Show HN, r/dotnet, YouTube pitches, listings) consumes this single artifact. The
facts-endpoint closing shot is mandatory: it is the one move no competing framework can mirror.

## Shape

- Script the shots before recording; no dead air, no debugging theater.
- Every command typed on screen is real; the run uses published packages only (ACCEPTANCE §1).
- The demo app is committed under `samples/` or `docs/case-studies/` so viewers can reproduce it.
- Caption and description may quote only the terseness receipt's LoC table (A11) with its
  reproduction link; no other numbers exist to publish.

## Evidence to read first

- [`../CHARTER.md`](../CHARTER.md) — claims and their receipt types.
- [`../../../getting-started/quickstart.md`](../../../getting-started/quickstart.md).

## Acceptance criteria

- [ ] Recording + GIF exist; total ≤5 minutes; every claim in it linked or inline-proven.
- [ ] Demo app committed and runnable from documented commands.
- [ ] Closing shot shows the facts endpoint and `koan.lock.json`.
- [ ] ACCEPTANCE §0 and §1 pass; A09's Wave-0 rehearsal passed on the recording machine.

## Proof

Recording and demo-app path recorded in `PROGRESS.md`.
