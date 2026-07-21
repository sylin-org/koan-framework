---
type: SPEC
domain: framework
title: "R10-10 - Graduate SnapVault as the local-first studio proof"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-18
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-18
  status: passed
  scope: local-first composition, participation-owned vector readiness, studio-to-client proof, public sample truth
---

# R10-10 — Graduate SnapVault as the local-first studio proof

- Tranche: `T7B — active-sample graduation`
- Status: `passed`
- Depends on: R10-09 semantic sample portfolio; R10-07 participation-owned Data readiness
- Unlocks: final active-application assessment (`CustomerCanon`)
- Owner: one local-first photo-studio result with optional AI/vector enrichment and fail-closed client access

## Meaningful outcome

A developer runs SnapVault from a fresh checkout without Docker, uploads one photo, sees Koan durably file and
serve it inside the local studio, and can grant that event to one known durable client whose access is structurally
limited to that gallery. External AI, vector, MongoDB, and object-storage providers remain composable enhancements;
their absence never makes the core application pretend to be broken, and using one makes its health responsibility
and correction visible.

## Why now

SnapVault is the smallest remaining likely graduation because its real `AddKoan()` suite already passes 33/33
across record, job, vector, blob, media, guest-access, mutation, and maintenance behavior. The application is still
absent from the public portfolio because its default data provider is MongoDB, it has no standard Development launch
profile, its Docker launcher assumes an external host mesh, Weaviate is readiness-critical merely by reference, and
its README offers maintainers a test command instead of giving developers a meaningful first result.

## Application intent

“Upload a photo into my studio, let Koan durably organize and serve it, then share that event with one client who
can see and proof only the photos I granted.”

## Public expression

