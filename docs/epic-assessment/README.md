# The Sylin Stack — Epic Assessment (Koi · Zen Garden · Koan)

**Date**: 2026-06-11 · **Scope**: the three-project stack as a whole — not any single repo ·
**Status**: point-in-time analysis, designed to be moved out of this repo (self-contained; no
links into the host repo's other docs).

## What this is

Each of the three sibling projects received its own staged, multi-agent maturity assessment in
June 2026:

| Project | What it is | Assessment location | Verdict (own assessment) |
|---|---|---|---|
| **Koi** | Rust; one-binary LAN substrate: mDNS discovery, local DNS, zero-config PKI ("certmesh"), TLS proxy, health | `koi/docs/assessment/` | Late feasibility / early alpha; "what is easy to test is exquisitely tested; what is risky has plausibly never been run" |
| **Zen Garden** | Rust; fleet orchestrator for scavenged heterogeneous hardware (incl. an Android stone): offerings lifecycle, pond mTLS, MongoDB replica choreography, VRAM-aware AI placement | `zen-garden/docs/notes/assessment-2026-06/` | L1 feasibility prototype with L2–L3 internals and L0 release engineering |
| **Koan** | .NET 10 meta-framework; entity-first, Reference=Intent, capability-graded multi-provider, agent-native thesis, MCP surface | `koan-framework/docs/assessment/` | L2 system with L3 islands and an L1 public face |

This document set evaluates the **Epic** — the claim that the three compose into a vertically
integrated stack: *Koi provides trust and discovery, Zen Garden provides fleet orchestration
and hardware reclamation, Koan provides the application framework — a sovereign stack for the
small senior team (plus agents) building on hardware they own, on a network they control.*

## The mission frame

These are open-source projects with a social mission, and the strategy below is read through
that lens (maintainer directive, 2026-06-11):

- **Capacitation.** The point is to enable individuals and small teams to partake in
  capabilities and workloads usually denied to them — self-hosting, compute sovereignty,
  production-grade infrastructure without an enterprise budget, an IT department, or a cloud
  account. Capability gaps, not market share, are the scoreboard.
- **Enablers, not competitors.** The stack exists to feed and complete the tools people
  already run, not to replace them. Koi's assessment already states the doctrine — *export in
  their formats, never require import in ours; consume what users already wrote; be the
  substrate, not the surface; every capability needs an exit; degrade gracefully when a layer
  is owned* — and this analysis elevates it from a Koi tactic to Epic-wide canon
  ([03 §0.0](03-strategic-opportunities.md)).

Two practical consequences run through everything that follows. First, every opportunity is
graded by *who it enables* before *who it displaces* — "competitor" analysis below is about
locating unserved people, not about winning territory. Second, the truth-first findings are
mission findings, not just engineering findings: the audiences being capacitated (first-time
builders, laptop revivers, small teams without IT) are precisely the people most harmed by
fictional front doors and silent trust gaps. For this mission, honesty is not a nicety — it
is the product.

## Method

- **Inputs**: the three per-project assessment corpora above (read in full), plus **fresh
  code-level verification of every cross-project coupling** — this analysis did not take the
  assessments' word for the seams; each was re-derived from source with file:line evidence.
- **Process**: 6 parallel evidence readers (3 corpus, 3 code-verification) → 3 independent
  strategy lenses (market/landscape, systems-architecture, red-team skeptic) over the combined
  fact pack → synthesis. A 4-agent adversarial review of the finished documents was launched
  and cut short by an external quota; in its place, every load-bearing citation was
  spot-re-verified inline by the lead: the five Koan mainline csproj references, ZG's five
  `../koi` path deps (Cargo.toml:98-102), moss's `with_no_client_auth` (tls.rs:101), Koi's
  consumer vocabulary (roster.rs:62) and frozen HKDF constants (unlock_slots.rs:41,551), the
  hardcoded Koi endpoints in Koan (Constants.cs:51-52), zen-garden's zero tags and both
  remotes' staleness dates, and the full commit-cadence math (re-run against all three git
  histories on 2026-06-11, including Koan's 192-day gap). All checked claims held.
- **Evidence convention**: citations are `repo-name/path:line`. All quantitative claims trace
  to either a per-project assessment (itself adversarially verified) or a direct code/git check
  performed for this analysis.

## The one-paragraph verdict

**The Epic is real as plumbing and fictional as trust — and trust is the word doing the
marketing work.** The dependency gradient the trilogy story assumes (Koi ← Zen Garden ← Koan)
is physically wired today: Zen Garden embeds Koi in-process and delegates its entire CA to
certmesh; Koan's mainline Mongo/Weaviate/Ollama/S3 connectors hard-reference ZenGarden
contracts and a Koan-side handler consumes Koi's mDNS-over-HTTP bridge. The discovery column
works end-to-end. But the trust column — the stack's namesake — is broken at every layer
simultaneously (Koi's TLS proxy has plausibly never worked, revocation doesn't revoke, private
keys ship over the wire, Moss serves without client auth, Zen Garden exposes unauthenticated
root code-push, and Koan contains zero certificate-handling code), every seam is private
(sibling path deps, in-tree contracts, hardcoded endpoints — versioned nowhere), and the whole
estate matures through **one strictly serialized attention stream** (verified: Koan's only
dormancy, 192 days, is exactly Zen+Koi's construction window). The stack-level work is
therefore not construction: publish the seams as versioned contracts, make the trust column
honest, mount one end-to-end integration proof — and let the vertical emerge from working
wedges instead of marketing the totality.

## Documents

| Doc | Contents |
|---|---|
| [01-stack-anatomy.md](01-stack-anatomy.md) | The layer model; the verified interlock ledger (every coupling, with status: real / half-real / broken / aspirational); the revealed architecture; the serialized-attention finding |
| [02-synergy-audit.md](02-synergy-audit.md) | What works (verified), what doesn't (verified), and the shared failure-mode analysis — why all three repos break in the same four ways, and what that implies for an Epic |
| [03-strategic-opportunities.md](03-strategic-opportunities.md) | The enabler doctrine (Epic-wide canon); stack-level opportunities ranked by uniqueness × feasibility; the five cross-project conflicts that must be resolved first; refused lanes at stack level |
| [04-architecture-alignment.md](04-architecture-alignment.md) | The target seam architecture: layering law, contract-type matrix, fixes for the three wrong couplings, discovery doctrine, trust-fabric layering, two shared substrates |
| [05-leverage-plan.md](05-leverage-plan.md) | The minimal truth set; sequencing for one maintainer; the Win10-ESU go/no-go rule; mapping onto the three repos' existing prompt stashes; operating rules |

## Relationship to the per-project assessments

This analysis **adds only the stack layer**. It does not restate per-project findings except
where they are load-bearing for a seam, and it does not re-issue per-project work: each repo
already has an operationalized prompt stash (Koan `docs/assessment/06+07`, Koi
`docs/prompts/P01–P13`, Zen Garden `.agentic/prompts/` ×16). The leverage plan maps stack work
onto those stashes and identifies the small set of genuinely new cross-repo artifacts (one
ADR, four seam contracts, one integration demo).
