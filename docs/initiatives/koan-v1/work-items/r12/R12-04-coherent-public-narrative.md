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
  status: in-progress
  scope: complete public-content inventory, greenfield narrative, executable guidance, and anti-drift gate
---

# R12-04 — Establish one coherent public narrative

- Tranche: `T7B — public product maturity`
- Status: `in-progress`
- Depends on: settled preview/version contract, preview-blocker dispositions, and generated maturity boundary
- Unlocks: R12-05 exact local candidate freeze and certification
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

## Architecture checkpoint — one generated public learning graph

**Task:** Make the existing public-document truth gate enumerate, classify, and protect the complete
current public surface before rewriting that surface into the 0.20 preview narrative.

**Application intent:** A developer or coding agent should receive the same product regardless of
whether they enter through the repository, documentation site, NuGet package, template, sample,
agent skill, or feedback workflow. Maintainers should have one command that names every current public
asset and rejects a newly introduced competing path.

**Public expression:** One 0.20 preview story begins with an ordinary package/template install, the
four-line `AddKoan()` host, one useful `Entity<T>`, and a progressive reference-driven capability path.
The same surfaces explain runtime facts, corrective failures, application responsibilities, supported
claims, experiments, and nonclaims without asking readers to reconcile initiative generations.

**Guarantee/correction:** Every tracked public asset belongs to a derived origin/purpose class and is
checked for the shared narrative invariants appropriate to that class. A missing link, retired public
term, alternate bootstrap, stale release posture, unlisted graduated sample, unscanned template/skill,
or stronger-than-generated maturity claim fails with the exact path and canonical correction.

**Complete intent surface:** Root and agent front doors; TOC roots and transitive current local links;
package README/TECHNICAL companions and generated product truth; every tracked asset under the sample
directories linked by `samples/README.md`; both public template payloads; `.claude/skills`; issue and
pull-request templates; executable snippets; current feedback/release guidance; and explicit stops at
ADRs, initiatives, assessments, proposals, migrations, archives, private dogfood, shelved projects,
build output, and `tmp/`.

**Public concepts:** Reuse ordinary Markdown links, repository-tracked paths, documentation
frontmatter, NuGet package/version language, `dotnet new`, standard .NET hosting, and the generated
support maturity labels. Origin classes map to reader purposes—orient, start, learn, apply, operate,
extend, troubleshoot, evaluate, or contribute—without requiring per-file narrative metadata.

**Docs read:** Root `README.md`, `llms.txt`, `CLAUDE.md`, `CONTRIBUTING.md`; site index/TOC,
quickstart/golden path; sample and template indexes; generated product surface; the complete R12-04
contract; and the current agent-skill catalog. The focused stale-language scan also covered current
docs, templates, samples, and all Koan skills.

**Code read:** `public-docs-lint.ps1`, `docs-lint.ps1`, `docs-inventory.ps1`, `skills-lint.ps1`, the
green-ratchet call sites, tracked sample/template/skill path conventions, and the link/frontmatter
parsers already used by repository documentation tooling.

**Reusing:** Evolve `public-docs-lint.ps1` as the sole public-graph owner. Use `git ls-files` for the
tracked boundary, `docs/toc.yml` and normal Markdown links for curriculum reachability,
`samples/README.md` for graduated-sample admission, existing package companion conventions, template
directories, and `.claude/skills`; continue to compose with the separate structural docs, skills,
example, package-quality, and product-surface gates.

**Creating new:** Add a derived origin/purpose projection and transitive current-link traversal to the
existing script, plus bounded narrative invariants and a concise inventory summary. Store no generated
inventory file and add no hand-maintained content registry.

**Coalescence:** Retire `docs-inventory.ps1` as a competing whole-tree, write-to-disk inventory once
the new read-only public graph covers its useful discovery role. Keep package maturity in generated
product truth, sample admission in the sample index, site admission in the TOC/link graph, and skill
structure in the existing skills gate; the public gate composes those facts rather than duplicating them.

**Ergonomics:** A new public page becomes covered by linking it normally; a graduated sample becomes
covered by listing it once; package and template assets are covered by their existing layout. Failure
messages state the file, conflicting language, and correction. The ordinary successful output remains
short while an explicit inventory mode exposes paths and purposes for maintainers.

**Constraints satisfied:** One generalized chokepoint replaces spread; standard Git/Markdown/.NET
concepts come first; generated claims remain maturity authority; historical evidence is preserved but
not treated as curriculum; private dogfood, shelved projects, `tmp/`, remotes, and publication remain
outside scope; focused documentation/tool evidence replaces a full release ratchet.

**Risks:** The old 233-file count hides linked current pages, graduated sample implementation/config,
template source, skills, and feedback surfaces. The current front-door closure already reaches 372
current files and 46 historical evidence boundaries; graduated samples add 245 tracked text-like
assets, templates add 11 files, skills add 24, and feedback adds 3, with overlap. Blind recursive scans
would incorrectly publish historical, ungraduated, private, or shelved material, while blind term
replacement could corrupt intentional nonclaims such as “do not call `AddKoanMcp()`.” Classification
and exact-context corrections must precede broad rewriting.

