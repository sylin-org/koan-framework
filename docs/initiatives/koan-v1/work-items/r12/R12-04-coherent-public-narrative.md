---
type: SPEC
domain: framework
title: "R12-04 - Establish One Coherent Public Narrative"
audience: [architects, maintainers, developers, technical-writers, ai-agents]
status: current
last_updated: 2026-07-20
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-20
  status: passed
  scope: coherent 0.20 public graph, post-extraction sample topology, executable guidance, and anti-drift gate
---

# R12-04 — Establish one coherent public narrative

- Tranche: `T7B — public product maturity`
- Status: `passed`
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

The current projection contains 699 current assets, 676 current text surfaces, 107 historical boundaries,
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

- public documentation truth: 699 current assets / 676 current text surfaces / 107 historical boundaries /
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
passes at 699 current assets / 676 text surfaces / 107 historical boundaries / 42 navigation targets / 12 graduated
sample roots. No sample process remains.

## Uniform sample launcher checkpoint — 2026-07-19

**Task:** Give every graduated sample executable a root-level `start.bat` without recreating a Koan runner or the
retired Docker/orchestration launcher generation.

**Application intent:** On Windows, a reader should enter any sample root and start it through the same obvious file,
regardless of the shell's original working directory.

**Public expression:** `start.bat [application arguments]`. Each launcher changes to its own directory, invokes that
root's exact project with standard `dotnet run`, forwards arguments after `--`, restores the caller's directory, and
returns the application/SDK exit code. Portable `dotnet run --project ...` commands remain the cross-platform owner.

**Guarantee/correction:** Every root admitted once through `samples/README.md` contains one tracked `start.bat` and
can resolve project-local settings, assets, models, and state. A missing launcher fails `public-docs-lint.ps1` with
the exact graduated root and correction. SDK, restore, build, application, and prerequisite failures remain visible
through their original output and nonzero exit code.

**Complete intent surface:** Run or double-click `start.bat`; optionally append the same application arguments accepted
by `dotnet run -- ...`. No launcher configuration, generated manifest, environment probing, browser automation,
container startup, process supervision, or Koan-specific command is introduced. Each sample retains its already
documented runtime prerequisites and launch-profile/default URL.

**Public concepts:** One ordinary Windows batch convenience and the existing .NET project/launch-profile vocabulary.
No framework API, option, service, executable, or package is added.

**Docs read:** engineering guardrails, architecture principles, the graduated sample index, and the sample portfolio
standard establish standard-.NET startup, one current path, project-root ownership, and graduation evidence.

**Code read:** the FirstUse, LocalChecklist, TaskGraph, AnimeRecommendations, and GardenCoop Chapter 1 hosts/projects;
all eleven graduated project roots; the current public-document graph; the retired S5 and attic batch launchers as
negative Docker/orchestration evidence.

**Reusing:** Each root's exact `.csproj`, existing launch profiles/defaults, project-relative content behavior, and
`public-docs-lint.ps1`'s derived `$sampleDirectories` set.

**Creating new:** Eleven root-local seven-line `start.bat` files, one public sample-index instruction, one portfolio
graduation rule, and one invariant in the existing public-document truth gate. No shared launcher file or generator.

**Coalescence:** Keep standard `dotnet run` as execution owner; absorb only working-directory normalization, argument
forwarding, and exit-code preservation into each required Windows entry file. Delete nothing because no current
launcher competes. Historical Docker-compose launchers remain historical and do not become a template. The graduated
sample graph is the single enforcement owner; a second sample registry or launcher validator is rejected.

**Ergonomics:** The same filename works in every sample, from Explorer or any shell. The seven readable lines expose
the exact project rather than hiding it behind discovery. Humans and coding agents retain one execution model and can
still copy the portable command from the sample index.

**Constraints satisfied:** No production runtime, HTTP, Entity, provider, configuration, package, or remote state
changes; no inline endpoints, constants/options, data access, shared wrapper, private dogfood, shelved content, or
`tmp/`; focused launcher and public-document evidence only.

**Risks:** Batch quoting, an empty `%*`, project paths containing spaces, console exit, and long-running Web process
termination require focused direct checks. Launcher tests must stop only the exact processes they start.

