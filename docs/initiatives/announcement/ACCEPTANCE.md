---
type: SPEC
domain: framework
title: "Announcement Initiative Acceptance Gate"
audience: [maintainers, ai-agents]
status: current
last_updated: 2026-08-28
framework_version: v1.0.30
validation:
  date_last_tested: 2026-08-28
  status: reviewed
  scope: initiative work-item acceptance criteria
---

# Announcement Initiative Acceptance Gate

Apply this gate to every work item. A card may add stricter criteria but may not waive
claim-provenance, honesty, or reproducibility requirements.

## Decision outcomes

- `PASS` — every applicable criterion has linked evidence; update the work item to `passed`.
- `BLOCK` — the outcome remains valuable, but a named dependency or decision prevents completion;
  record the blocker and a safe restart point.
- `STOP` — evidence invalidates the approach, the work duplicates another owner, or continuing
  would violate an invariant; preserve the learning and close the card as `stopped`.

## 0. Claim provenance — mandatory

- No quantitative claim publishes without tracing to a repository-owned measurement — the
  terseness receipt (A11), the recorded baseline, or a later published report. If the receipt
  does not exist, the claim does not exist.
- Superlatives ("fastest", "simplest", "orders of magnitude") publish only as the measured number
  with its reproduction instructions beside it.
- Criticism of other frameworks states verifiable facts (end-of-support dates, published
  positions) and never speculation about their motives or quality.

## 1. Honesty — mandatory

- Every published artifact links the "when not to use Koan" material (A06) or states its limits
  inline.
- Known gaps named in `docs/reference/what-works.md`, recipe `absent:` fields, and capability-map
  maturity states are not papered over in launch copy.
- Screenshots and demos record real runs against published packages; no mocked output.

## 2. Reproducibility — mandatory

- Any measurement that publishes must be rerunnable from a fresh checkout by documented
  commands alone.
- Run records (transcripts, logs, grader output, counting output) are retained beside the
  report.
- The agent-race benchmark campaign lives in maintainer-local notes
  (`local/initiatives/announcement-benchmark/`) and publishes only after a recorded operator
  decision re-charters it into this initiative.

## 3. Provenance and privacy — mandatory

- No private downstream name, path, identity, or identifying example appears in public artifacts.
- Private experience is a source of questions, not public proof.
- Destructive or externally visible actions (posting, registering listings) have a recorded
  operator decision before they happen.

## 4. Community safety

- Public comments follow `CODE_OF_CONDUCT.md`; skeptical engagement is answered with evidence and
  thanks, never dismissed.
- Defects and feature asks surfaced by newcomers enter the normal registers with their evidence,
  regardless of launch timing.
