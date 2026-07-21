---
type: SPEC
domain: framework
title: "R10-09 - Coalesce the Semantic Sample Portfolio"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-17
  status: passed
  scope: semantic taxonomy, accepted culls, cumulative GardenCoop journey, DevPortal social cards, retained applications, and public truth
---

# R10-09 — Coalesce the semantic sample portfolio

- Tranche: `T7B — maintained-sample graduation`
- Status: `passed`
- Depends on: R10-02 exact inventory and the accepted 2026-07-17 culling assessment
- Interrupts: R10-07 implementation until the retained portfolio has stable names and locations
- Owner: sample identity, curriculum structure, cumulative journey truth, and retained application evidence

## Task

Remove samples that do not earn a distinct business lesson, replace global sequence numbers with semantic
application identity, reserve ordering for real narrative journeys, absorb narrow package demonstrations into
business applications, and make GardenCoop Chapter 2 a strict cumulative extension of Chapter 1.

## Application intent

“The cooperative starts with a small application that keeps dry garden beds from being missed; when members later
need to find available produce by meaning, the same application gains local semantic discovery without losing its
members, plots, sensors, readings, reminders, APIs, or recovery behavior.”

Independent samples have no intrinsic global sequence. Their names state the business application. The public
curriculum supplies a recommended reading order; only a journey chapter carries an ordered identifier.

## Public expression