**Implementation result:** All eleven executable roots admitted through the public sample index contain the same
seven-line launcher shape and an explicit tracked target project. LocalChecklist's launcher runs from the repository
root, resolves its project-local content, prints the expected checklist result, and exits zero. Each of the ten Web
launchers runs from that same external directory, forwards an isolated `--urls` argument, starts its exact host, and
returns HTTP 200 from `/health/live`. The audit stops the exact listening process after each proof; ports 5221–5230
have no listeners and no sample process remains. `public-docs-lint.ps1` derives executable roots from the existing
sample graph, validates launcher mechanics/targets, and passes at 699 current assets / 676 current text surfaces /
107 historical boundaries / 42 navigation targets / 12 graduated sample roots. No full sample suites or release
ratchet ran.

## AnimeRecommendations portability and durability realignment — 2026-07-19

This checkpoint supersedes the embedded-only boundary and the uniform-launcher assumption above for
AnimeRecommendations. Those records remain as evidence of the decisions that produced the regression; they are not
the accepted product promise after the maintainer clarified that “less but more meaningful moving parts” constrains
Koan's application grammar, not an application's legitimate deployment topology or durable user value.

**Task:** Restore the valuable S5.Recs durability and distributed-runtime experience in AnimeRecommendations without
restoring its 96-file service architecture, while proving that one Koan business application keeps its quality across
embedded and Compose-selected providers.

**Application intent:** Acquire an anime catalog once, preserve its source evidence and viewer state across process or
container replacement, and return the same kind of explained recommendations whether the application uses embedded
or distributed persistence, vector, and embedding resources.

**Public expression:** The application host remains `AddKoan()` plus `RunAsync()`. `Anime` remains automatically
embedded on `Save()` without naming a provider model. `CatalogImport : Entity<CatalogImport>, IKoanJob<CatalogImport>`
is the one durable acquisition/rebuild expression. `dotnet run --project samples/applications/AnimeRecommendations`
selects SQLite + sqlite-vec + ONNX through ordinary configuration; root `start.bat` owns the standard Docker Compose
topology and selects Mongo + Weaviate + Ollama through environment configuration. The same Entities, import job,
recommendation workflow, controllers, UI, cache format, and acceptance contract serve both.

**Guarantee/correction:** A successful source refresh writes every raw page before projection, records one completed
manifest, resumes the same attempt from already cached pages after job redelivery, and projects stable source records
through normal Entity `Save()` so the elected vector/AI providers index them. A completed manifest can rebuild an
empty catalog without network access. Local SQLite state and Compose bind-mounted Mongo, Weaviate, Ollama model, and
source-cache directories survive ordinary restarts and container replacement. Unsupported provider placement,
missing models, source HTTP/rate-limit failure, malformed source data, missing cache manifests, and failed durable-job
requirements remain explicit through startup, health, facts, HTTP problem responses, and the Jobs ledger; no fallback
is mislabeled as equivalent quality. Reset and cache flush are explicit destructive actions and never occur during
start.

**Complete intent surface:** Run `start.bat` for the complete Compose topology or the documented `dotnet run` command
for the embedded topology; browse and rate the starter catalog immediately; request explained recommendations; queue
an incremental AniList refresh; inspect durable job progress and completed source manifests; rebuild from one cached
manifest offline; explicitly flush source evidence when intended; inspect health and runtime facts; stop Compose with
the documented standard command. No provider-specific application branch, manual service registration, repository,
custom worker, hidden reset, generated launcher, Koan CLI, auth/control plane, or second recommendation algorithm is
required.

**Public concepts:** Existing `Entity<T>`, `EntityController<T>`, `[Embedding]`, `Client.Embed`, `Vector<T>.Search`,
`IKoanJob<T>`, `JobContext`, `KoanModule`, MVC, options, `HttpClient`, filesystem APIs, and Docker Compose retain their
current meaning. One `CatalogImport` business Entity exists because acquisition/rebuild is durable work with progress.
One application-scoped source contributor exists because source protocol/parsing changes independently of cache and
projection while multiple sources must share the same chokepoint. One typed catalog-options object exists because
endpoint, paging, retry, and cache placement are deployment tunables. A cache manifest is durable source evidence,
not a second application database.

**Docs read:** `docs/engineering/index.md` establishes controller/entity/constants/options and focused-validation
guardrails; `docs/architecture/principles.md` establishes business-first code, provider election, thin adapters, and
standard-.NET ownership; `docs/toc.yml`, root `README.md`, and `samples/CATALOG.md` establish the current public front
doors; the product constitution establishes honest topology commitments and golden-sample evidence; the current Jobs
card establishes `IKoanJob<T>` as the durable restart-safe work expression; current Mongo, Weaviate, and Ollama package
docs establish exact provider configuration and capability boundaries; NOW and this card establish the active R12
handoff.

