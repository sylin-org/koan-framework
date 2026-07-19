---
type: SPEC
domain: framework
title: "R12-04 - Establish One Coherent Public Narrative"
audience: [architects, maintainers, developers, technical-writers, ai-agents]
status: current
last_updated: 2026-07-19
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-19
  status: pending
  scope: complete public-content inventory, greenfield narrative, executable guidance, and anti-drift gate
---

# R12-04 — Establish one coherent public narrative

- Tranche: `T7B — public product maturity`
- Status: `pending R12-01 through R12-03`
- Depends on: settled preview/version contract, preview-blocker dispositions, and generated maturity boundary
- Unlocks: public-only consumer proof and the first 0.20 preview wave
- Owner: every surface from which an external developer or coding agent learns what Koan is and how to use it

## Meaningful outcome

A person or coding agent can enter Koan through any public front door and encounter the same greenfield
product: the same purpose, first result, capability progression, package identity, composition model,
guarantees, corrective failures, maturity boundary, and production responsibilities.

No public reader must reconstruct the current framework by reconciling multiple generations of
initiatives, migrations, retired package names, superseded startup paths, or contradictory examples.

## Narrative contract

Every public surface tells this story at the depth appropriate to its audience:

1. **Purpose:** Koan lets .NET application code read as the business while referenced capabilities own
   composition, safe defaults, infrastructure negotiation, and explanation.
2. **Start:** install the 0.20 preview, call `AddKoan()`, define an `Entity<T>`, and reach one meaningful
   result.
3. **Grow:** add Web, persistence, Jobs, Communication, MCP, security, and optional AI/vector capability
   without rebuilding the application's architecture.
4. **Understand:** startup facts, health, HTTP facts, MCP facts, and the lockfile explain the same host
   decisions.
5. **Correct:** unsupported or unsatisfied guarantees fail with an actionable owner and correction.
6. **Operate:** applications own credentials, topology, durability, cost, provider commitments, and
   deliberate departures from the common path.
7. **Trust:** package availability, preview maturity, verified guarantees, experiments, and non-claims
   remain visibly distinct.

## Complete public-facing inventory

The task covers at least:

- root `README.md`, `llms.txt`, `CLAUDE.md`, and `CONTRIBUTING.md`;
- `docs/index.md`, `docs/toc.yml`, every TOC-linked page, and all pages linked as the current getting
  started, guide, reference, architecture, troubleshooting, and release path;
- `samples/README.md` and every active sample README, run instruction, launch profile, visible UI copy,
  and checked-in configuration used by the documented journey;
- every active package's NuGet description, tags, README, proportional technical companion, install
  instruction, reference effect, configuration, inspection path, correction, and limitation;
- template package documentation and the source content emitted by every public template;
- generated product-surface and package-quality projections plus their irreducible claims source;
- public tool/help text, workflow guidance, issue/feedback instructions, and release notes used by
  preview testers;
- skills, blueprints, or agent guidance presented as current Koan authoring instructions.

ADRs, initiatives, assessments, proposals, and archives remain dated repository evidence. They are not
rewritten as current curriculum and must not be linked as required ordinary-use instructions.

## Focused discovery and coalescence assessment

- **User's business sentence:** “Tell me one coherent Koan story regardless of where I enter.”
- **Smallest public expression:** one install path, one four-line host, one Entity result, and one
  progressive capability ladder.
- **Complete action surface:** reference, code, configuration, runtime prerequisite, inspection, failure,
  and removal are all stated where applicable; no hidden ceremony lives only in another generation's guide.
- **Guarantee and correction:** contradictory current guidance is a failing product defect naming both
  files and the canonical owner. Historical material is allowed only when unmistakably dated and outside
  the public learning graph.
- **Public concepts:** reuse the product constitution, Entity language, Reference = Intent, standard .NET
  hosting/options/health, generated maturity labels, and ordinary NuGet vocabulary. Add no documentation-only
  architecture taxonomy.
- **Current owner:** the public navigation graph plus package-owned presentation and generated product truth.
  `public-docs-lint.ps1` already enumerates much of this surface and should evolve rather than gain a rival.
- **Coalescence:** rewrite or delete duplicate present-tense guidance; link to one canonical explanation;
  retain package-local orientation where discovery requires it. Do not produce a second hand-maintained
  module catalogue or narrative ledger.
- **Ergonomics:** optimize time to meaningful result, number of concepts before business code, copy/paste
  correctness, corrective failure quality, and the reader's ability to predict what a reference changes.

## Work

1. Compile an exact public-content graph from navigation, package metadata, package companions, templates,
   samples, root/agent front doors, and current authoring assets.
2. Classify each item by audience and purpose: orient, learn, apply, operate, extend, troubleshoot, or
   evaluate maturity.
3. Record contradictions, duplicated decisions, initiative-era narration, stale generations, hidden
   prerequisites, and pages with no distinct reader outcome.
4. Establish the canonical story and terminology from the product constitution plus R12-01/R12-03 truth.
5. Rewrite the public path from the user journey inward; merge, redirect, demote, or delete competing
   current guidance.
6. Reconcile every package and sample surface with the same story at proportional depth.
7. Extend the existing public-doc truth gate so the complete inventory and critical narrative invariants
   fail automatically when they drift.
8. Run public-context-only cold reads with both people and coding agents; turn confusion into repository-owned
   anonymous corrections.

## Acceptance

1. Every public-facing file has an explicit audience/purpose or is removed from the public graph.
2. Every public entry point reaches the same first result, package identity, and capability progression.
3. Current prose contains no migration/campaign narration, retired mechanism, stale version generation,
   alternate bootstrap, or maturity claim stronger than generated evidence.
4. All examples include their complete references, code, configuration, context, and runtime prerequisites.
5. Package pages explain why to reference the package, what becomes automatic, what remains the
   application's responsibility, how to inspect it, and how failure corrects the user.
6. Historical evidence remains accessible but is never required to understand or operate current Koan.
7. Templates, active samples, package-only consumer proofs, snippets, links, generated pages, and strict
   public-doc checks pass from one exact candidate.
8. At least two independent public-context-only readers reproduce the intended story and identify no
   unresolved contradiction; their anonymous evidence and resulting corrections are recorded.
9. A newly added public-facing file cannot silently escape the inventory or introduce a competing current path.

## Stop conditions

- Stop prose work when the underlying behavior, package boundary, version, or maturity decision is unsettled.
- Stop if “greenfield” is used to erase required limitations, security responsibilities, or corrective failures.
- Stop if a mechanical vocabulary replacement changes meaning or presents lint as proof of narrative quality.
- Stop if the work edits dated ADR conclusions merely to make current marketing cleaner.
- Stop before public publication; R12-06 owns the exact external mutation and observation boundary.