## Implementation state — compiled graph and first greenfield pass

The existing public-doc gate now compiles one read-only graph from tracked paths. It follows the site TOC and
transitive Markdown links, admits a graduated sample directory once from `samples/README.md`, includes package
companions, template payloads, product truth, feedback surfaces, `.claude/skills`, and any future `.claude/agents`
guidance, and requires every nonhistorical Markdown file under `docs/` to declare either `current` or an evidence
status. Current orphan pages can no longer evade narrative checks merely by lacking an inbound link.

The current projection contains 667 current assets, 656 current text surfaces, 107 historical boundaries,
42 navigation targets, and 11 graduated sample roots. Current assets are derived into reader-purpose classes;
there is no generated inventory file or maintained content ledger.

The first coalescence pass:

- aligned root, site, LLM, contributor, template, package-entry, sample, and agent-skill front doors on the selective
  0.20 preview, canonical package IDs, the four-line host, Entity-first growth, runtime facts, and the generated
  maturity boundary;
- removed duplicate or retired current instruction surfaces for the old Web guide, OpenAPI duplicate, CLI/Aspire
  orchestration, general-object Messaging, Flow-era recipes, AI integration duplication, transaction draft, and
  internal endpoint-service/guard prose that had no distinct current reader outcome;
- retired 11 stale custom agent profiles in favor of the validated skill catalogue, and made future agent profiles
  enter the same graph automatically;
- retired `docs-inventory.ps1` and the hand-maintained adapter YAML/matrix, leaving the public graph and generated
  product surface as the two non-overlapping owners of discoverability and maturity;
- classified previously ambiguous plans, findings, dogfood research, and migration material as historical evidence
  without rewriting their conclusions as product truth;
- replaced removed Flow/Messaging teaching with the actual Entity lifecycle, Jobs, Communication, and contributor
  chokepoints, and changed Jobs tuning examples to standard `Configure<JobsOptions>`;
- regenerated all ten graduated sample locks from successful builds so promoted modules report 0.20 while
  intentionally unpromoted modules retain their real package lines.

## Focused evidence

- public documentation truth: 667 current assets / 656 current text surfaces / 107 historical boundaries /
  42 navigation targets / 11 graduated sample roots;
- packaging compiler tests: 16/16 for product-surface and package-quality generation;
- packaging tool: Release build, zero warnings and errors;
- generated truth: 29 claims / 93 packages and 93 structurally ready packages, byte-identical after regeneration;
- graduated samples: ten Release builds with zero warnings/errors; lock audit rejects both a promoted module below
  0.20 and an unpromoted module at 0.20;
- strict skills: 20 skills, zero errors/warnings;
- instructional examples: 20/20 compiled;
- broad documentation structure: zero errors / 1,377 non-gating warnings, improved from the 1,633-warning opening
  baseline after removing the obsolete lockstep-root-version assumption;
- whitespace validation: tracked and staged diffs clean.

R12-04 remains in progress. Two independent public-context-only comprehension reads and their anonymous evidence
are still required before closure. They must reproduce the intended story and identify contradictions, but they do
not claim that unpublished install commands execute. Exact local package behavior belongs to R12-05; genuine
public-feed installation and independent execution belong to R12-06.

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
8. Run comprehension-only public-context cold reads with both people and coding agents; turn confusion into
   repository-owned anonymous corrections without presenting unpublished installation as executable public proof.

## Acceptance

1. Every public-facing file has an explicit audience/purpose or is removed from the public graph.
2. Every public entry point reaches the same first result, package identity, and capability progression.
3. Current prose contains no migration/campaign narration, retired mechanism, stale version generation,
   alternate bootstrap, or maturity claim stronger than generated evidence.
4. All examples include their complete references, code, configuration, context, and runtime prerequisites.
5. Package pages explain why to reference the package, what becomes automatic, what remains the
   application's responsibility, how to inspect it, and how failure corrects the user.
6. Historical evidence remains accessible but is never required to understand or operate current Koan.
7. Templates, active samples, snippets, links, generated pages, and strict public-doc checks pass from source;
   R12-05 owns their exact package-only candidate proof.
8. At least two independent public-context-only readers reproduce the intended story, accurately distinguish
   candidate readiness from public availability, and identify no unresolved contradiction; their anonymous
   evidence and resulting corrections are recorded.
9. A newly added public-facing file cannot silently escape the inventory or introduce a competing current path.

## Stop conditions

- Stop prose work when the underlying behavior, package boundary, version, or maturity decision is unsettled.
- Stop if “greenfield” is used to erase required limitations, security responsibilities, or corrective failures.
- Stop if a mechanical vocabulary replacement changes meaning or presents lint as proof of narrative quality.
- Stop if the work edits dated ADR conclusions merely to make current marketing cleaner.
- Stop before public publication; R12-06 owns the exact external mutation and observation boundary.
