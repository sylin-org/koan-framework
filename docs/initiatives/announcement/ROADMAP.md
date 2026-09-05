---
type: ARCHITECTURE
domain: framework
title: "Announcement Initiative Roadmap"
audience: [maintainers, ai-agents]
status: current
last_updated: 2026-09-05
framework_version: v1.0.30
validation:
  date_last_tested: 2026-08-28
  status: reviewed
  scope: tranche dependencies and exit criteria
---

# Announcement Initiative Roadmap

This file defines dependency order and exit criteria. It intentionally does not track live status;
[`PROGRESS.md`](PROGRESS.md) is the only status ledger.

## Dependency graph

```text
T1 Artifacts (drafting may overlap; publication gated on A09's Wave-0 rehearsal)
  A05 ABP migration doc ─┐
  A06 when-not-to-use ───┤ (independent)
  A03 flagship demo ─────┤
  A11 terseness receipt ─┤ (needs A03's demo app)
  A04 announcement copy ─┘ (needs A11's table + A06's link)
T2 Launch
  A07 distribution registrations (needs A03)
  A08 community surfaces
      -> A09 launch runbook (needs A03, A04, A06, A07, A08)
T3 Sustain
      -> A10 retro + archive (needs A09 + 30 days elapsed)
```

The standing rule of this initiative: **the receipt gates the claim.** No public artifact ships
before the receipt for every claim it makes exists in the repository, and every quantitative
statement in a published artifact traces to its receipt. Today's receipts are the quickstart,
the flagship demo (A03), and the terseness receipt (A11). Performance and agent-productivity
claims have no publishable receipt — the measuring campaign lives in maintainer-local notes
(`local/initiatives/announcement-benchmark/`) and in `evals/agent-race/`, and returns to this
initiative only by a recorded operator decision that re-charters it.

Feedback may move from later tranches to earlier ones. A later work item cannot declare an
earlier exit gate satisfied without updating the earlier artifact and evidence.

## T1 — Artifacts

**Outcome:** every launch asset exists and every claim in it links to a repository-owned receipt.

**Exit gate:**

- flagship demo (A03) records the approval-desk outcome end to end, including the facts endpoint;
- terseness receipt (A11) carries the LoC table, the stated counting method, and a one-command
  reproduction from a fresh checkout;
- copy pack (A04) quotes only A11's table where numbers are needed, carries the
  boilerplate-objection rebuttal, and links a "when not to use Koan" page (A06);
- ABP migration doc (A05) is honest about gaps and maps concepts without disparagement;
- no artifact contains an unlinked superlative.

## T2 — Launch

**Outcome:** staged-wave rollout — soft launch on r/dotnet + the article, then Show HN once the
landing path has held — with community surfaces live and registrations in place before the
first post.

**Exit gate:**

- GitHub Discussions and updated `SUPPORT.md` are live (A08) before any post;
- A09 runbook executed: waves in order, comment policy honored as written, metrics captured
  against the charter baseline with the contamination-resistant instruments.

## T3 — Sustain

**Outcome:** a 30-day retrospective against the recorded baseline, with an explicit archive or
continue decision.

**Exit gate:**

- A10 report compares the baseline's signals — read through A09's instruments, with
  eval-window self-traffic subtracted where gross NuGet/clone numbers are quoted — against
  [CHARTER.md](CHARTER.md)'s baseline table;
- the receipt verdict covers A11: did the LoC table survive public scrutiny, and would the
  method be run the same way again;
- lessons are written where the repository keeps them (`docs/MEMORY.md`);
- the initiative is archived or re-chartered — it does not linger as a standing banner.