**Code read:** Current AnimeRecommendations host, Entities, workflow, controllers, module, configuration, UI, and
focused fixture; GoldenJourney and SnapVault durable-job expressions; Data default-provider, Vector default-provider,
ONNX/Ollama source, Mongo, and Weaviate composition owners; the deleted S5.Recs `start.bat`, Dockerfile, Compose graph,
AniList provider, raw-page cache/manifests, rebuild endpoints, and import jobs. Explicit repository searches found
1,413 constant declarations, 935 constants-owner references, 528 options references, and 913 record/DTO/request/
response candidates; no current general source-acquisition/cache contract owns this application-specific lifecycle.

**Reusing:** Koan Jobs owns durable execution, retries, progress, ledger status, and restart recovery. Entity statics
own all application data. Automatic embedding-on-save owns indexing. Data/Vector/AI composition owns provider choice
and facts. Standard `HttpClient` owns source transport, the filesystem owns raw evidence, and Docker Compose owns the
distributed topology. The current starter catalog remains the immediate first-use floor; the current recommendation
workflow remains the one provider-independent quality owner.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| `CatalogOptions` | `samples/applications/AnimeRecommendations/Options/CatalogOptions.cs` | One typed owner for source endpoint, bounded paging/retry, and cache placement. |
| `IAnimeCatalogSourceContributor` and source-page contract | `samples/applications/AnimeRecommendations/Catalog/` | Application-specific protocol/parsing contribution chokepoint; no framework law is implied. |
| `AniListCatalogSourceContributor` | `samples/applications/AnimeRecommendations/Catalog/AniListCatalogSourceContributor.cs` | Thin AniList HTTP/JSON mechanics behind the source boundary. |
| `CatalogSourceCache` and manifest | `samples/applications/AnimeRecommendations/Catalog/` | One crash-safe raw-page/manifests owner shared by refresh, resume, inspection, and rebuild. |
| `CatalogImportWorkflow` | `samples/applications/AnimeRecommendations/Catalog/CatalogImportWorkflow.cs` | The irreducible acquire/cache/project workflow, separate from HTTP and the job ledger. |
| `CatalogImport` | `samples/applications/AnimeRecommendations/Domain/CatalogImport.cs` | Durable Entity work and progress using the existing Jobs pillar. |
| `CatalogOperationsController` | `samples/applications/AnimeRecommendations/Controllers/CatalogOperationsController.cs` | Thin controller for refresh, status, manifests, rebuild, and explicit flush actions. |
| Compose assets | `samples/applications/AnimeRecommendations/docker/` and root `start.bat` | Standard topology owner for app + Mongo + Weaviate + Ollama and durable bind mounts. |
| Focused cache/import/provider-parity evidence | `tests/Suites/Samples/Koan.Samples.AnimeRecommendations.Tests/` | Proves source evidence, offline rebuild, unchanged recommendation behavior, and embedded/Compose contract parity. |

**Coalescence:** Closest historical pattern is S5.Recs' AniList provider plus raw cache; closest current execution
pattern is SnapVault's Entity job. Rebuild the historical acquisition capability but delete its bespoke import
orchestrator, three workers, provider/parser registries, settings Entity, band/sliding caches, duplicate profiles, and
admin control plane. Application specificity is correct: source acquisition is AnimeRecommendations business
behavior, while Jobs/Data/Vector/AI/Compose already own their wider laws. `CatalogImportWorkflow` is the single owner;
putting source protocol in the Entity is narrower and unreadable, while adding a Koan source-ingestion pillar is wider
than the demonstrated reuse. No separate application microservice is introduced merely to claim microservices; the
Compose profile honestly demonstrates a containerized Koan workload composed with independently replaceable remote
data, vector, and AI services. A future second business workload requires its own justified contract boundary.

**Ergonomics:** A reader sees one four-line host, ordinary Entities, one named durable job, and one named workflow.
IntelliSense discovers durable work on `CatalogImport.Job`; source contributors remain behind DI and do not contaminate
the recommendation path. Human-readable configuration names the elected providers, runtime facts confirm them, and
the UI presents refresh/rebuild status without exposing queues or adapter APIs. The only topology branch is the user's
standard launch/configuration choice; business code has none.

