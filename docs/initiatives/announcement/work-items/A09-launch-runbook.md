---
type: GUIDE
domain: framework
title: "A09 - Launch runbook and rollout"
audience: [maintainers, ai-agents]
status: draft
last_updated: 2026-08-28
framework_version: v1.0.30
validation:
  date_last_tested: 2026-08-28
  status: reviewed
  scope: launch-runbook work-item specification
---

# A09 — Launch runbook and rollout

- Tranche: `T2 — Launch`
- Status: `draft`
- Depends on: A03, A04, A06, A07, A08
- Unlocks: A10
- Owner: maintainer

## Meaningful outcome

The launch is executed from a written runbook rather than improvised: staged waves with a
retreat point between each, a comment policy that fits a one-maintainer project, and metric
capture against the charter baseline using instruments the project's own automation cannot
inflate.

## Wave plan

- **Wave 0 — rehearsal (day −1).** A fresh container or machine, published feed only: run the
  quickstart, the `dotnet new install` flow, the README front door, and every A03/GIF link.
  Then flip the pre-launch switches: A08 surfaces live, A07 registrations in place, GitHub
  About + topics carrying the "Koan .NET framework" phrase, NuGet package READMEs current.
  *Exit: every step passed on the rehearsal machine, not the dev box.*
- **Wave 1 — soft launch.** The r/dotnet "I built this, tear it apart" post (A04's shape) and
  the article (Substack/dev.to). The article URL is the durable link everything later points
  at. *Exit: every landing-path defect found in comments fixed the same day it was reported.*
- **Wave 2 — Show HN.** Only after Wave 1 has held for 48 hours. The title carries the
  name-collision phrase; the author's first comment covers motivation, limits, the
  when-not-to-use link (A06), and the boilerplate-objection paragraph (A04).
- **Wave 3 — sustain.** YouTube pitches only if the demo GIF is done; participation visits to
  C#/.NET chat communities — answering questions where they are asked, never broadcasting.

Never cross-post the HN and Reddit texts. The highest-variance channel comes after the landing
path has survived friendlier traffic; a paused wave is recoverable, a burned audience is not.

## Comment policy (one maintainer)

Replies happen when the maintainer is available. No scheduled windows, no public SLA — launch
copy promises no response time. Two standing duties survive any schedule: skeptical engagement
gets evidence and thanks (ACCEPTANCE §4), and a newcomer-reported defect enters the normal
register the same day it is seen. A question answered twice becomes a documentation fix the
same week.

## Retreat rule

If reception exposes a broken quickstart or first-run defect, the wave pauses — fix with
ordinary evidence, then resume. The pause is announced plainly if the channel is still active.

## Metrics (contamination-resistant)

NuGet download velocity and clone counts are dominated by the project's own automation — the
2026-08-28 re-measure caught a +16k one-day family delta against a referrer table with a
single entry (release train, CI, eval restores). Capture instead, at day 0, 7, and 30:

- referrer diversity (baseline: 1 domain, github.com);
- non-maintainer Discussions topics and issues (baseline: 0);
- external repositories referencing `Sylin.Koan` in a csproj, via GitHub code search
  (baseline: 0);
- `Sylin.Koan.Templates` install delta read only on days with no eval runs (clean-day trend);
- quickstart completions reported back, in either direction.

Gross NuGet/clone numbers remain available to A10, subtractable for eval windows using the
eval campaign's own run records.

## Acceptance criteria

- [ ] Runbook text exists before any post goes out.
- [ ] Waves executed in order; post links recorded in `PROGRESS.md`.
- [ ] Day-0 and day-7 metric snapshots recorded against the baseline with the instruments
      above.

## Proof

Runbook text, post URLs, and metric snapshots in `PROGRESS.md`.