Every GardenCoop chapter remains independently runnable through the complete canonical host:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
```

Chapter 1 references the local Data/Web capabilities required by its garden journal and expresses business through
`Entity<T>`, `EntityController<T>`, Entity Lifecycle policy, and one application `KoanModule`.

Chapter 2 preserves that complete expression and adds only:

- the Data.AI, ONNX, Vector, and sqlite-vec references;
- `Produce : Entity<Produce>` with `[Embedding]`;
- one conventional Produce controller and one business-named semantic-search controller;
- local model/database configuration and starter produce;
- one additional dashboard surface and cumulative executable assertion.

DevPortal gains social-card publication by referencing OpenGraph and declaring the `Article` card in its application
module. The OpenGraph pillar contributes middleware through Koan.Web's existing ordered pipeline seam. `Program.cs`
remains the standard four-line host.

## Guarantee and correction

- Every retained standalone application has one semantic name, one documented meaningful result, solution ownership,
  and focused evidence.
- Every GardenCoop chapter runs independently from a clean checkout and proves all earlier chapter outcomes before
  its new outcome. A later chapter cannot silently drop an Entity, route, rule, projection, or deployment statement.
- Adding Chapter 2's references and business declarations produces local semantic discovery with no external
  service. A missing configured model rejects startup with the existing corrective configuration path.
- DevPortal HTML navigation for a published Article receives its social card without application middleware wiring.
  With no registered cards or usable shell, OpenGraph safely passes the request onward.
- Deleted applications make no support or curriculum claim. Framework defects first found by them survive only in
  framework-owner tests; application-specific implementation and planning material do not remain as pseudo-samples.

## Complete intent surface

For a Chapter 1 user, no action exists beyond referencing its capabilities, writing garden Entities/rules/controllers,
and calling `AddKoan()`.

For Chapter 2, the only added actions are the four local AI/vector capability references, `[Embedding]` on Produce,
the scored search intent, bundled-model paths, and starter listings. There is no provider selector, repository,
manual host binding, readiness loop, or external runtime prerequisite.

For DevPortal, the added actions are an OpenGraph reference, an Article card declaration, and shell/default-card
configuration. Middleware placement is not application responsibility.

## Public concepts

| Concept | Why it is visible |
|---|---|
| `Entity<T>` / `EntityController<T>` | the cooperative's durable business state and conventional HTTP surface |
| Entity Lifecycle | watering policy must apply to every write path |
| `KoanModule` | each chapter owns real rule composition, starter state, and startup explanation |
| `[Embedding]` | Chapter 2 declares that Produce meaning is indexed when saved |
| `Vector<Produce>.Search` | Chapter 2 owns a scored semantic query rather than ordinary CRUD |
| `SocialCards.For<Article>` | DevPortal declares the publication metadata for one business Entity |
| journey chapter number | the code is a cumulative point in one application history |

No global sample number remains because unrelated applications have no stable ordinal meaning.

## Docs read

- `docs/engineering/index.md` — requires Entity-first access, controller-owned HTTP, centralized constants/options,
  project hygiene, and focused validation; directly governs retained applications.
- `docs/architecture/principles.md` — makes business intent the API, references the availability declaration, and
  requires compiled composition plus one current path; directly governs the reorganization.
- `docs/toc.yml` and root `README.md` — establish the public curriculum and four-line host; paths must remain current.
- `docs/guides/garden-cooperative-journal.md` — currently documents only Chapter 1 and must become the journey front
  door without turning into a second implementation authority.
- GardenCoop's two READMEs and R10-01/R10-06 — prove both current applications independently but expose the missing
  cumulative contract and conflicting deployment narrative.
- `docs/archive/proposals/complete/garden-cooperative-journal.md` — records the accepted original intent for
  `g1c1`, `g1c2`, and later requirements as chapters of one cooperative.
- `docs/decisions/DX-0045-sample-collection-strategic-realignment.md` — historical global numbering/capability-coverage
  strategy is superseded for current curriculum, but the ADR remains unchanged.

## Code read

- `samples/guides/g1c1.GardenCoop/**` — coherent Chapter 1 MVP with five garden Entities, lifecycle automation,
  controllers, one module, and a four-line host; keep as the cumulative base.
- `samples/guides/g1c2.GardenCoopEmbedded/**` — compact local semantic Produce application but not a Chapter 1
  extension; absorb its unique AI/vector files into a full Chapter 2 snapshot.
- both GardenCoop focused sample suites — independently prove each current outcome; rebuild Chapter 2 evidence so it
  first proves the full Chapter 1 story.
- `samples/S10.DevPortal/**` — coherent publication workflow and ideal business owner for Article social cards.
- `src/Koan.Web.OpenGraph/**` — owns card registration/rendering but currently requires consumer middleware wiring.
- `src/Koan.Web/Hosting/IKoanWebPipelineContributor.cs` and `DevIdentityContributor` — closest ordering-safe pattern
  for a functional web pillar to contribute middleware without changing application hosts.
- S5, S7, S8 shells, S18, S19, S20, archived projects, and their tests — assessed for unique behavior, evidence,
  overlap, and maintenance burden; the accepted terminal dispositions are recorded below.

## Reusing

- the current Chapter 1 source and cumulative garden test;
- Chapter 2's `Produce`, local embedding/search expression, bundled model, configuration, and semantic assertion;
- the canonical four-line host and application-module lifecycle;
- OpenGraph's registry, renderer, options, and `UseOpenGraphCards()` middleware implementation;
- Koan.Web's ordered `IKoanWebPipelineContributor` seam;
- existing graduated sample tests and the solution-owned sample test convention.

## Creating new

| New code/artifact | Location | Justification |
|---|---|---|
| semantic sample taxonomy | `samples/fundamentals/`, `samples/journeys/`, `samples/applications/` | groups by intent; only journeys encode order |
| GardenCoop journey index | `samples/journeys/GardenCoop/README.md` | one owner for chapter order, cumulative laws, and selection |
| Chapter 1 snapshot | `samples/journeys/GardenCoop/01-GardenJournal/` | retained meaningful MVP under semantic identity |
| Chapter 2 snapshot | `samples/journeys/GardenCoop/02-LocalDiscovery/` | independently runnable strict superset plus local semantic discovery |
| Produce routes | Chapter 2 `Infrastructure/GardenApiRoutes.cs` | stable garden API identifiers stay centralized |
| OpenGraph pipeline contributor | `src/Koan.Web.OpenGraph/Hosting/OpenGraphPipelineContributor.cs` | pillar-owned use of the existing canonical web seam |
| DevPortal application module | `samples/applications/DevPortal/Initialization/DevPortalModule.cs` | application owns Article card declaration and startup explanation |
| cumulative Chapter 2 spec | renamed GardenCoop Chapter 2 test project | prevents later chapters from dropping prior behavior |
| portfolio/current-doc updates | sample index, guide, R10/NOW/PROGRESS, scripts, solution | one current public and maintainer truth |

## Coalescence

- **Closest cumulative pattern:** FirstUse → GoldenJourney preserves the host while references and business concepts
  add a meaningful outcome. GardenCoop applies that principle as independently runnable chapter snapshots.
- **Current decision owners:** sample paths incorrectly own global order; g1c2 owns a separate application; S20 owns
  a package mechanism demo; application `Program.cs` owns OpenGraph middleware placement.
- **State/lifetime/hot path:** chapter identity is repository curriculum state; AI/provider composition occurs once
  per host; social-card declarations compile during application composition; OpenGraph request middleware resolves
  the retained renderer and does no discovery or election.
- **Specificity:** portfolio taxonomy is R10 curriculum policy; chapter deltas are application policy; OpenGraph
  pipeline placement is pillar policy using Core Web law.
- **Disposition:** keep the seven graduated outcomes; keep and rename SnapVault; rebuild Canon and OrderIntake under
  semantic names; absorb S20 into DevPortal; rebuild g1c2 as cumulative; delete S5, S7, S18, S19, empty S8 shells,
  archived sample source, and application-only tests; remove local ghost directories separately.
- **One target owner:** `samples/README.md` owns public curriculum order, `samples/journeys/GardenCoop/README.md` owns
  chapter order, each application owns its business additions, and OpenGraph owns middleware contribution. A global
  numeric directory scheme is too broad; `Program.cs` is too narrow for pillar middleware law.
- **Superseded paths:** global `S#` identities, independent g1c2 application semantics, manual OpenGraph registration
  and endpoint/middleware demo, empty Api/Shared sample projects, historical executable archive, and speculative
  application specifications presented beside maintained samples.

## Ergonomics

- Humans browse business names, choose either a focused application or a cumulative journey, and see exactly what
  changed between chapters.
- IntelliSense grows from Entity declarations and referenced capabilities; chapter code does not introduce a shared
  sample framework or inheritance hierarchy.
- Coding agents can infer that every Chapter N contains Chapter N-1 behavior and verify it through one cumulative
  test. Paths no longer encode obsolete portfolio history.
- Intent branches are reduced: standalone application, fundamental, or journey chapter. No capability matrix or
  arbitrary number is required to understand identity.

## Constraints satisfied

- Entity statics remain the application data language; no repository layer is introduced.
- All new HTTP behavior remains controller-owned; OpenGraph is middleware, not an inline application endpoint.
- Stable routes live in project infrastructure constants; provider/model tunables remain configuration/options.
- No empty placeholders, compatibility aliases, shared chapter base project, or commented scaffolding survives.
- All current data sets are deliberately bounded; no unsupported streaming claim is added.
- ADRs remain unchanged. Current guides, reference cards, solution membership, scripts, and sample docs are aligned.
- Focused owner/consumer evidence runs during implementation; release certification remains a portfolio-boundary gate.

## Risks

- Path and assembly renames touch solution/test/script/current-doc references; perform one mechanical move and verify
  no current references to retired identities remain while leaving historical ADR text unchanged.
- Chapter snapshots intentionally duplicate stable application code. A shared base would reduce files but destroy
  independent readability and make the delta depend on hidden inheritance; duplication is the chosen tradeoff.
- Existing g1c2 NativeAOT documents conflict with its current R10 contract. Until the rebuilt Chapter 2 is freshly
  published and run, current guidance must retain only the measured Chapter 1 claim and describe older g1c2 evidence
  as historical ADR context.
- OpenGraph's automatic contributor must be inert when no card/shell exists and must run before routing without
  intercepting APIs/assets; focused pipeline evidence is required before deleting S20.

## Accepted disposition map

| Current | Terminal disposition |
|---|---|
| `FirstUse`, `GoldenJourney` | keep at the sample front door |
| `S0.ConsoleJsonRepo` | rename to `fundamentals/LocalChecklist` |
| `S1.Web` | rename to `fundamentals/TaskGraph` |
| `g1c1.GardenCoop` | rename to `journeys/GardenCoop/01-GardenJournal` |
| `g1c2.GardenCoopEmbedded` | rebuild as `journeys/GardenCoop/02-LocalDiscovery`, strict C1 superset |
| `S10.DevPortal` + `S20.OpenGraph` | `applications/DevPortal`; absorb social cards, delete S20 |
| real `S8.Canon` project | `applications/CustomerCanon`; delete Api/Shared shells |
| `S14.AdapterBench` | `applications/OrderIntake`; retain the accepted R10-07 rebuild contract |
| `S6.SnapVault` | `applications/SnapVault`; retain for later flagship graduation |
| `S5.Recs`, `S7.Meridian`, `S18.Prism`, `S19.McpCatalogSample` | delete after preserving framework-owner regressions only |
| `samples/archive/` | delete; Git history remains the archive |
| `S3.Mq.Sample`, `S16.PantryPal` | remove untracked local ghost/runtime debris; no repository artifact exists |

## Focused acceptance

1. Physical directories, project/assembly identities, solution membership, tests, scripts, and current docs use the
   semantic taxonomy; current non-ADR material contains no retired sample identity.
2. Deleted samples and archive projects are absent. Every remaining non-scratch sample project has a terminal R10
   status and focused evidence or an explicit active rebuild card.
3. DevPortal's existing publication proof gains one Article social-card assertion while its four-line host remains
   unchanged; OpenGraph package tests prove inert and active pipeline contribution.
4. Chapter 1 retains its complete garden result. Chapter 2 proves that same result plus five Produce Entities and
   local semantic search, with no external service or manual host/provider setup.
5. The GardenCoop journey index and guide explain meaningful requirement-driven deltas, not capability bingo.
6. Current NativeAOT/sample claims match freshly retained evidence; historical ADRs remain untouched.
7. Focused warning-as-error builds/tests, public-doc lint, diff/privacy checks, and the appropriate portfolio boundary
   pass without running release certification during each edit.

## Acceptance evidence

- The retained portfolio now uses `fundamentals`, `journeys`, and `applications`; unrelated applications no longer
  carry global sequence numbers. `Koan.sln`, project identities, focused suites, lockfiles, READMEs, and current
  guides agree with those paths.
- GardenCoop Chapter 1 passes its complete dry-bed/recovery contract (1/1). Chapter 2 passes a cumulative contract
  (1/1) that first proves the same plots, sensors, readings, reminder, and recovery, then proves five Produce records
  and local `ripe red tomato` semantic discovery through ONNX and sqlite-vec.
- OpenGraph contributes its middleware through Koan.Web's ordered pipeline seam. Its owner suite passes 39/39, and
  DevPortal passes 1/1 with the unchanged four-line host plus an Article social-card navigation assertion.
- LocalChecklist passes 1/1 and a strict Release build; TaskGraph passes 5 with 2 intentional capability skips;
  SnapVault passes 33/33; CustomerCanon and OrderIntake build with zero warnings and zero errors.
- Deleted samples, executable archive projects, duplicate shells, stale application-only tests, and obsolete
  operational narratives are absent. `samples/README.md` explicitly distinguishes graduated curriculum from the
  applications still awaiting a business-level graduation.
- The public documentation truth gate passes across 174 current files and 36 navigation targets. `git diff
  --check` reports no whitespace error; line-ending notices remain repository-normal. ADR files were not edited.

R10-09 therefore passes. R10-07 resumes at `applications/OrderIntake`; this slice does not claim that the three
retained but ungraduated applications are complete curriculum.