**Constraints satisfied:** No inline endpoints; all HTTP remains controller-owned. Entity statics replace repositories.
Stable routes/source IDs/cache names belong to the application constants owner; deployment tunables belong to typed
options. Catalog projection is explicitly page-bounded and never calls unbounded `All`/stream APIs. Cross-module Koan
contracts remain untouched and isolated. Standard .NET and existing Koan capabilities precede new ceremony. Public
docs and the launcher invariant change with the product promise. Validation remains focused; private dogfood, `tmp/`,
shelved projects, remotes, publication, tagging, release, and the full ratchet remain out of scope.

**Risks:** AniList availability and rate limits cannot be a startup dependency; the starter catalog preserves first
use. Different embedding models will not produce byte-identical rankings, so parity evidence must assert bounded
business relevance and exclusions rather than exact order. Compose image/model pulls are expensive on first use and
must report progress rather than appear hung. Bind mounts require path and reset safety. Provider connectors are below
the supported 0.20 boundary, so this sample demonstrates interchangeability without promoting their maturity.

**Implementation result:** AnimeRecommendations now provides the same lean application over an embedded
SQLite/sqlite-vec/ONNX profile and a root-launchable Mongo/Weaviate/Ollama Compose profile. The latter ran healthy on
MongoDB 8.3.4, Weaviate 1.37.6, and Ollama 0.32.0; runtime facts selected Mongo and Weaviate, the packaged lockfile
matched all 22 modules, the 24 starter records plus one cached AniList record remained available after application
recreation, and a pre-existing raw-page manifest rebuilt one item offline as a completed durable job. Explainable
recommendations returned five requested results after recreation. Compose was stopped without volume deletion and
the versioned Mongo/Weaviate data, source cache, and Ollama model directories remain under the sample's `.koan`
owner. Focused sample evidence passes 3/3; no full release ratchet ran.

## Pre-release dependency modernization checkpoint — 2026-07-19

**Task:** Promote Koan's exact .NET 10 toolchain pin to the latest stable SDK and evaluate every release-relevant
external dependency against its latest stable release before the initial 0.20 publication.

**Application intent:** A contributor or release runner should build the initial Koan preview from one current,
reproducible dependency constitution rather than inherit unexplained old feature bands, floating versions, or
project-by-project version drift.

**Public expression:** `global.json` pins the current stable .NET 10 SDK exactly. Standard .NET Central Package
Management owns one version for each external NuGet identity in `Directory.Packages.props`; ordinary project
`PackageReference` items state only dependency intent. Internal `Sylin.Koan.*` compatibility placeholders and template
substitution properties remain owned by their existing packaging contracts. Workflow actions and runnable sample
container images use reviewed, reproducible versions rather than accidental stale or floating inputs.

**Guarantee/correction:** The same commit restores, builds, tests, and packages with one explicit external dependency
graph. A missing central version, incompatible major upgrade, advisory, stale release literal, or unexplained local
override fails focused restore/build/package evidence and is corrected at the owning dependency or recorded as a
time-bounded exception. Reproducibility never means silently retaining an old dependency; freshness never means
floating a build input between commits.

**Complete intent surface:** Install the SDK named by `global.json`, restore normally, and build. Maintainers update
one central NuGet declaration per external identity and the exact pins for SDK, workflow, tool, and container inputs.
No Koan dependency manager, version registry, generated application ceremony, or runtime configuration is added.

**Public concepts:** `global.json`, `Directory.Packages.props`, `PackageReference`, NuGet restore/audit, GitHub Actions,
and Docker image tags are standard platform concepts. The only policy concept is “latest stable by default”; an
exception must name the compatibility blocker and its revisit condition.

**Docs read:** The engineering front door requires evaluated packaging and clean metadata; architecture principles
prefer standard .NET and one current path; the public TOC and root README establish 0.20 as the initial preview line;
ARCH-0082/0085 establish independently versioned Koan packages; ARCH-0110 establishes exact-toolchain, clean-room,
audited release proof.

**Code read:** `global.json` and `release-on-dev.yml` currently split one SDK policy across five literals;
`Directory.Build.props` repeats SourceLink/NBGV versions into every project; 216 solution projects contain 755
release-relevant `PackageReference` sites for 91 identities; `.config/dotnet-tools.json` separately pins NBGV;
`ReleaseWorkflowContractTests` protects exact release-action identities and trust boundaries. Explicit searches found
1,422 constant declarations, 935 constants-owner references, 536 options references, and 926 record/DTO/request/
response candidates; none is relevant because this slice changes build dependency ownership, not runtime behavior.

