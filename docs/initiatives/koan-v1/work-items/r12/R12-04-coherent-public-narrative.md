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

The current projection contains 688 current assets, 676 current text surfaces, 107 historical boundaries,
42 navigation targets, and 12 graduated sample roots. Current assets are derived into reader-purpose classes;
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
- regenerated all eleven graduated sample locks from successful builds so promoted modules report 0.20 while
  intentionally unpromoted modules retain their real package lines.

## Focused evidence

- public documentation truth: 688 current assets / 676 current text surfaces / 107 historical boundaries /
  42 navigation targets / 12 graduated sample roots;
- packaging compiler tests: 17/17 for product-surface and package-quality generation;
- packaging tool: Release build, zero warnings and errors;
- generated truth: 29 claims / 93 packages and 93 structurally ready packages, byte-identical after regeneration;
- graduated samples: eleven Release builds with zero warnings/errors; lock audit rejects both a promoted module below
  0.20 and an unpromoted module at 0.20;
- strict skills: 20 skills, zero errors/warnings;
- instructional examples: 20/20 compiled;
- broad documentation structure: zero errors / 1,375 non-gating warnings, improved from the 1,633-warning opening
  baseline after removing the obsolete lockstep-root-version assumption;
- whitespace validation: tracked and staged diffs clean.

R12-04 remains in progress. The maintainer is the sole validation authority for this cycle. The completed
independent coding-agent read supplied adversarial cold-read evidence, and the maintainer accepted or rejected every
finding. No second agent or human review is a completion gate. Exact local package behavior belongs to R12-05;
genuine public-feed installation belongs to R12-06.

## Cold-read correction checkpoint — 2026-07-19

**Task:** Reconcile the first independent coding-agent read with current source and remove every confirmed public
contradiction before R12-04 closes.

**Application intent:** A new reader should know what can be run today, what becomes the package-first path after
publication, what each maturity label guarantees, and where to inspect or correct a host without guessing across
generations.

**Public expression:** While NuGet publication is pending, the source-built FirstUse command is the executable first
action and the template command is explicitly the post-publication canonical entry. The four-line host, Entity
grammar, and capability references do not change.

**Guarantee/correction:** Current commands must be executable in their stated availability state; generated product
truth must define every accepted maturity; troubleshooting must name only current APIs, routes, and configuration;
security text must separate framework primitives from application policy/deployment; inspection routes must be exact.
Any future drift fails at the public-doc or generated-product owner with the conflicting path and correction.

**Complete intent surface:** Reorder pre-public first-use instructions; define maturity dispositions in generated
truth; rebuild the troubleshooting hub around startup/facts/health and current pillar references; clarify security
responsibility, aggregate capability inspection, Jobs discovery, Communication reach, and the LLM front door; fix
the README template/sample mismatch; then run the documented sample commands for maintainer validation. No package
installation or additional reviewer recruitment is part of R12-04.

**Public concepts:** Reuse standard source checkout, NuGet availability, package reference, configuration, health,
and HTTP vocabulary plus existing Entity, Reference = Intent, facts, lockfile, and generated maturity labels. Add no
new Koan concept.

**Docs read:** the reader packet, product constitution, Entity semantics contract, root/index/quickstart/template
front doors, product surface, troubleshooting, Jobs, Communication, composition lockfile, FirstUse, and
GoldenJourney.

**Code read:** product-surface compiler/constants/tests; current Web facts/aggregate/auth controllers and route
constants; Data default-provider plan and corrective strings; Jobs discovery marker/registry; Core environment and
health contracts.

**Reusing:** The product-surface generator owns maturity vocabulary; current pillar references own detailed
troubleshooting; Web route constants/controllers own inspection truth; public-doc lint owns narrative invariants.

**Creating new:** Add only a generated maturity-definition table and its focused assertion. No runtime type,
endpoint, option, service, sample, or documentation ledger is required.

