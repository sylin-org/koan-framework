---
type: GUIDE
domain: framework
title: "Announcement Initiative"
audience: [maintainers, ai-agents]
status: current
last_updated: 2026-09-05
framework_version: v1.0.30
validation:
  date_last_tested: 2026-08-28
  status: reviewed
  scope: initiative index and links
---

# Announcement Initiative

Take Koan 1.0 public with receipt-backed claims. Nothing publishes before its receipt exists;
every claim links to a repository-owned artifact; the campaign spends time, not money.

## Read in this order

1. [CHARTER.md](CHARTER.md) — mission, positioning, the three claims and their receipt types,
   the held hypothesis, ranked audiences, the recorded pre-announcement baseline, constraints,
   non-goals.
2. [ROADMAP.md](ROADMAP.md) — tranche dependencies and exit criteria.
3. [ACCEPTANCE.md](ACCEPTANCE.md) — the gate every work item passes or fails.
4. [PROGRESS.md](PROGRESS.md) — the only live status ledger.
5. [NOW.md](NOW.md) — current session handoff.
6. [`work-items/`](work-items/) — bounded, self-contained cards (A03–A11).

## The standing rule

**The receipt gates the claim.** Launch copy quotes only numbers whose receipts live in the
repository — today that is the terseness receipt (A11) and the recorded baseline. Performance
and agent-productivity claims have no publishable receipt: the measuring campaign was moved to
maintainer-local notes (`local/initiatives/announcement-benchmark/`) by operator decision on
2026-08-28 and continues in-tree under `evals/agent-race/`; it returns here only by a recorded
operator decision that re-charters it.

## Tranches

| Tranche | Outcome | Cards |
|---|---|---|
| T1 Artifacts | every launch asset exists with linked claims | A03, A04, A05, A06, A11 |
| T2 Launch | live community surfaces, registrations, staged-wave rollout | A07, A08, A09 |
| T3 Sustain | 30-day retro against baseline; archive or re-charter | A10 |

## Shared application work

[Application evolution](../application-evolution/README.md) uses A03's approval desk as its
first consumer. A03 owns the flagship application and recording; AE-01 owns the shared
foundation and second consumer. Record one canonical application path and pin A11's receipt
revision. The evolution experiments, independent pilots, and Aspire investigation add no
launch prerequisites; only demonstrated findings enter announcement copy under this
initiative's existing acceptance contract.