**Reusing:** NuGet restore/audit, MSBuild evaluation, NBGV, the release compiler, package-graph tests, workflow contract
tests, Docker Compose validation, and focused connector/sample suites remain the proof owners. The official .NET 10
release metadata reports SDK 10.0.302/runtime 10.0.10 as the current stable line. The NuGet flat-container index was
queried for all 86 external identities discovered in the release-relevant graph; no identity failed lookup.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| Central external package versions | `Directory.Packages.props` | Standard .NET single owner replacing repeated and divergent version literals. |
| Freshness policy and exception contract | current engineering/packaging documentation | Makes “latest stable” enforceable and prevents unexplained holds from returning. |
| Focused dependency audit evidence | existing packaging tests/scripts where a durable gate is justified | Reuses the release-quality chokepoint instead of adding another versioning subsystem. |

**Coalescence:** The closest current owner is root `Directory.Build.props`, but it mixes universal build behavior with
two package versions and cannot govern the other 753 sites. Absorb external NuGet versions into the SDK-native central
package file; keep dependency intent at each consumer; keep internal Koan range tokens at their packaging owners;
delete external `Version` duplication and floating ranges. The repository root is the one correct lifetime and scope:
project-local ownership is too narrow and the release compiler is too late because restore has already resolved the
graph. Human review gains one readable inventory, IntelliSense continues to discover ordinary PackageReferences,
and the coding model loses version branches without gaining a Koan abstraction.

**Ergonomics:** A dependency upgrade becomes one obvious diff plus any necessary source adaptation. Contributors no
longer search hundreds of project files or wonder which duplicate is authoritative. Exact SDK/action/image pins keep
failures attributable to a commit; automated or manual freshness checks can propose the next stable version without
changing builds implicitly.

**Constraints satisfied:** No application HTTP, Entity, data, options, or provider semantics change by architecture;
standard .NET owns dependency management; internal cross-package compatibility remains isolated; no inline endpoints,
runtime constants, repositories, private dogfood, shelved scope, `tmp/`, remote mutation, publication, tag, push, or
full release ratchet enters the slice. Major dependency adaptations will stay at the existing package owner and receive
focused tests.

**Risks:** Latest stable major releases can contain intentional API or behavior breaks; each must be compiled and
tested rather than mechanically declared successful. Some packages may lag current .NET support or constrain a peer
dependency. Container service majors can change persisted formats, so runnable samples require explicit migration or
fresh-state evidence. Updating the SDK changes compiler/MSBuild behavior and therefore requires packaging/generator
proof in addition to application builds. The current machine lacks SDK 10.0.302, so validation must use an isolated
exact SDK installation until the workstation installs the repository prerequisite.

**Implementation result:** The repository now pins SDK 10.0.302, runtime images 10.0.10, NBGV 3.10.91, exact current
GitHub Action SHAs, and one Central Package Management inventory for the 216-project solution. All listed stable NuGet
updates were adopted except four explicit constraints: Microsoft.OpenApi remains on 2.11.0 because ASP.NET Core
10.0.10's OpenAPI generator is incompatible with the 3.x read-only model; ImageSharp 3.1.12 and Drawing 2.1.7 remain
the latest freely distributable compatible pair because Drawing 3 requires a private Six Labors build license;
StreamJsonRpc 2.25.29 is the same NuGet identity displayed with `+RR` metadata; Couchbase.Transactions 3.9.0 remains
deprecated-but-required because CouchbaseNetClient 3.9.4 does not expose the transactions namespaces. Milvus'
official 2.6.20 etcd 3.5.25 and MinIO 2024-12-18 companions remain a tested stack rather than independently promoted
incompatibly. All runnable production/sample containers use exact non-floating tags and versioned persisted-data
paths where service majors changed.

The complete solution restore resolves all 216 projects and the transitive vulnerability audit reports zero known
vulnerable packages. Focused evidence passes dependency/workflow governance 12/12, filtering 94/94, RabbitMQ 8/8,
OpenAPI 12/12, Milvus 25 passed with eight declared capability skips, and each promoted connector suite exercised
against its exact current service image. The dependency constitution now fails duplicate/missing external NuGet
ownership, inline external versions, non-exact SDK/action selection, and floating release-relevant container tags.