The host remains the standard four lines plus the standard ASP.NET Core SPA fallback:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
```

The complete first action is:

```powershell
dotnet run --project samples/applications/SnapVault
```

The developer opens the printed URL and uploads one JPEG or PNG. The default composition uses SQLite and local
blob storage under `.koan/`; `PhotoAsset`, `Event`, `PhotoProcessingJob`, media recipes, and the existing controllers
express the business result. No container, AI model, vector service, cloud credential, tenant identifier, repository,
worker registration, or schema step is required. An operator may later enable an explicitly documented external
composition without changing those business terminals.

## Guarantee and correction

The local path stages the bytes under the ambient studio, submits one tenant-carrying durable job, stores one unique
original blob, extracts safe metadata, creates or reuses the UTC-day event, persists the photo, serves on-demand
media recipes, and reports progress from the Jobs ledger. AI failure is non-fatal and explicit: the stored photo
remains usable with a failed/unavailable analysis state and can be retried after an eligible provider appears.

An operator grants a known active durable person access to one event. Raw Entity reads, media reads, and proofing
writes all fail closed outside that event; access closure removes the grant and membership and emits an
integrity-checked operation record. Token invitation acceptance is deliberately absent until Koan can prove a
distributed single-claim ceremony.

Referenced-but-unused external providers are available composition, not readiness dependencies. Once a vector
provider is actually selected by a vector operation, it becomes critical and its probe/correction is reported. An
explicitly required provider that cannot be elected or reached fails loudly; Koan does not silently substitute a
weaker guarantee.

## Complete intent surface

- References: Web, Jobs, Tenancy, Media Web, local storage, SQLite, identity/access, plus clearly optional AI/vector
  connectors retained by the complete application.
- Code: the four-line host, domain Entities/jobs, thin controllers, media recipes, and one application module.
- Decorations: existing `[StorageBinding]`, `[Embedding]`, `[AccessScoped]`, `[Parent]`, and `[HostScoped]` declarations.
- Configuration: checked-in SQLite/local-storage defaults; external providers only in explicit optional/production
  configuration whose prerequisites are documented and tested to the level claimed.
- Context: Development supplies one safe local studio/operator posture; production remains fail-closed.
- Runtime prerequisites: writable checkout for the local path; nothing else. Optional paths name their own services.

No application repository, `DbContext`, migration command, provider switch, tenant filter, queue, hub, or middleware
registration is added to the common path.

## Public concepts

- `PhotoAsset` is the stored original and the Entity-centered media/analysis surface.
- `Event` is the studio album and auto-organization boundary.
- `PhotoProcessingJob` makes ingest durable and carries ambient studio context.
- `GalleryGrant` expresses the client-sharing business decision rather than a second tenant axis.
- `ProofSelection` keeps client choices attributed to the guest instead of mutating studio ratings.
- AI analysis and semantic search are optional enrichments; they do not redefine whether a photo vault works.

No new application abstraction is justified merely to wrap those concepts. The two one-implementation service
interfaces and the historical phase vocabulary are candidates for deletion during the code-readability pass.

## Evidence read

### Documentation

- `docs/engineering/index.md` requires Entity-first data, controller-owned HTTP, centralized constants/options,
  focused tests, and current companion docs; it governs the graduation edits.
- `docs/architecture/principles.md` establishes business-to-code mapping, local-first defaults, provider honesty,
  one compiled composition, and thin hot paths; it rejects the current external-first default.
- `README.md` defines Koan's V0-to-V1 promise and one inspectable composition; SnapVault must demonstrate rather
  than complicate that promise.
- `samples/README.md` requires one business sentence, one standard command, focused proof, and truthful prerequisites.
- `DX-0046` is retained as a dated feature record; its old S6 names, five-week roadmap, SLAs, and absent APIs are not
  current product curriculum.
- `snapvault-product-spec.md`, `snapvault-ui-api-contract.md`, `snapvault-delight-research.md`, and the tenancy/
  modernization plans preserve valuable domain invariants and rebuild history, but contain superseded and partly
  contradictory status. Current public instruction must move to the sample README; ADR/history remains untouched.

### Code and tests

- `Program.cs` has the right `AddKoan()` center but retains duplicate static-file setup, `AppHost.Current` ceremony,
  and historical comments; keep the host semantics and delete any framework-owned ceremony proved unnecessary.
- `SnapVaultModule` is the correct application composition owner; retain its seeding/media/service responsibilities,
  make reporting current, and remove phase-number narration.
- `PhotoAsset` correctly centralizes storage, embeddings, relationships, and access scoping; preserve its tenant and
  event-scope invariants while rewriting comments as greenfield contract.
- `PhotoProcessingService` contains genuine domain work (unique blob keys, EXIF, UTC daily events, analysis
  normalization, hybrid fallback, reroll-with-holds) but also historical narration and two unearned interfaces.
- `PhotosController` is controller-owned and Entity-first, but seven hand-written 405 overrides expose a future
  reusable read-only Entity-controller opportunity. Do not lift it without a second semantic consumer in this slice.
- `SnapVaultTenancyFlagshipSpec`, `SnapVaultIngestSpec`, and `SnapVaultGuestLifecycleSpec` prove the valuable core
  under one real `AddKoan()` host with no Docker. The suite passes 33/33; it does not prove the checked-in default
  web-host configuration or shortest HTTP/UI path.

## Existing constants, options, and contracts

- Already exists: `CollectionOptions`, media recipe names, progress-stage constants, job action names, photo-set and
  mutation wire contracts, guest lifecycle Entities/services, and the complete focused behavior suite.
- Needs central ownership: upload/file limits and allowed extensions currently live in `PhotosController`; byte-unit
  constants live in `MaintenanceController`; stable analysis model/source names are repeated in attributes/service code.
- Needs to be created: a Vector-pillar participation contract/ledger and shared vector health base; one checked-in
  local source configuration; one default-host/HTTP graduation proof; concise current README/requests.
- Does not need to be created: another repository, photo workflow facade, tenant resolver, queue, progress store,
  derivative Entity type, provider selector, or sample-specific health system.

## Coalescence decision

- Closest golden application: `OrderIntake`—local control plane, optional providers, participation-owned readiness,
  exact correction, and one focused host proof. Reuse the law, not its workload vocabulary.
- Closest application domain proof: SnapVault's own 33-test suite. Preserve the real domain invariants and delete the
  migration diary from current code/docs.
- Current decision owner: vector connector health contributors each claim `IsCritical=true` by package presence,
  while `VectorService` owns provider election and repository lifetime. This splits one decision across four places.
- Chosen specificity: Vector pillar. Core's `ProviderCatalog` already owns generic election law; Vector must own when
  an elected provider becomes an application dependency. Connector contributors own only backend probes.
- Disposition: keep the domain core and existing access/job/media chokepoints; rebuild first-use composition and
  public perimeter; absorb vector participation/readiness into one pillar base; delete unsupported launcher/deployment
  claims, duplicate interfaces/ceremony when proved unneeded, magic literals, and historical phase narration.
- Target owner: `Koan.Data.Vector` marks provider/source participation before repository construction and exposes
  one shared health policy to Qdrant, Weaviate, and Milvus. The application never decides connector criticality.
- State lifetime/hot path: provider candidates and the default election compile once; participation is a host-owned
  monotonic set; repository resolution marks once per `(provider, source)`; health snapshots read that set without
  rescanning assemblies or renegotiating providers.
- Wider owner rejected: Core cannot decide that a Vector availability check or repository creation constitutes a
  readiness dependency. Narrower owner rejected: each connector cannot know whether it was selected.

## Ergonomics

Humans read upload → durable ingest → album → share → proof. IntelliSense stays on Entity, Jobs, media, and access
semantics rather than infrastructure setup. A coding model can infer the same business arc from type names and one
README command. The default has no provider branch. Optional AI/vector behavior adds one explicit prerequisite and
one visible runtime decision; it does not change the application grammar.

## Code placement

| New or changed code | Location | Justification |
|---|---|---|
| vector participation contract | `src/Koan.Data.Vector.Abstractions/` | cross-module vocabulary consumed by the pillar and connectors |
| vector participation ledger and health base | `src/Koan.Data.Vector/` | one pillar owner for activation/readiness meaning |
| Qdrant/Weaviate/Milvus probes | their connector projects | backend mechanics remain adapter-owned and thin |
| vector participation proof | focused Data/Vector test project | proves inactive, active, failed-first-use, and exact-provider behavior |
| local source/storage defaults and launch profile | `samples/applications/SnapVault/` | standard .NET first-use composition |
| application constants | `samples/applications/SnapVault/Infrastructure/` | stable business/runtime identifiers have one owner |
| cumulative web-host proof | `tests/Suites/Samples/Koan.Samples.SnapVault.Tests/` | checked-in defaults, HTTP, readiness/facts, ingest, and clean stop |
| public instructions and API exercise | sample `README.md` and `requests.http` | one current developer/operator path |

## Constraints satisfied

- Entity statics remain the data language; no repository facade is introduced.
- HTTP remains attribute-routed controllers; the existing SPA fallback is the standard ASP.NET Core non-API shell
  exception and will be made explicit rather than expanded with inline business endpoints.
- Stable identifiers move to application/pillar constants; tunable behavior uses existing typed options.
- No unbounded new data path is added; existing full-library materializations remain a separately visible scale
  limitation unless this graduation touches them directly.
- Contracts consumed by connectors live in `Koan.Data.Vector.Abstractions`; functional activation stays out of the
  contract project.
- Public docs, facts, health, errors, tests, sample index, and lockfile will agree before graduation.

## Execution plan

1. Coalesce Vector readiness around runtime participation; migrate Qdrant, Weaviate, and Milvus and prove inactive
   availability versus active dependency without a release-certification run.
2. Make the checked-in SnapVault Development composition local-first with SQLite and local storage; retain external
   connectors only as explicit optional composition and delete any unproved launcher/deployment path.
3. Add one cumulative default-host proof: initial readiness/facts, real JPEG upload through HTTP and durable Jobs,
   stored/served photo, event creation, optional AI degradation, and clean shutdown.
4. Close the studio-to-client arc at the smallest reliable surface: explicit known-person grant, event-scoped
   gallery/proofing, cross-event denial, and an integrity-checked access-closure record. Reuse existing proof rather
   than duplicate it.
5. Rewrite current sample code comments and README as greenfield intent; centralize live constants; remove unearned
   interfaces/ceremony and stale deployment artifacts only where focused compile/proof confirms deletion.
6. Run the focused Vector owner tests, SnapVault 33-test suite plus the new cumulative cell, strict sample build,
   docs/public truth gates, diff/privacy checks, then add SnapVault to the public complete-application table.

## Outcome

SnapVault is graduated public curriculum. Its checked-in Development composition boots in about one second with
SQLite and local storage, requires no external service, and presents one meaningful browser path from upload to a
durably processed and served photo. AI and vector semantics remain in the business code while provider mechanisms
are opt-in by reference; an unavailable enrichment never invalidates the photo-studio result.

The application perimeter is smaller and more truthful: external-first provider references, Docker launchers,
production-shaped configuration, duplicate host ceremony, unsupported HEIC claims, three stale request fragments,
and two one-implementation service interfaces are gone. One current README and `requests.http` explain the local
result, optional enrichment, runtime inspection, and production boundary. Current source comments describe domain
contracts rather than the rebuild sequence.

The cumulative TestServer proof uses the same SQLite/local-storage shape as the application, uploads a generated
JPEG over real multipart HTTP, drains its durable Job, verifies the resulting event and photo, serves original and
gallery media, and checks readiness and facts. The existing tenant, guest, proofing, mutation, progress, blob, media,
AI/vector-degradation, and cleanup contracts remain green under that consolidated host.

## Verification

- Focused tests: Vector participation/health owner cells; SnapVault cumulative default-host cell; existing SnapVault
  suite (34/34 including the new cell, zero skips).
- Broader regression: only connector builds/tests directly affected by the shared Vector health base.
- Documentation: public-doc truth gate, docs lint with zero new errors, sample README/request/source agreement.
- Manual/observable proof: one `dotnet run` to local ready state and one photo ingest; optional browser proofing only
  if it adds evidence not already covered by the HTTP/contract suite.
- Privacy: no private dogfood name, machine path, credential, personal identity, or external application reference.

## Verification result

- Vector participation/readiness proof passed 4/4; Qdrant, Weaviate, and Milvus strict connector builds passed.
- SnapVault strict Release build passed with zero warnings and zero errors.
- SnapVault passed 34/34 in one consolidated real Koan host with SQLite and in-memory vector isolation.
- Manual default startup reached healthy readiness and complete facts in about one second; the actual process stopped
  cleanly after the local path was exercised.
- Public documentation truth passed across 174 current files and 36 navigation targets. Structural docs lint reports
  zero errors; the repository's existing warning backlog remains non-gating.
- `git diff --check` and the privacy boundary passed; no ADR, package, tag, release, branch, or remote setting changed.

R10-10 therefore passes. Assess `CustomerCanon` next using the same graduate-or-remove standard.

## Stop conditions

- If SQLite cannot support the existing tenancy/job/media guarantees, stop and select another local provider rather
  than weakening the guarantee.
- If AI/vector modules cannot remain inactive without application-specific disable switches, fix the owning pillar
  activation law before documenting a local-first claim.
- If the client flow requires external identity infrastructure in Development, narrow the public first result to
  local ingest and keep the already-proved lifecycle as an advanced contract; do not invent a fake production claim.
- Do not publish, push, tag, release, or mutate remote configuration in this card.
