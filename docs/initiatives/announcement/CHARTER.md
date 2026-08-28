---
type: ARCHITECTURE
domain: framework
title: "Announcement Initiative Charter"
audience: [maintainers, ai-agents]
status: current
last_updated: 2026-08-27
framework_version: v1.0.12
validation:
  date_last_tested: 2026-08-27
  status: reviewed
  scope: announcement mission, claims, baseline, and session protocol
---

# Announcement Initiative Charter

Read this file in full before claiming any initiative work item. A session may have no access to
the conversation that created this initiative; this charter is the portable contract.

## Mission

Take Koan 1.0 to the public with receipt-backed claims, and convert the pre-announcement quiet
footprint into an active, measurable community. Nothing publishes before its receipt exists; no
claim is made that a repository-owned artifact cannot prove.

Koan is free and open under Apache-2.0. This initiative spends time and artifacts, never money,
and never changes that posture.

## Product thesis — three claims, three receipt types

1. **Composition delight.** "Entity. Controller. Done." — a package reference is the intent, and
   the application keeps saying its business nouns.
   *Receipt: the runnable quickstart and the flagship demo artifact (A03).*
2. **One model, every surface.** The same `Entity<T>` serves HTTP, jobs, events, and MCP clients
   under the same access rules. There is no second agent domain model; advertisement is
   enforcement.
   *Receipt: `docs/capabilities/agents.md` and its validated leaves; demonstrated in A03.*
3. **Agent-amplified development.** A semantically expressive Koan application is materially
   faster and cheaper for a coding agent to produce and verify than the equivalent plain
   ASP.NET Core application: fewer degrees of freedom, exact identifiers, machine-readable
   composition facts.
   *Receipt: the agent-race benchmark report (A02) and nothing else. No magnitude claim — no
   "orders of magnitude", no multiplier — may publish until A02 measures it, and the published
   number is the measured number.*

## Target audiences, ranked

1. **The solo or small-team .NET developer** shipping a SaaS or internal tool — the Rails/Laravel
   audience inside .NET.
2. **The AI-forward .NET developer** adding semantic search, embeddings, or agents to a real
   application without leaving C#.
3. **MCP and agent-tool builders** — a fast-growing audience with almost no .NET-native answer.
4. **Coding agents and the people who direct them** — Koan's docs, skills, retrieval maps, and
   facts endpoints are agent-first surface; every agent user is an adoption channel.

Not yet targeted: enterprise architecture teams seeking commercial modules and support SLAs.
Positioning against that buyer wastes the launch and invites comparison Koan should not accept yet.

## Recorded baseline (2026-08-27, pre-announcement)

Captured from live sources on the initiative's opening day; A10 measures against it.

| Signal | Value | Note |
|---|---|---|
| Repository age | public since 2025-08-18 | first NuGet publish 2025-09-16 |
| Stars / forks / watchers | 4 / 3 / 0 | all from distinct external accounts |
| 14-day traffic | 18 views / 7 unique · 887 clones / 196 unique | ~90 CI runs in window; clone-to-view inversion suggests automated consumption |
| Referrers | github.com only | zero inbound links from the open web |
| NuGet family | 201 packages · 181,402 downloads | transitive fan-out inflates totals |
| `Sylin.Koan` bundle | 2,042 downloads | the honest proxy for distinct app restorations |
| `Sylin.Koan.Templates` | 1,191 downloads | `dotnet new install` onboarding runs |
| Social footprint | none | zero HN/Reddit/dev.to/Medium discussion |

## Environment

- Classic ASP.NET Boilerplate reached end-of-support in May 2026; ABP vNext is widely criticized
  as overweight for small and mid-size applications. A displaced audience exists and is unowned.
- MCP is the de facto agent-to-tool standard; production concerns (governance, bounded exposure)
  are the live discussion. Koan's advertisement-is-enforcement and `$koan-explain` answer them.
- Koan 1.x targets .NET 10 (LTS).
- Known hazard: the name "Koan" collides with DotNetKoans and koan-style tutorials in search.
  Published artifacts must carry the phrase "Koan .NET framework" and a direct link; discovery
  cannot rely on search engines alone.

## Constraints

- Zero budget: no paid placement, no sponsorships, no paid tooling.
- Owner capacity is one maintainer plus coding agents; every work item must be executable in
  bounded sessions and produce artifacts that outlive the session.
- Every public claim links to repository-owned code, docs, samples, or the A02 report.
- Launch copy promises no features and no dates. Feedback and defects route to the normal
  registers (`docs/MEMORY.md`, work-item conventions), never to launch commitments.

## Non-goals

- No paid marketing of any kind.
- No enterprise sales motion, commercial edition, or support-tier discussion.
- No framework changes driven by launch optics; correctness findings discovered by newcomers are
  ordinary defects with ordinary evidence.
- No community-size vanity targets; success is measured against the recorded baseline.

## Session protocol

Read, in order: this charter; [`ROADMAP.md`](ROADMAP.md) for dependency order;
[`PROGRESS.md`](PROGRESS.md) for live status; [`NOW.md`](NOW.md) for the current handoff; then the
claimed work item. Update `PROGRESS.md` in the same change that starts, blocks, or completes a
work item.