**Coalescence:** Rebuild stale troubleshooting prose instead of preserving removed Scheduling examples. Keep the
real auth-discovery, KoanEnv, and health seams. Correct the aggregate path to its actual distinct capability endpoint
rather than falsely collapsing it into facts. Fold reader findings into this work card; create no second review log.

**Ergonomics:** One runnable command leads today, one package command leads after publication, maturity labels answer
“what may I rely on?”, and diagnosis begins with startup → health → facts → owning pillar. Security language maps
framework mechanism to application policy without suggesting that applications rebuild supported capabilities.

**Constraints satisfied:** standard .NET/Git/NuGet/HTTP concepts first; one generated maturity owner; no runtime API,
inline endpoint, data path, magic configuration, private dogfood, remote mutation, or full ratchet; maintainer-only
human acceptance is explicit.

**Risks:** The first reader overclassified several live APIs as stale; every removal must remain source-verified.
Maturity is a set of evidence/support dispositions, not a total ranking. Port examples retain the printed-URL rule;
changing all ports to placeholders would add ceremony without correcting current behavior.

### First coding-agent finding disposition

| Finding | Disposition |
|---|---|
| headline install is unavailable; README mixes Todo template with Approval calls | accepted; lead with source today and keep template as explicit post-public path |
| maturity terms undefined and `verified` omitted from the preview summary | accepted; generate definitions for the complete canonical vocabulary |
| troubleshooting is wholly stale | narrowed; remove retired Scheduling/default-key prose, retain source-confirmed auth, environment, and health seams |
| security responsibility appears contradictory | accepted; distinguish framework primitives from application policy and deployment responsibility |
| aggregate capability endpoint conflicts with facts | accepted as an exact-route defect; use `/.well-known/Koan/aggregates` and explain its distinct purpose |
| Jobs discovery attribute appears application-authored | accepted as clarity; `[KoanDiscoverable]` belongs to `IKoanJob`, not each job type |
| Communication “mesh” overstates RabbitMQ | accepted; connectors extend Transport reach, not local Events |
| `llms.txt` is undiscoverable | accepted; link the agent front door from the root README |
| product constitution/entity semantics were absent from the reader packet | protocol defect, not document defect; include both in subsequent reads |
| fixed port examples are contradictory | rejected; the public text already makes the printed URL authoritative and the ordinary no-profile host uses the shown default |

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
8. Use the completed coding-agent cold read as evidence, turn accepted confusion into repository-owned corrections,
   and leave final acceptance solely to the maintainer without presenting unpublished installation as executable
   public proof.

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
8. The independent cold-read findings have explicit maintainer dispositions, accepted defects are corrected, and
   the maintainer identifies no unresolved contradiction. No second agent or human review is required.
9. A newly added public-facing file cannot silently escape the inventory or introduce a competing current path.

## Stop conditions

- Stop prose work when the underlying behavior, package boundary, version, or maturity decision is unsettled.
- Stop if “greenfield” is used to erase required limitations, security responsibilities, or corrective failures.
- Stop if a mechanical vocabulary replacement changes meaning or presents lint as proof of narrative quality.
- Stop if the work edits dated ADR conclusions merely to make current marketing cleaner.
- Stop before public publication; R12-06 owns the exact external mutation and observation boundary.

## Graduated sample start-command checkpoint — 2026-07-19

**Application intent:** A maintainer runs any graduated sample from the repository root with its one documented
`dotnet run --project ...` command and reaches the named local result without hidden infrastructure.

**Complete expression:** One console command for each of the ten executable sample projects; only TaskGraph and the
two GardenCoop chapters add their documented standard ASP.NET Core `--urls` argument. Optional Docker services remain
optional and no Koan runner or wrapper script exists.

**Guarantee/correction:** The console sample exits with its result. Every Web sample starts on its documented port,
serves liveness and runtime facts, and reaches its sample-specific entry surface. A command that loses its project
content root, ignores its settings/assets, or activates an optional dependency is a sample-owned failure.

