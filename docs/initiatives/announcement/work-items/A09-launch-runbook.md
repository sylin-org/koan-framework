---
type: GUIDE
domain: framework
title: "A09 - Launch runbook and rollout"
audience: [maintainers, ai-agents]
status: draft
last_updated: 2026-08-27
framework_version: v1.0.12
validation:
  date_last_tested: 2026-08-27
  status: reviewed
  scope: launch-runbook work-item specification
---

# A09 — Launch runbook and rollout

- Tranche: `T2 — Launch`
- Status: `draft`
- Depends on: A02, A03, A04, A06, A07, A08
- Unlocks: A10
- Owner: maintainer

## Meaningful outcome

The launch is executed from a written runbook rather than improvised: ordered rollout, comment
window staffing, metric capture against the charter baseline, and a retreat rule.

## Runbook contract

- **Order:** A08 surfaces live → Show HN + r/dotnet (day 0, staggered hours, never cross-posted)
  → A07 listings already in place → YouTube pitch emails (week 1).
- **Comment window:** the maintainer answers every top-level comment for 48 hours per channel;
  skeptical engagement gets evidence and thanks (ACCEPTANCE §4); defects found by newcomers enter
  the normal registers the same day.
- **Retreat rule:** if reception exposes a broken quickstart or first-run defect, the launch pauses
  — fix with ordinary evidence, then resume. A paused launch is recoverable; a launch that burned
  trust is not.
- **Metrics:** at day 0, 7, and 30 capture stars, forks, clones/views, referrers, NuGet velocity,
  and discussion volume — same sources as the baseline snapshot.

## Acceptance criteria

- [ ] Runbook text exists before any post goes out.
- [ ] Posts made per order; links recorded in `PROGRESS.md`.
- [ ] Day-0 and day-7 metric snapshots recorded against baseline.

## Proof

Runbook text, post URLs, and metric snapshots in `PROGRESS.md`.