## Usagi Picks product graduation checkpoint — 2026-07-20

The maintainer promoted AnimeRecommendations from bundled framework sample to the standalone product repository
`https://github.com/lbotinelly/usagipicks`. The application and focused tests moved rather than being rebuilt; the
shared ONNX model was copied because other Koan samples still consume it. Existing ignored `.koan` state moved with
the product, including raw-source manifests, SQLite, versioned MongoDB/Weaviate stores, and the Ollama model. Legacy
database identifiers remain intentionally stable so the product rename does not orphan preserved data.

The standalone repository owns a temporary explicit source-checkout bridge because the complete set of required
preview packages is not yet public; ordinary package references remain the permanent boundary. It also records the
accepted next product specification: Media as the common business primitive with honest Anime/Manga subtype meaning;
artwork captured, stored, and locally served through Koan.Media; public and authenticated journeys; identity-owned
libraries, feedback, and progress; several user-named explainable discovery modes; a protected task-oriented catalog
administration experience; and restoration of the mature interaction depth under the Usagi Picks brand. Historical
S5.Recs commit `5310fda55c4608540f476990e23f219823044388` is retained as capability/UI evidence, never as an architecture
to transplant.

Koan's solution, bundled sample catalog, and current AI/vector references no longer point at deleted local paths;
they identify Usagi Picks as a standalone application where relevant. Historical initiative passages remain evidence
of the decisions and tests that occurred before graduation. The current bundled graduated portfolio is ten
executables; prior eleven-sample test counts are historical results, not a current inventory claim.

## Post-extraction public-graph closure checkpoint — 2026-07-20

**Application intent:** A reader entering Koan through any current front door sees one 0.20 preview product and
understands that Usagi Picks is a separate Koan application, not a missing or partially removed bundled sample.

**Complete expression:** The canonical install → four-line `AddKoan()` host → first `Entity<T>` result → progressive
capability-reference path remains unchanged. `samples/README.md` admits eleven curriculum roots containing ten
executables, and AI/vector guidance links directly to the standalone Usagi Picks repository where its complete
product journey is relevant.

**Guarantee/correction:** No current public surface may point at the removed local AnimeRecommendations application or
tests, repeat its old bundled-sample totals as present fact, or require historical initiative prose to explain the
move. The existing public-content compiler derives the current graph and reports the exact offending path; dated
checkpoints retain the counts and evidence that were true when they ran.

**Coalescence:** Keep `scripts/public-docs-lint.ps1` as the single inventory and narrative-invariant owner. Sample
admission remains one ordinary README link, local launchability remains the existing root `start.bat` contract, and
an ordinary external repository link expresses the product boundary. Add no sample manifest, redirect stub,
Usagi-specific compiler rule, or second documentation ledger.

**Ergonomics:** A developer can distinguish bundled learning applications from a standalone product at a glance;
there is no dead local path, unexplained count mismatch, or duplicate launch model. Maintainers continue to run one
discoverable truth command and reason about one derived graph.

**Implementation boundary:** Reconcile current front doors, sample inventory, AI/vector references, solution
membership, this work card, and `NOW.md`. Preserve historical evidence unchanged. Do not modify runtime APIs,
generated maturity truth, ADR conclusions, sample behavior, remote state, or the R12-05 candidate-certification
boundary.

### Closure result

The intended patch passes the public-document truth gate in an isolated worktree at 677 current assets, 656 current
text surfaces, 107 historical boundaries, 42 navigation targets, and eleven graduated curriculum roots. All ten
executable roots have the standard launcher contract; the GardenCoop overview is the one non-executable admitted
root. The broad current documentation structure check reports zero errors and fourteen non-gating frontmatter
warnings, the four changed instructional files contain no marked executable code block, scoped whitespace checks
pass, and current-path plus solution searches find zero deleted AnimeRecommendations references.

The same public gate run in the working checkout stops only because its deliberate isolation guard observes existing
changes to `DATA-0103` and `SERV-0001`. Those ADR edits belong to the separate dependency/source work already present
in the dirty tree; this slice neither changes them nor weakens the guard. No full ratchet, sample suite, remote
mutation, publication, tag, push, or private dogfood inspection ran. R12-05 now owns the exact frozen-candidate proof.