**Coalescence:** Keep ordinary `dotnet run` as the single start owner. The GardenCoop projects retain their existing
plain-SDK/explicit-ASP.NET shape because Chapter 1 owns a measured NativeAOT deployment; switching it to the Web SDK
changes native compilation behavior. Both chapters and the LocalChecklist console project declare the standard
MSBuild `RunWorkingDirectory` so settings, assets, relative local data, and generated lockfiles resolve against each
sample rather than the caller's directory. Add no launcher, path calculation, environment variable, or Koan convention.

**Ergonomics:** One copyable command works from the repository root, local state stays under the sample directory,
and the printed URL remains authoritative. The maintainer is the only acceptance authority.

**Focused evidence:** All eleven graduated commands were executed from the repository root. LocalChecklist reaches its
deterministic console result and reports `lockfile ok`. The ten Web hosts start on their documented ports, return
HTTP 200 from liveness and runtime facts, and reach their named root/API surface. Chapter 2 additionally activates
the bundled ONNX provider and returns semantic search successfully. AnimeRecommendations starts on port 5094,
returns its product UI and a local mood-and-ratings recommendation, and excludes a newly rated pick from the next
feed. The LocalChecklist, GardenCoop C01, GardenCoop C02, and AnimeRecommendations focused suites each pass 1/1.
All processes started by the audit were stopped.

**Transferred concern:** A focused win-x64 NativeAOT publish currently fails inside the .NET 10.0.10 ILC analyzer
with `IndexOutOfRangeException`. The same failure occurs with GardenCoop C01's original plain SDK, so it is not a
regression from the start-command correction. Its public deployment claim must be re-proved or narrowed before the
R12-05 candidate freeze; it does not invalidate the now-working ordinary start command.

## AnimeRecommendations restoration checkpoint — 2026-07-19

**Task:** Restore the recommendation product that R10-09 deleted, under the semantic identity
`samples/applications/AnimeRecommendations`, and rebuild it against the current Koan application language instead of
reviving the retired `S5.Recs` architecture. This maintainer mandate supersedes only R10-09's terminal deletion
disposition; the dated R10 evidence remains unchanged as the reason the old implementation must not return.

**Application intent:** A viewer rates anime they know and immediately discovers unfamiliar anime that match both
their stated mood and the taste expressed by those ratings. The app should be delightful on first run, remain useful
without a network or container, and teach a reader how little application machinery a current Koan recommendation
product needs.

**Public expression:** A four-line `AddKoan()` host; `Anime`, `Viewer`, and `LibraryEntry` Entities; `[Embedding]` on
the anime meaning; ordinary Entity controllers for conventional state; one business-named recommendation workflow;
thin rating/recommendation controllers; one application module for starter state and explanation; and a compact
static UI. Direct references to SQLite, ONNX, sqlite-vec, Data.AI, Web, and OpenAPI make the complete local capability
set active without application provider registration.

**Guarantee/correction:** A clean local start creates the starter viewer and catalog, indexes each anime through its
normal `Save()`, serves the product UI, accepts a bounded 1–5 rating, and returns an ordered bounded recommendation
set with human-readable reasons. Unknown viewers/anime, invalid ratings, missing query intent, unavailable AI/vector
capability, and provider failures remain explicit; the application never labels popularity fallback as semantic or
personalized output. Runtime facts and health explain the elected providers.

**Complete intent surface:** Browse the starter catalog; record or replace one viewer/anime rating; request a bounded
recommendation from an optional mood plus positively rated anime; exclude already rated titles; inspect health,
facts, and OpenAPI; reset local state by removing the sample-owned SQLite files. Remote catalog import, authentication,
social features, model training, collaborative filtering, distributed jobs, external vector servers, and production
authorization are deliberate nonclaims rather than half-built controls.

