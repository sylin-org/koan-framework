---
type: ARCHITECTURE
domain: framework
title: "Announcement Initiative Roadmap"
audience: [maintainers, ai-agents]
status: current
last_updated: 2026-08-27
framework_version: v1.0.12
validation:
  date_last_tested: 2026-08-27
  status: reviewed
  scope: tranche dependencies and exit criteria
---

# Announcement Initiative Roadmap

This file defines dependency order and exit criteria. It intentionally does not track live status;
[`PROGRESS.md`](PROGRESS.md) is the only status ledger.

## Dependency graph

```text
T0 Receipts
  A01 agent-race harness -> A02 execution + receipt report
T1 Artifacts (drafting may overlap A02; publication is gated on A02)
  A05 ABP migration doc ─┐
  A06 when-not-to-use ───┤ (independent)
  A03 flagship demo ─────┤ (needs A01's task shape)
  A04 announcement copy ─┘ (needs A02's measured number)
T2 Launch
  A07 distribution registrations (needs A03)
  A08 community surfaces
      -> A09 launch runbook (needs A02, A03, A04, A06, A07, A08)
T3 Sustain
      -> A10 retro + archive (needs A09 + 30 days elapsed)
```

The standing rule of this initiative: **the receipt gates the claim.** No public artifact ships —
not a post, not a listing, not a demo caption — before A02 closes, and every quantitative statement
in a published artifact traces to the A02 report.

Feedback may move from later tranches to earlier ones. A later work item cannot declare an earlier
exit gate satisfied without updating the earlier artifact and evidence.

## T0 — Receipts

**Outcome:** a reproducible, third-party-runnable measurement of how fast coding agents produce a
fixed outcome on Koan versus plain ASP.NET Core, published with raw run records.

**Exit gate:**

- `evals/agent-race/` contains the task contract, identical prompts, seed corpus, framework-agnostic
  grader, and run records for at least five runs per arm;
- a fresh checkout can rerun the benchmark from documented commands;
- `REPORT.md` states measured medians only, and names every threat to validity it could not remove.

## T1 — Artifacts

**Outcome:** every launch asset exists and every claim in it links to a repository-owned receipt.

**Exit gate:**

- flagship demo (A03) records the recipe-box outcome end to end, including the facts endpoint;
- copy pack (A04) carries the measured number, the boilerplate-objection rebuttal, and a
  "when not to use Koan" link (A06);
- ABP migration doc (A05) is honest about gaps and maps concepts without disparagement;
- no artifact contains an unlinked superlative.

## T2 — Launch

**Outcome:** coordinated rollout across Show HN, r/dotnet, distribution listings, and direct
pitches, with community surfaces live before the first post.

**Exit gate:**

- GitHub Discussions and updated `SUPPORT.md` are live (A08) before any post;
- A09 runbook executed: posts made, comment window staffed, metrics captured against baseline.

## T3 — Sustain

**Outcome:** a 30-day retrospective against the recorded baseline, with an explicit archive or
continue decision.

**Exit gate:**

- A10 report compares stars, clones, NuGet velocity, and inbound referrers against
  [CHARTER.md](CHARTER.md)'s baseline table;
- lessons are written where the repository keeps them (`docs/MEMORY.md`);
- the initiative is archived or re-chartered — it does not linger as a standing banner.
