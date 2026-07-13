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
  scope: initial R00 handoff
---

# Koan V1 Reorganization Current Handoff

Replace this file at every handoff. It is a restart point, not a diary.

## Active work

- Work item: [R00 — Establish the privacy boundary](work-items/R00-privacy-boundary.md)
- State: `in-progress`
- Objective: make the public repository independent of every private downstream identity while
  preserving anonymous, reproducible learning.
- Current fact: tracked content and paths are clean, and 53,203 historical objects contain no
  identifying paths.
- Rewrite fact: identifying content remains reachable in Git history because the sanitized line has a
  reachable predecessor. Two bounded full-content scans did not complete, so the total extent remains
  inconclusive.
- Authorization: on 2026-07-13, the operator explicitly authorized rewriting affected published
  history and force-pushing affected refs.

## Next safe actions

1. Commit the reviewed initiative state and create a recoverable bundle outside the repository.
2. Clone an isolated mirror of the published repository and record branch/tag object IDs.
3. Rewrite the private term set without emitting terms or matching content.
4. Verify current files, reachable paths, reachable object content, and changed refs.
5. Force-push only changed branch/tag refs, then independently clone and verify the public result.
6. Pass R00 and replace this file to start R01.

## Expected working tree

The initiative bootstrap may add `docs/initiatives/`, update `docs/toc.yml`, and sanitize a generic
privacy note in the architecture redesign ledger. Treat every other pre-existing change as user-owned.

## Verification at handoff

- documentation metadata and links pass the repository documentation linter;
- the documentation table of contents resolves;
- current tracked files contain no operator-supplied identifying terms;
- `git diff --check` passes;
- no private term, application name, path, domain detail, or identifying example appears in the diff.

## Do not infer

- Do not infer that exposure is limited to the known predecessor.
- Do not infer that private downstream success is a supported public capability.
- Do not infer permission to rewrite history, publish identity, or broaden the initiative into runtime
  changes.