**Public concepts:** Reuse `Entity<T>`, `EntityController<T>`, `[Embedding]`, `Client.Embed`, `Vector<T>.Search`,
`KoanModule`, standard MVC controllers, ordinary DI/configuration, static files, SQLite, and HTTP. Add no application
repository, provider abstraction, worker vocabulary, cache layer, custom pipeline, or Koan-specific recommendation
framework.

**Docs read:** engineering rules, architecture principles, product constitution, Entity semantics contract, current
AI/Data-AI/Vector/Jobs/Web references, sample curriculum, R10-09's accepted deletion evidence, this R12-04 narrative
contract, and the current handoff.

**Code read:** the deleted 96-file `S5.Recs` tree and its host/model/controller/service topology at parent commit
`5310fda55`; GardenCoop Chapter 2's local embedding/search/module/test path; GoldenJourney and OrderIntake's current
job expression; current sample solution/test conventions; and the shared Entity/Web/AI/Vector contracts located by
focused constants/options/request-response searches.

**Reusing:** Preserve Recs' actual product promise—catalog discovery, ratings, personalized semantic ranking,
explanations, and a usable browser experience. Reuse GardenCoop's proven local ONNX + SQLite + sqlite-vec composition,
automatic embedding-on-save, four-line host, module-owned starter data, and focused executable-host proof.

**Creating new:** One semantic application project, three business Entities, one recommendation result contract, one
multi-Entity recommendation workflow, two thin business-action controllers plus conventional Entity controllers, one
starter catalog/module, a small static UI, one README/request file, and one focused golden-path test project. Exact
routes and limits live in one application constants owner.

**Coalescence:** Delete by omission the old manual provider/parser registry, import orchestrator, three hosted workers,
embedding monitor, raw/band/sliding caches, recommendation-settings persistence, duplicate rating/profile documents,
15-controller admin taxonomy, auth/backup/service-demo surface, compatibility paths, and service-interface pairs.
Viewer taste is derived from the bounded rating ledger at request time rather than persisted twice and synchronized.
`Save()` owns indexing; `Vector<Anime>.Search` owns candidate retrieval; one application workflow owns the irreducible
cross-Entity ranking and explanation.

**Ergonomics:** Clone, run one command, and receive a polished catalog immediately. Rate a few titles and ask in plain
language for a mood; the next result visibly changes and explains why. A reader can understand the complete backend
by reading the three Entities, one workflow, and controllers without learning an internal sample framework.

**Constraints satisfied:** Business intent leads; standard .NET and current Koan contracts precede new ceremony;
application HTTP stays controller-owned; all data operations use Entity APIs; provider references express intent;
the default is local and deterministic; focused tests replace the full ratchet; no private dogfood, `tmp/`, shelved
projects, remote configuration, publication, release, or push enters the slice.

**Risks:** A very small starter catalog proves product flow, not recommendation quality at internet scale. Bounded
in-memory reranking is honest only because the candidate and rating sets are explicitly capped. Local ONNX startup
and first indexing cost must remain acceptable. Sample polish must not expand into a second frontend framework or
restore an operations console. Adding optional external import before the core journey is proven would recreate the
architecture that caused Recs to be removed.

**Implementation result:** The retired 96-file app is replaced by a 21-file application plus a four-file focused
test project. The app contains three persisted business concepts, one derived recommendation workflow, conventional
Entity controllers, two business-action controllers, one starter module/catalog, a four-line host, and a compact
dependency-free UI. Its exact documented command starts from the repository root, serves the product on port 5094,
curates and indexes 24 titles, uses Mika's three ratings as initial taste, returns explained local recommendations,
and removes a newly rated title from the next feed. The Release build is warning-clean; the focused executable host
suite passes 1/1; the selective lock records the real promoted/unpromoted package lines; public documentation truth
passes at 688 current assets / 676 text surfaces / 107 historical boundaries / 42 navigation targets / 12 graduated
sample roots. No sample process remains.
