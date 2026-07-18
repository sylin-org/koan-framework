---
type: SPEC
domain: framework
title: "R11-05 - Graduate Package Families"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: source-first
validation:
  date_last_tested: 2026-07-17
  status: in-progress
  scope: dependency-ordered family assessment, coalescence, package prose, and focused consumer evidence
---

# R11-05 — Graduate package families

- Tranche: `T7B — package-product graduation`
- Status: `in-progress`
- Depends on: R11-04 golden package journey
- Unlocks: final packed rendering and complete release boundary

## Application intent

> Every package beyond the front door states one distinct capability addition, activates only that intent, and
> explains its smallest useful result and honest limits without requiring framework-internal knowledge.

## Method

Work in dependency order. Before editing package prose, each family receives a focused discovery pass for duplicated
mechanisms, misplaced dependencies, contract/function leakage, and opportunities to centralize policy at the concern
owner. A package is documented only after its boundary earns `keep`; a failed boundary is merged, split, renamed, or
retired first.

Each family slice updates the exact R11-02 disposition, package-specific metadata/docs, focused behavior evidence,
and generated quality report. It does not run the complete release ratchet.

## Family queue

| Family | Scope | Status |
|---|---|---|
| foundation runtime | Core, Data contracts/runtime, JSON, SQLite, Web, Communication | passed |
| contract isolation | ZenGarden, AI contracts, Vector/Media/Storage abstractions, Orchestration CLI contracts | passed |
| provider families | Data, Vector, AI, Cache, Storage, Auth, Orchestration providers | Storage passed; remaining pending |
| semantic capabilities | Jobs, MCP, AI, Cache, Tenancy, Identity, Canon, Media, Classification, Security | pending |
| projections and tools | Web add-ons, testing, analyzers, generators, CLI and operator surfaces | pending |

## Foundation discovery

The package boundary review retains all seven foundation-runtime packages because each states distinct reference
intent. It also found one responsibility leak: `Data.Abstractions` referenced ASP.NET Core JSON Patch and exposed a
legacy object-shaped `PatchRequest`, while the current REST/MCP path already normalizes into canonical `PatchPayload`.

The repair removes the unused legacy request/applicator path, leaves RFC/media-type normalization in Web, and leaves
Data with one provider-neutral patch operation model. Data adapter and module authors no longer inherit HTTP patch
machinery merely to consume Entity and repository contracts.

The package review also corrected the JSON floor's corruption posture. Writes now produce a complete adjacent snapshot
before replacing the active file, cancellation is honored at the file boundary, and invalid persisted JSON throws a
corrective .NET `InvalidDataException` instead of being silently interpreted as an empty store. The package page states
its single-process, whole-file, non-transactional boundary explicitly.

## Foundation evidence

- terminal dispositions: seven `keep`; each represents a distinct composition, contract, runtime, provider, or
  projection intent;
- focused builds: Core, Data Abstractions, Data Core, JSON, SQLite, Web, and Communication — zero warnings/errors;
- patch normalization: `Koan.Web.PatchOps.Tests` 14/14 and Web JSON patch surface 5/5;
- JSON provider: connector suite 21/21, including fail-loud corrupt-store coverage;
- package artifacts: all seven Debug evidence nupkgs packed successfully and contain package-owned `README.md` plus
  canonical `icon.png` bytes; artifacts remain local evidence, not release candidates;
- generated quality: 108 packages, 36 repair-required, 57 review-required, 15 structurally ready, 231 findings; all
  seven foundation-family packages have zero structural findings;
- generated product surface: 15 claims and 108 packages agree with the evaluated graph.

This passes the foundation family only. It is not a release certification and does not promote `structurally-ready`
into a support claim.

## Contract-isolation progress

- `Sylin.Koan.ZenGarden.Core` was renamed to `Sylin.Koan.ZenGarden.Contracts`; reusable endpoint, initialization,
  tool, and capability vocabulary now states its inert role directly and functional activation remains in
  `Sylin.Koan.ZenGarden`.
- `Sylin.Koan.Orchestration.Cli.Core` was dissolved. Its public package claim was false—the containing types were
  internal and consumed only by the executable—so planning and command mechanics now live with their only runtime
  owner in `Sylin.Koan.Orchestration.Cli`. The retained Abstractions package remains the independent provider/exporter
  SPI.
- focused CLI proof: restored dependency graph, warning-free Release build, executable `--help`, and a clean tool
  package containing the owned README and canonical icon; the quality compiler now recognizes standard
  `dotnet tool install --global` first-use instructions.
- generated truth after the CLI merge: 107 packages, 34 repair-required, 56 review-required, 17 structurally ready,
  and 223 findings. The dissolved false boundary removes one package and its two findings; the retained CLI advances
  to structurally ready.
- `Sylin.Koan.AI.Contracts` and `.Contracts.Shared` both earn `keep`: the former is the inert provider/inference
  boundary and the latter is a dependency-free lifecycle exchange boundary retained by accepted cross-repository
  architecture. `AiCapability`, `IAiRecipeProvider`, and `IAiModelAdvisor` moved out of mandatory Core into the AI
  contract they describe; AI Contracts no longer has a false Core dependency. ZenGarden can describe its optional
  advisor through that inert boundary, while only an active AI runtime resolves and uses it.
- focused AI proof: Core, AI Contracts, AI runtime, ZenGarden, Ollama, and LM Studio build warning-free; AI unit
  160/160, integration 49/49, end-to-end 31/31, and ZenGarden 86/86 pass. Both contract packages pack with owned
  README and canonical icon.
- generated truth after AI contract graduation: 107 packages, 33 repair-required, 55 review-required, 19
  structurally ready, and 220 findings. Both retained contract packages now have zero structural findings.
- `Sylin.Koan.Data.Vector.Abstractions` now contains only the provider/query/schema SPI. Runtime provider election,
  per-host memoization, dependency injection, physical naming, and segmentation moved to `Sylin.Koan.Data.Vector`;
  the contracts package consequently depends only on `Sylin.Koan.Data.Abstractions`.
- `Sylin.Koan.Data.Vector` earns `keep` as the one runtime owner for entity-first facades, provider selection,
  repository caching, isolation-aware naming, participation, and health. Its package page now documents the real
  `Vector<TEntity>` surface and coordination limits instead of the former nonexistent profile/workflow examples.
- focused Vector proof: Abstractions, runtime, SearchEngine, Qdrant, Milvus, Weaviate, OpenSearch, Elasticsearch,
  and SQL Server build warning-free; Data Core vector specs pass 58/58 and the zero-infrastructure in-memory
  conformance suite passes 33/33. Synthetic adapter hosts receive the neutral segmentation realization through one
  shared test-kit chokepoint; production composition remains owned by Data Core.
- generated truth after Vector contract graduation: 107 packages, 33 repair-required, 53 review-required, 21
  structurally ready, and 216 findings. Both retained Vector packages now have zero structural findings.

### Storage and Media discovery

**Application intent:** a module or provider author can consume Storage/Media vocabulary without activating either
engine, while an application still expresses an Entity-backed media model as `Photo : MediaEntity<Photo>`.

**Complete expression:** provider authors reference `Sylin.Koan.Storage.Abstractions`; ordinary applications
reference Storage/Media functional packages or their higher bundles. `MediaEntity<TEntity>` moves to the application
namespace `Koan.Media`; no new startup call, decoration, configuration, context, or runtime prerequisite is added.

**Guarantee and correction:** contract-only references register nothing. Storage remains the owner of routing,
segmentation, provider IO, and object lifecycle; Media Core remains the owner of Entity-backed upload/dedup/read,
recipe discovery, and pipeline execution. A missing runtime or provider continues to fail at the owning functional
chokepoint rather than being simulated by a contracts assembly.

**Coalescence decision:** create one necessary `Sylin.Koan.Storage.Abstractions` boundary from the SPI already embedded
in Storage, including the service result and binding vocabulary needed across modules. Keep Storage as the runtime;
rebuild Media Abstractions as inert media/recipe vocabulary; absorb `MediaEntity<TEntity>` into Media Core; remove the
AI runtime's unused Storage and Media references. No compatibility shim or parallel namespace survives.

**Ergonomics:** application code reads `using Koan.Media; public sealed class Photo : MediaEntity<Photo>;` instead of
importing an `Abstractions.Model` namespace to obtain functional behavior. Package names, IntelliSense, runtime
activation, and agent-readable dependency intent consequently agree.

### Storage and Media evidence

- `Sylin.Koan.Storage.Abstractions` is the new inert boundary for the existing service/provider/object/binding SPI;
  its packed dependency graph contains only `Sylin.Koan.Data.Abstractions`. Storage runtime, both providers, Data
  Backup, and Media consumers now declare the exact contract dependency they compile against.
- `MediaEntity<TEntity>` moved from Media Abstractions to Media Core and the application namespace `Koan.Media`.
  Media Abstractions now depends only on Data/Storage contracts; AI dropped two unused functional dependencies.
- focused builds: Storage contracts/runtime, Local, S3, Data Backup, Media contracts/runtime/Web, AI, and the
  SnapVault dogfood application compile. The Data Backup build's stale restored `Core.Adapters` warning disappears
  after graph restore and is not a source dependency.
- focused behavior: Storage Core 3/3, Media Core 562/562, Media Web 4/4, and Storage tenant-isolation 12/12 pass.
  The legacy Local connector test project still targets obsolete option/profile/capability shapes and is assigned to
  the provider-family repair; the shipping provider itself builds warning-free. This is not concealed as provider
  graduation.
- package artifacts: Storage Abstractions, Storage, Media Abstractions, and Media Core pack with owned README and
  canonical icon; nuspec inspection confirms the two contracts packages do not depend on functional assemblies.
- package identity: the new Storage Abstractions owner deliberately starts at compatibility tier `0.18`; NBGV
  preview and public pack both produce `0.18.0`. The package-creation guide now requires this preview so a repeated
  inherited minor cannot silently preserve unrelated repository height.
- generated truth: 108 packages, 33 repair-required, 50 review-required, 25 structurally ready, and 208 findings.
  All four retained Storage/Media boundaries in this slice have zero structural findings.

The contract-isolation family passes. Provider prose and the stale Local connector suite remain explicit work in the
next family; no full release certification ran here.

### Storage provider-family discovery

**Task:** graduate the Local and S3 provider packages by rebuilding Storage provider description, election, routing,
proof, and prose around one compiled pillar plan.

**Application intent:** “Store this Entity-backed object locally or in S3; when I reference both providers, compose
the declared Storage mode without vendor-specific application code.”

**Public expression:** reference `Sylin.Koan.Storage.Connector.Local` or
`Sylin.Koan.Storage.Connector.S3`, call the application's existing `AddKoan()`, declare a
`StorageEntity<TEntity>` (optionally with `[StorageBinding]`), and configure the profile/container plus the selected
provider's physical connection settings. No Storage-specific registration call is part of the supported path.

**Guarantee/correction:** an exact provider pin wins; otherwise Storage deterministically elects the highest-priority
provider for the profile's Local/Remote placement and compiles each profile once. A sole profile is the implicit
default; several profiles require `DefaultProfile` or an explicit operation/binding profile. Unknown pins, ambiguous
defaults, absent providers, invalid containers, and explicitly requested replication without both placements fail
with a corrective error instead of weakening intent. Capability claims and optional provider interfaces must agree.

**Complete intent surface:** package reference, `AddKoan()`, Entity/binding code, logical profile/container, and
backend connection settings are the complete surface. Provider authors additionally declare stable `Name`,
`StorageProviderPlacement`, unified `StorageCaps`, and optional `[ProviderPriority]`; they implement no election,
routing, reporting, or manual-registration machinery.

**Public concepts:** `StorageCaps` expresses backend guarantees through the framework-wide capability vocabulary;
`StorageProviderPlacement` supplies the Local/Remote fact required by automatic topology; existing
`ProviderPriorityAttribute` expresses deterministic fallback rank. `StorageFallbackMode`, `ValidateOnStart`, the
boolean `StorageProviderCapabilities` bag, and manual Storage/provider DI extensions do not represent necessary
application decisions and will not survive.

**Docs read:** `architecture/principles.md` requires business-first APIs, one composition kernel, and compiled hot
paths; ARCH-0084 explicitly assigns Storage to unified capability tokens; ARCH-0115 assigns generic election mechanics
to Core and routing meaning to Storage; the Storage package pages establish profile, segmentation, transfer, and
provider boundaries; STOR-0005 preserves Local filesystem safety requirements; the quality report identifies Local's
weak package expression and S3's missing owned docs.

**Code read:** `StorageService` currently mixes object operations with profile validation, name-heuristic placement,
selection, replication construction, and reload-shaped options access; `StorageModule` owns the supported activation
path; Local and S3 providers own physical IO but duplicate registration/configuration helpers; the stale Local suite
tests former shapes rather than current auto-detection; `ProviderCatalog<T>` and Cache/Data selectors are the closest
shared pattern for normalized identity, priority, stable ties, and selection receipts.

**Reusing:** Core `ProviderCatalog<TProvider>`, `ProviderMetadata`, `ProviderSelectionReceipt`, unified
`CapabilitySet`/`IDescribesCapabilities`, standard .NET Options/DI, Storage's existing `IStorageService` segmentation
decorator, and provider optional-operation interfaces already exist.

**Creating new:** `StorageCaps` and `StorageProviderPlacement` live in `Koan.Storage.Abstractions` because provider
authors and the runtime share them; `StorageProviderCatalog`, `StorageRoutingPlan`, and their immutable route records
live under `Koan.Storage/Routing` because Storage owns placement and profile policy; `StorageCompositionFacts` lives
under `Koan.Storage/Composition` so startup/facts project the compiled authority; S3 receives package-owned README and
TECHNICAL documents. No application-facing registration type is added.

**Coalescence:** closest patterns are Core `ProviderCatalog<T>`, Data's host catalog, and Cache's placement resolver.
Disposition: keep the Core catalog mechanics; rebuild Storage selection around them; absorb validation, defaulting,
placement election, replication composition, and receipts into one immutable Storage routing plan; keep
`StorageService` as a thin operation executor; delete the provider-name heuristic, boolean capability record,
per-operation routing negotiation, duplicate manual DI extensions, duplicate S3 options configurator, and stale test
model. Core is too wide for Storage modes; an adapter is too narrow to select peers or defaults.

**Ergonomics:** application code gains no new branch and loses unsupported setup paths. IntelliSense gives provider
authors one capability catalog and one placement fact. Humans and coding models see the same reference → profile →
meaningful Entity operation story; startup, facts, errors, and tests read from the same compiled routes.

**Constraints satisfied:** no HTTP surface; no data scan; constants/options remain at their current owners; runtime
routing compiles once; provider IO stays adapter-local; package docs and generated reference truth update in the same
slice; only focused Storage tests/builds/packs run before R11-07.

**Risks:** S3's ZenGarden discovery is lazy, so capability support must describe the adapter mechanism while health
and first-use errors describe endpoint readiness; replication owns background resources and therefore the compiled
plan must dispose composites with the host; changing profile options after host composition is intentionally not a
supported hidden reload path.

### Storage provider-family evidence

- Storage now has one immutable routing authority. Core's generic `ProviderCatalog<T>` owns normalized identity,
  priority, and stable ties; `StorageProviderCatalog` owns placement/capability qualification; `StorageRoutingPlan`
  owns profile validation, defaulting, exact pins, election receipts, and replicated composition. `StorageService`
  is consequently a thin IO/transfer executor and performs no runtime provider negotiation.
- Local and S3 declare only stable identity, placement, unified capability tokens, priority, and physical IO.
  Replicated routes preserve automatic versus required intent, explicit replication fails rather than degrades, and
  partially compiled composites are disposed if a later startup correction rejects the plan.
- Deleted concepts have no replacement ceremony: the provider-name heuristic, boolean capability bag,
  `StorageFallbackMode`, `ValidateOnStart`, manual Storage/provider registration extensions, duplicate S3 options
  configurator, per-operation route compilation, and the obsolete 335-line Local test model are gone.
- Focused behavior passes: Storage Core 20/20; Local filesystem 24/24 including container/key traversal rejection;
  Tenancy Storage isolation 12/12; Media Web startup 4/4; Backup Storage acceptance 5/5; and the real `AddKoan()`
  Storage bootstrap/facts contract 1/1. SnapVault, the current Local/Media dogfood application, builds warning-free.
- Storage, Local, and S3 Release builds complete with zero warnings/errors. The integration bootstrap retains a known
  stale test-project reference warning for the already retired `Koan.Core.Adapters`; its executable Storage contract
  passes and this slice does not conceal or broaden that unrelated fixture debt.
- Local `0.17.2` and S3 `0.17.3` evidence packages contain package-owned README, canonical icon, symbols, and the
  expected functional/contract dependencies. Generated truth contains 108 packages: 32 repair-required,
  49 review-required, 27 structurally ready. Both Storage connectors now have zero objective quality findings.
- S3 has warning-clean source and artifact proof plus existing application dogfood, but no hermetic MinIO/S3 engine
  conformance suite in this slice. Its package page therefore states buffering, endpoint readiness, and backend
  semantics conservatively; this evidence graduates package expression, not every compatible S3 implementation.

The Storage provider subfamily passes. The complete release ratchet remains reserved for R11-07.

### Cache and SQLite provider-family discovery

**Task:** graduate the Cache core and persistent-local provider boundary by making its existing public semantics
executable, deleting framework-author ceremony, and refusing to overstate the adjacent Redis package.

**Application intent:** “Cache this Entity for five minutes; if I reference SQLite, keep the local cache across
process restarts, without changing normal Entity reads or writes.”

**Public expression:** reference `Sylin.Koan.Cache`, retain the application's one `AddKoan()`, and declare
`[Cacheable(300)]` on the Entity. Reference `Sylin.Koan.Cache.Adapter.Sqlite` to replace the memory floor with the
higher-priority persistent Local store. Optional host-wide `Koan:Cache:LocalProvider` / `RemoteProvider` pins and
SQLite `DatabasePath` are the only provider-selection/configuration additions. No cache registration belongs in
`Program.cs`.

**Guarantee/correction:** one host-owned topology compiles all referenced stores once using exact pins, placement,
priority, and stable identity. A missing or contradictory pin fails boot with registered choices. `CacheTier` must
actually constrain reads/writes; a requested unavailable tier fails with a correction instead of becoming a no-op.
`AllowStaleFor` means bounded stale serving only—Koan does not claim background revalidation. SQLite tags match
exactly, sliding expiry refreshes when declared, and persistence remains process-local rather than distributed.

**Complete intent surface:** package reference, `AddKoan()`, `[Cacheable(ttlSeconds)]`, and optional host-wide
provider/database configuration are complete. Ordinary Entity operations and `entity.Cache.Evict()` do not add
services, repositories, transport code, or provider branches. Provider authors implement `ICacheStore`, stable
`Name`, placement, unified capability description, optional priority, and standard DI registration in their module.

**Public concepts:** `[Cacheable]` expresses Entity cache intent and TTL; `CacheTier` expresses the meaningful
Local/Remote/Layered operation choice and therefore survives only after it is enforced; `AllowStaleFor` expresses a
bounded stale-read window; host-wide provider pins express infrastructure intent; `CacheCaps` describes provider
guarantees. Per-policy provider pins, `CacheConsistencyMode`, manual `AddKoanCache`, a mutable store registry, and a
Cache-specific DI registration helper/analyzer do not create a real business guarantee and will not survive.

**Docs read:** `docs/engineering/index.md` establishes Entity/controller/constants/package guardrails;
`docs/architecture/principles.md` requires one compiled pillar plan and standard .NET before custom machinery;
`docs/architecture/koan-cache-module.md` assigns topology/policy/coherence meaning to Cache and physical storage to
adapters; `docs/architecture/cache-scope-key-convergence.md` requires repository and explicit eviction to share one
identity plan; accepted ARCH-0075 records the four Cache concerns while its amendments supersede the speculative
coherence surface; current Cache README/TECHNICAL/reference/card state the zero-registration Entity story but
currently overclaim working tier/provider controls and stale revalidation.

**Code read:** `CacheTopologyResolver` already uses Core `ProviderCatalog<T>` but is fed through a second mutable
registry; `CacheServiceCollectionExtensions` exposes unsupported manual setup and registers that duplicate path;
`ICacheStore` carries an unused boolean capability bag; `LayeredCache` always uses every available tier and therefore
ignores public `CacheTier`; `CachePolicyAttribute`/descriptor materialize dead per-policy pins and consistency values;
Memory, SQLite, and Redis implement the same K/V contract, while SQLite uses substring tag matching and exposes an
unused sweep option; the Cache analyzer exists only to require `AddCacheStore<T>` instead of standard two-generic DI.

**Reusing:** Core `ProviderCatalog<T>`, `ProviderSelectionReceipt`, unified `CapabilitySet`/
`IDescribesCapabilities`, standard `TryAddEnumerable(ServiceDescriptor.Singleton<TService,TImplementation>())`,
standard Options/DI/health, existing `CacheIdentityPlan`, `LayeredCache`, Entity cache plan, cache conformance suites,
and SQLite transactions/indexes already provide the required laws or mechanisms.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| `CacheCaps` | `src/Koan.Cache.Abstractions/Capabilities/CacheCaps.cs` | Shared provider/runtime vocabulary belongs in the inert contract package. |
| compiled candidate state inside `CacheTopology` | `src/Koan.Cache/Topology/CacheTopology.cs` | Cache topology is simple enough for one plan; a second public/dynamic catalog owner is unnecessary. |
| SQLite constants | `src/Koan.Cache.Adapter.Sqlite/Infrastructure/Constants.cs` | Stable provider/config/schema identifiers belong to the adapter, not literals across module/store code. |
| Redis constants | `src/Koan.Cache.Adapter.Redis/Infrastructure/Constants.cs` | Stable provider/config/channel identifiers remain adapter-owned while its larger activation boundary is assessed. |
| package companions | Cache Abstractions, SQLite, and Redis project roots | Each retained package must state exact reference intent, first result, and limits. |

**Coalescence:** closest pattern is the newly graduated Storage plan over Core provider law, but Cache needs only one
Local/Remote topology rather than profile routing. Rebuild `CacheTopology` as the single immutable owner; absorb
resolver/catalog projection there; delete `ICacheStoreRegistry`, `CacheStoreRegistry`, manual `AddKoanCache`,
`AddCacheStore<T>`, and the entire Cache analyzer package/test project. Keep LayeredCache as the runtime chokepoint,
but make it enforce the already-public tier decision. Delete dead consistency/per-policy-pin knobs rather than
inventing runtime branches. The next wider owner (Core) cannot decide Local/Remote cache meaning; each adapter is too
narrow to elect peers. Redis's borrowed functional Data owner is not normalized in this slice: it requires a joint
backend-capability decision and remains ungraduated.

**Ergonomics:** human and model code remains `[Cacheable(300)]` plus normal Entity verbs. IntelliSense loses false
provider/consistency knobs and a framework-specific registration helper. Module authors use the standard .NET DI
shape plus one Cache contract; operators and agents see selected stores, capabilities, receipts, exact tier posture,
and honest stale-serving/persistence bounds from the compiled authority.

**Constraints satisfied:** no HTTP route is added; no data scan is introduced; Entity statics remain the application
path; tag enumeration is provider-bounded; stable identifiers move to adapter constants; options retain only working
decisions; README/TECHNICAL/reference/TOC and generated truth update with behavior; ADRs remain unchanged historical
records; focused Cache/SQLite/tenancy/bootstrap/artifact evidence runs without release certification.

**Risks:** SQLite schema migration must preserve existing rows while replacing comma/substring tag semantics; tier
enforcement affects tests that previously asserted only materialization; removing the analyzer is a release-lineage
retirement; Redis's current transitive Data activation is an explicit remaining boundary and prevents Redis package
graduation even if its prose becomes structurally complete.

### Cache and SQLite provider-family evidence

- Cache now has one immutable composition authority. Standard .NET DI supplies `ICacheStore` candidates; Core's
  `ProviderCatalog<T>` owns normalized identity/priority/stable ties; `CacheTopology` owns Local/Remote placement,
  exact host pins, capability sets, election receipts, and one-time selection. The mutable registry and separate
  resolver are deleted.
- `CacheTier` is executable: LocalOnly and RemoteOnly touch only their required route and fail with a correction
  when absent; Layered uses both selected routes when available and gracefully uses the one that exists. Cache
  operations negotiate tags, sliding expiry, bounded stale serving, and binary payloads through unified `CacheCaps`.
- Dead public concepts are gone without aliases: per-policy provider pins, `CacheConsistencyMode`, manual
  `AddKoanCache`, the Cache registration helper, its package-specific analyzer/test project, the mutable registry,
  unused diagnostics/tag-capacity settings, and SQLite's unused sweeper option. One internal subject-aware Cache
  client now keeps Entity facets thin while Cache owns segmentation and physical identity.
- SQLite stores exact tags in a normalized case-insensitive table, serializes compatibility tag data as JSON,
  migrates old rows/schema in place, updates entries and tags transactionally, and genuinely renews sliding expiry.
  Its provider/config identifiers are centralized and its persistence claim remains Local/process-bound.
- Focused behavior passes: Cache Abstractions 51/51; Cache topology 62/62; SQLite persistence/exact-tag/sliding/
  expiry-cleanup behavior 5/5; Memory/SQLite cross-engine contract 14/14; Cache Web 23/23; Entity Cache facet 4/4; and real Redis
  adapter behavior 5/5. The two focused Cache/Tenancy classes are currently blocked before Cache execution by an
  unrelated Local Storage `BasePath` fixture validation and are preserved as PMC-031 rather than hidden or widened.
- All four Cache package projects build, and evidence packages contain their DLL/XML, package-owned README,
  canonical `icon.png`, symbols, and build-transitive package metadata. Generated truth contains 107 packages:
  28 repair-required, 48 review-required, and 31 structurally ready. Cache, Cache Abstractions, SQLite, and Redis
  have zero objective package-quality findings; Redis remains `assess`, not graduated, because its Cache reference
  still activates the functional Data Redis connection owner.

The Cache core/contracts and SQLite provider subfamily pass. Redis behavior and expression are evidenced, but its
package boundary remains open for a joint backend/Data decision. The complete release ratchet remains reserved for
R11-07.

### Redis shared-backend discovery

**Task:** remove Cache Redis's transitive activation of the functional Data Redis connector and give Redis
connection, discovery, orchestration, and host-lifetime pooling one backend-owned chokepoint.

**Application intent:** reference Redis-backed Data, Redis-backed Cache, or both; configure one Redis endpoint once;
retain `AddKoan()` as the only bootstrap expression. A Cache-only application must not acquire a Data provider merely
because both capabilities use the same physical technology. When both adapters are referenced, they must reuse one
default connection and one backend lifecycle.

**Public expression:** applications reference `Sylin.Koan.Data.Connector.Redis` and/or
`Sylin.Koan.Cache.Adapter.Redis`, keep `builder.Services.AddKoan()`, and use the standard
`ConnectionStrings:Redis` key for the common endpoint. Data-only database/page settings remain Data-owned; Cache
key/tag/database/channel settings remain Cache-owned. The shared-backend mechanism adds no application terminal,
attribute, module ID, or manual registration.

**Guarantee/correction:** a Cache-only reference activates Cache plus the Redis backend, never the Redis Data
provider. A Data-only reference activates Data plus the same backend. Referencing both produces one host-owned default
`IConnectionMultiplexer`; named Data sources reuse the same pool when endpoints coincide and receive one connection
per distinct endpoint otherwise. Malformed/unreachable endpoints retain corrective Redis errors. Connection,
discovery, orchestration, adapter participation, and health facts remain attributable to their actual owners.

**Complete intent surface:**

- Cache Redis alone: Redis is eligible as Remote Cache and for layered invalidation, with no Data adapter activation.
- Data Redis alone: Redis is eligible as a Data provider and preserves source/database routing.
- Both adapters: one backend module, default connection, discovery decision, and orchestration contribution are shared.
- Multiple Data sources: explicit endpoints are pooled per host and per distinct connection; logical databases remain
  Data routing, not backend identity.
- Explicit endpoint: `ConnectionStrings:Redis` wins; `Koan:Redis` owns backend-specific discovery controls.
- Automatic endpoint: the backend owns local/container/Aspire discovery and explains the result once.
- Failure: invalid configuration fails with a Redis-specific correction; an unelected Data provider remains
  non-critical through Data's existing participation-aware health policy.
- Removal: removing the last Redis adapter reference removes the backend transitively and leaves no Redis module.

**Public concepts:** no new application concept. Module authors that need source-aware Redis reuse gain one narrow inert
contract, `IRedisConnectionProvider`; ordinary consumers continue to inject StackExchange.Redis's standard
`IConnectionMultiplexer`. `Koan.Redis` is a functional backend package and `Koan.Redis.Abstractions` contains only the
cross-module contract, satisfying the isolated-contract mandate without `Inert` metadata.

**Docs read:** engineering front door; product constitution; architecture principles; adapter/orchestration
registration standard; current Data Redis and Cache Redis README/TECHNICAL companions; documentation TOC; historical
ARCH-0080 only as prior decision context (it remains unchanged).

**Code read:** Data Redis project/module/options/configurator, connection factory and source pool, adapter factory,
health contributor, discovery adapter, orchestration evaluator, constants and provenance; Cache Redis project/module,
options, store, layered Communication adapter and constants; Cache Redis and bootstrap fixtures; Core Data source and
routed-connection resolution; ZenGarden contract isolation as the closest package-boundary precedent; repository
package/version/solution conventions; all current `IConnectionMultiplexer`, Redis configuration, and Redis package
consumers outside generated output.

**Reusing:** standard `IConnectionMultiplexer`; standard .NET DI/options/configuration/lifetime disposal; Koan's
`KoanModule`, service discovery, orchestration, Aspire-resource, provenance, and health contribution seams; Data's
`AdapterConnectionResolver`, `DataSourceRegistry`, provider-aware health, and source/database routing; Cache's compiled
topology and existing Redis store/Communication contracts.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| `IRedisConnectionProvider` | `src/Koan.Redis.Abstractions` | One inert cross-module seam is required to share the default connection and source-aware pool without exposing a functional adapter as vocabulary. |
| Redis backend module/options/provider/discovery/orchestration | `src/Koan.Redis` | Redis lifecycle and endpoint resolution currently sit incorrectly inside Data; two independent consumers prove a backend owner exists. |
| `IKoanAspireResources` isolation | `src/Koan.Orchestration.Aspire.Abstractions` | Redis exposed that the Aspire contribution contract still lived in a functional module; isolate it once so Redis/Postgres contributors do not activate the Aspire runtime merely to describe resources. |
| focused backend-boundary evidence | Redis/Cache/Data focused suites | Proves Cache-only activation, shared default identity, source pooling, and retained adapter behavior without a release certification run. |

**Coalescence:** move connection construction, default/source pooling, endpoint configuration, discovery,
`KoanService` metadata, orchestration, Aspire resource declaration, and their provenance from Data Redis into one Redis
backend. Data Redis becomes a thin Data adapter: repository mechanics, database/source routing, Data capabilities, and
provider-aware health. Cache Redis remains a thin Cache/Communication adapter. Delete Data's private duplicate
connection owner and Cache's borrowed-owner prose. A Core abstraction is rejected because Core must not depend on
StackExchange.Redis; duplicate `TryAddSingleton` registrations are rejected because registration order would become
a second ownership policy; a public helper in either functional adapter is rejected by the isolated-contract mandate.
The same mandate moves `IKoanAspireResources` out of the functional Aspire runtime; Redis and Postgres then reference
inert Aspire vocabulary while only an AppHost that intentionally references the runtime activates it.

**Ergonomics:** the human and model sentence becomes “reference Redis Cache/Data, configure Redis once, use the normal
Entity/Cache surface.” IntelliSense gains no new application ceremony. Module developers use one purposeful provider
contract only when they need multiple Redis sources; simple consumers use the ecosystem-standard multiplexer. Operators
and agents see Redis endpoint/lifecycle once, then Data and Cache participation separately, so physical infrastructure
and semantic capability are no longer conflated.

**Constraints satisfied:** standard .NET carries connection identity; contracts are isolated and inert; functional
activation remains reference-driven; no legacy alias is taught in current docs; no `Inert` project metadata or custom
module identity is introduced; stable constants and typed options own configuration; no Entity/HTTP surface changes;
ADRs remain historical; only focused builds/tests/packages run until R11-07.

**Risks:** moving the canonical key from `Koan:Data:Redis:ConnectionString` to `ConnectionStrings:Redis` requires a
current-doc/test sweep; source pooling must not eagerly connect the default endpoint when only an explicit named source
is used; Aspire/discovery must not be registered twice; Cache topology currently constructs eligible stores during
composition, so a referenced Redis Remote candidate can still resolve its connection at startup; generated package
truth grows by three packages and must classify them honestly rather than declaring graduation from structure alone.

### Redis shared-backend evidence

- `Sylin.Koan.Redis` is now the only functional owner of Redis endpoint configuration, autonomous discovery,
  orchestration/Aspire contribution, connection creation, pooling, and host-lifetime disposal. It publishes the
  standard `IConnectionMultiplexer` for ordinary consumers and the narrow `IRedisConnectionProvider` only for
  source-aware modules.
- Data Redis is thin: it owns repository mechanics, logical database/source routing, Data capabilities, and
  provider-aware health. Its private connection factory, source pool, discovery adapter, orchestration evaluator,
  backend configuration, and `KoanService` ownership are deleted.
- Cache Redis is thin: it owns Remote Cache storage and layered invalidation semantics, references the shared backend,
  and no longer references or activates the functional Data Redis package. A focused boundary spec proves a
  Cache-only host resolves Redis while exposing no Redis `IDataAdapterFactory`.
- `IKoanAspireResources` now lives in the inert `Sylin.Koan.Orchestration.Aspire.Abstractions` package. Redis and
  Postgres can describe resources without activating the functional Aspire runtime; the runtime alone discovers and
  executes contributors. No `Inert` metadata or custom module identity was introduced.
- The supported application expression is one adapter reference, the existing `AddKoan()`, and optionally
  `ConnectionStrings:Redis`. Current tests and public docs no longer teach the Data-owned connection key. Data and
  Cache options remain under their own concern-specific sections.
- Focused behavior passes: Cache Redis 6/6, including Cache-only activation; Data Redis 12/12, including source and
  shared-default routing. The new and affected Redis/Aspire/Postgres projects build warning-clean.
- Seven focused packages contain their DLL/XML, package-owned README, canonical `icon.png`, symbols, and
  build-transitive composition metadata. Their nuspec dependency graph carries the shared backend/contracts without
  pulling Data into Cache or functional Aspire into resource contributors. The three new packages have no vulnerable
  direct or transitive dependencies in the current NuGet audit.
- Generated truth now contains 110 packages: 28 repair-required, 48 review-required, and 34 structurally ready. The
  new Redis backend, Redis contracts, and Aspire contracts have zero objective package-quality findings; product
  surface truth describes all three without promoting an unevidenced public claim.

The Redis backend, Data provider, Cache provider, and Aspire contribution-contract boundaries pass this family
slice. The complete release ratchet remains reserved for R11-07.

### Relational provider-family discovery

**Task:** graduate the relational Data family by isolating cross-module contracts, making schema governance
provider/source-specific, removing provider-to-provider activation, and retaining only shared mechanics that have
identical meaning and lifecycle.

**Application intent:** “Reference PostgreSQL, SQL Server, SQLite, or CockroachDB; define an Entity; use normal Entity
verbs. If I configure schema governance, it applies only to the selected provider/source, and adding another connector
does not activate a sibling provider or change an existing database's DDL decisions.”

**Public expression:** reference one relational connector, retain the application's single `AddKoan()`, and define the
Entity. With one eligible provider no relational registration or repository appears in application code. Pin
`[DataAdapter("postgres")]` or `Koan:Data:Sources:Default:Adapter` only when durability must remain stable as references
grow; configure `ConnectionStrings:<Provider>` when discovery is not appropriate and the provider's existing
`DdlPolicy` / `SchemaMatchingMode` only when overriding safe defaults.

**Guarantee/correction:** each selected provider/source carries its own endpoint, schema, DDL policy, matching mode,
projection mechanics, and production guard. A Cockroach reference must not activate PostgreSQL. Multiple referenced
relational providers must not mutate one global schema-options object. Missing providers, invalid sources, strict
schema mismatches, or forbidden production DDL reject with the existing corrective Data/relational failure rather
than silently selecting, provisioning, or weakening another route.

**Complete intent surface:** package reference, `AddKoan()`, Entity definition, and runtime backend are sufficient.
Provider/source pinning, connection configuration, and DDL/matching overrides are optional business/operations
decisions. There is no application-facing `AddRelationalOrchestration`, global relational materialization section,
repository, Npgsql helper, module identity, or provider bridge. The unevidenced `RelationalStorageShape` decoration is
not part of the supported expression.

**Public concepts:** Entity verbs express persistence intent; Data provider/source selection expresses physical
placement; provider-local `DdlPolicy` expresses whether Koan may validate/create schema; provider-local
`SchemaMatchingMode` expresses whether a mismatch is corrective. Relational projection mode, DDL executors,
store features, and Npgsql repository options are module-author mechanics, not application decisions. The unused
Dapper helper package and the unproved Entity storage-shape decoration do not earn separate concepts.

**Docs read:** `docs/engineering/index.md` requires Entity-first access, project-scoped constants/options, package
companions, and focused validation; `docs/architecture/principles.md` requires contracts to be inert, pillars to own
meaning, adapters to stay thin, and composition to compile once; `docs/engineering/adding-a-connector.md` establishes
reference-driven modules, real integration evidence, and shared-backend ownership but still contains one stale
typed-helper citation to correct; `docs/reference/data/index.md` limits provider parity claims and defines exact
selection/correction order; `docs/reference/data/adapter-diagnostics.md` describes the intended shared diagnostics
posture but is stale about mutable augmenters and must not be treated as current runtime truth. Historical DATA-0052
and DATA-0053 preserve the still-valid separation of adapter SQL mechanics from provider-neutral Direct access.

**Code read:** `RelationalModule` is a functional module that currently registers nothing while every adapter calls a
public registration helper; `RelationalSchemaOrchestrator` owns useful schema mechanics but reads one global
`IOptionsMonitor<RelationalMaterializationOptions>`, contains unused cache/debug state, and accepts no provider/source
policy; PostgreSQL, SQL Server, SQLite, and Cockroach modules duplicate bridge registration into that global object;
PostgreSQL and SQL Server repositories are large dialect-specific implementations, while Cockroach is a genuinely
thin PostgreSQL-wire delta implemented through a functional Postgres reference plus `InternalsVisibleTo`; the
`Koan.Data.Relational.Dapper` package has no consumer anywhere in source, tests, or samples.

**Reusing:** ordinary package/project references and semantic module activation; Data's compiled provider catalog,
source routing, naming, health participation, Entity grammar, and corrective errors; the existing relational schema
orchestrator, filter translator, comparable encoding, provider options, discovery/orchestration adapters, real
connector suites, and PostgreSQL-wire repository behavior; standard .NET DI/options only at their actual owner.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| relational contracts and immutable schema policy | `src/Koan.Data.Relational.Abstractions` | Cross-module DDL/dialect/orchestrator vocabulary must be inert and cannot remain in a functional module. |
| shared PostgreSQL-wire repository mechanics | `src/Koan.Data.Relational.Npgsql` | PostgreSQL and Cockroach use identical Npgsql/Dapper repository mechanics but must not activate each other's provider module. |
| provider-isolation/schema-policy specs | `tests/Suites/Data/Relational/Koan.Data.Relational.Tests` | A fast owner suite must prove one orchestrator can execute different provider policies without global mutation or provider activation. |
| package companions and version intent | both new project roots | Every independently shipped mechanism/contract package needs an exact module-author reference intent and honest limits. |

**Coalescence:** closest successful pattern is the Redis backend split: shared vocabulary is inert, one functional
owner registers shared lifecycle, and thin consumers contribute concern-specific mechanics. Rebuild Relational the
same way at pillar specificity. `Koan.Data.Relational` becomes the single functional owner of schema orchestration and
translator mechanics; its Abstractions package owns only contracts/policy. Delete global
`RelationalMaterializationOptions`, its configurator, adapter bridge configurators, repeated
`AddRelationalOrchestration` calls, provider-local duplicate DDL/matching enums, unused cache/debug output, the unused
Dapper package, and the unproved public storage-shape concept. Extract the byte-identical PostgreSQL/Cockroach
repository into a no-module Npgsql mechanism package and delete the friend-assembly relationship. Do not force SQL
Server/SQLite into the same repository base yet: their connection, JSON, paging, and DDL mechanics differ materially;
the per-route policy/contract base is the stable prerequisite for any later repository-engine convergence.

**Ergonomics:** application C# does not grow. Humans and models still read “reference provider, AddKoan, Entity,”
with optional provider-local governance where the guarantee requires it. IntelliSense loses three duplicate policy
enums and an unsupported storage-shape branch. Module authors receive one relational contract vocabulary and no
registration helper; Cockroach becomes an honest thin provider instead of secretly carrying PostgreSQL. Operators
and agents can attribute schema decisions to the actual provider/source instead of a process-global mutable option.

**Constraints satisfied:**

- no HTTP route or controller change;
- Entity statics remain the application data path;
- no large-source scan or streaming claim changes;
- stable provider/config identifiers remain in provider `Infrastructure.Constants` and tunables remain typed options;
- both reusable packages receive README/TECHNICAL/version companions;
- current docs and generated package truth update; ADRs remain unchanged historical records;
- only relational owner/provider/tests/packages run before the R11-07 certification boundary.

**Risks:** four connector suites and Jobs/Web consumers rely on relational schema behavior; Cockroach needs a real
container to prove the extracted pg-wire delta; production-DDL guards must remain fail-closed; removing the unused
storage-shape API is intentionally breaking and requires a current-doc sweep; generated truth will add two packages
and retire one. Full PostgreSQL/SQL Server repository convergence remains a future internal opportunity, not a claim
of this bounded stable-base slice.

### Relational provider-family evidence

- `Sylin.Koan.Data.Relational` is now the single functional schema-orchestration owner. The public registration helper,
  global materialization options/configurator, and four provider bridge configurators are deleted.
- Cross-module DDL, dialect, feature, column, and immutable policy vocabulary now lives in the module-free
  `Sylin.Koan.Data.Relational.Abstractions` package. Provider-local duplicate DDL/matching enums are gone.
- Every repository supplies its already-resolved table plus one immutable provider/source policy. The orchestrator no
  longer resolves the default Data route a second time, so named sources and coexisting providers retain their own
  schema, projection, production guard, and matching decision.
- PostgreSQL and CockroachDB share `Sylin.Koan.Data.Relational.Npgsql`, a module-free repository mechanism. Cockroach
  no longer references the PostgreSQL connector or uses `InternalsVisibleTo`; its primary-key ordering difference is an
  explicit option. SQL Server and SQLite remain separate because their mechanics do not yet justify a common engine.
- The unused Dapper command shim and unevidenced `RelationalStorage` Entity decoration are retired rather than taught.
  SQLite's test-only schema fallback and debug output are removed; the shared owner is authoritative.
- The focused relational owner suite passes 4/4, proving route-local policy/table identity, one registration owner,
  DDL guard integrity, and Cockroach assembly independence. Isolated real-path checks pass for SQLite lifecycle, PostgreSQL idempotent schema
  creation, Cockroach shared isolation, and SQL Server CRUD/paging. The full SQLite process still exposes a separate
  parallel host-bootstrap/Pillar-catalog contamination (34 fail during boot, 1 pass); an isolated affected cell passes.
- All four connectors and both new mechanism/contract projects build warning-clean. Seven Release packages contain
  their DLL/XML, package-owned README, canonical icon, symbols, and build-transitive composition props. Nuspecs carry
  the intended graph; Cockroach has no PostgreSQL connector dependency. The two new packages and Cockroach have no
  known vulnerable direct or transitive packages under the current NuGet audit.
- Generated truth now contains 111 packages: 26 repair-required, 47 review-required, and 38 structurally ready. The
  relational owner, contracts, Npgsql mechanism, PostgreSQL, SQL Server, SQLite, and Cockroach package pages describe
  their current boundaries without promoting support maturity.

The relational provider family passes this R11-05 slice. The complete release ratchet remains reserved for R11-07;
the shared parallel-host bootstrap defect remains separately visible and is not misreported as relational evidence.

### Local Data provider-family discovery

**Task:** graduate the JSON and InMemory Data connectors by making their zero-infrastructure roles explicit,
removing alternate registration/configuration surfaces that bypass the Data pillar, and keeping physical naming inside
the already-elected provider route.

**Application intent:** “Define an Entity and use normal Entity verbs. With the foundation bundle, persist an
inspectable local result automatically through JSON. When a test or deliberately ephemeral application references
InMemory directly, keep its state inside that host and lose it at process exit.”

**Public expression:** the application retains one `AddKoan()` and its Entity definition. JSON requires no provider
code when it is the available automatic floor. InMemory requires only its package reference when it is the intended
direct provider; `Koan:Data:Sources:{source}:Adapter` or `[DataAdapter]` remains available only when an application
with several eligible providers must pin placement. There is no per-Entity `AddJsonData<TEntity,TKey>` registration.

**Guarantee/correction:** JSON is the deliberate automatic floor (`IsAutomaticFloor`); InMemory is a low-priority
direct provider, not a hidden fallback. Selection still follows Data's reference provenance, source, Entity, and
application-default rules. InMemory data is owned by one host singleton and isolated by routed source, Entity type,
and ambient partition. JSON physical names are resolved by the factory already selected for that repository; an Entity
operation must not re-enter provider election merely to name its file. Invalid JSON remains corrective and neither
provider claims provider-bounded streaming.

**Complete intent surface:** package reference, `AddKoan()`, Entity definition, optional source/provider pin, JSON
directory configuration, and ordinary Entity verbs. JSON's unused `DefaultPageSize` setting is not part of repository
behavior and is removed rather than documented. The InMemory store manager and reset methods are implementation
details, not application or test APIs; conformance tests own isolation through fresh hosts.

**Public concepts:** JSON means inspectable, single-process local persistence and is the bundle's automatic floor.
InMemory means host-scoped ephemeral storage and a provider-neutral conformance oracle. A provider priority orders
already-eligible direct candidates; it does not by itself create fallback eligibility. Data owns selection,
participation diagnostics, repository caching, semantic guards, and naming composition; adapters own only their
storage mechanics and naming constraints.

**Docs read:** `docs/engineering/index.md`, `docs/architecture/principles.md`, `docs/reference/data/index.md`, and
`docs/guides/data/entity-access-and-streaming.md` establish Entity-first expression, reference-driven availability,
deterministic provider selection, and honest streaming limits. Both connector README/TECHNICAL companions were read
in full. Current InMemory prose incorrectly calls priority `-100` a fallback and disagrees with current public evidence
on 55 versus 56 tests; JSON documents its real automatic-floor/readiness behavior but does not expose the unused
page-size setting it still reports at boot.

**Code read:** `DataProviderCatalog.SelectAutomaticCore` grants automatic eligibility only to directly referenced
providers and `IsAutomaticFloor` candidates; priority orders that eligible set. `DataService` caches the selected
repository by Entity/key/provider/source. `JsonAdapterFactory`, `JsonRepository`, JSON options/configuration/health,
and the full live usage search show that `AddJsonData` has no consumer and bypasses factory election, source routing,
health participation, and the semantic facade. `DefaultPageSize` is configured and reported but never read by the
repository. `JsonRepository.ComputePhysicalName` calls `AdapterNaming`, which re-runs route selection after the
factory was already selected. `InMemoryDataStore` is host-singleton implementation state; its two public reset methods
have no live consumer. Redis and InMemory Vector confirm the closest correct naming pattern: retain the selected
factory/naming provider and resolve the ambient partition through it.

**Reusing:** Data's existing provider catalog, source routing, repository cache/facade, diagnostics, common key-value
family, `INamingProvider.ResolveStorage`, `StorageNameGenerator`, ambient partition, standard .NET DI/options, and the
existing JSON/InMemory connector suites. No new application abstraction or registration mechanism is needed.

**Creating new:** one provider-local constants owner in InMemory for its stable provider ID, alias, priority, and boot
report keys. These values already form public/configuration vocabulary and otherwise remain duplicated magic literals.
No new runtime service, contract, option, attribute, or application API is introduced.

**Coalescence:** delete the JSON manual-registration extension and dead page-size option/reporting branch. Pass the
already-selected JSON factory and host services into its repository for naming, matching the existing provider-owned
naming model and eliminating recursive election from the operation path. Internalize the InMemory store and delete its
unused reset surface. Preserve the common key-value family and Data facade as the meaningful shared chokepoints; do not
invent a local-provider base class because file persistence and resident dictionaries have materially different
lifecycle and concurrency mechanics.

**Ergonomics:** application code does not grow. Developers and models see one truthful story: JSON happens
automatically for a first local result; referencing InMemory means intentionally ephemeral state. IntelliSense loses
an unsupported `AddJsonData` branch and an internal store manager. Startup facts stop calling InMemory a fallback and
stop reporting a setting with no effect. Operators retain selected-provider/source participation and JSON readiness.

**Constraints satisfied:** Entity statics remain the only application data path; no controller/HTTP surface changes;
provider identifiers remain stable; no large-source or durability claim grows; the automatic JSON floor is preserved;
current public docs and generated package truth update together; ADRs remain untouched historical records; only the
two connector suites/builds/packages and documentation checks run before R11-07.

**Risks:** changing JSON naming construction must preserve ambient partition files and configured source directories;
removing a public but unused registration extension is intentionally breaking under the greenfield mandate; internal
tests that construct `JsonRepository` directly need the same selected naming owner explicitly; package-quality counts
must be regenerated rather than hand-edited. No cross-provider selection behavior is changed.

### Local Data provider-family evidence

- JSON remains Data's explicit `IsAutomaticFloor`; InMemory remains a direct provider at priority `-100`. Public and
  startup prose now distinguish eligibility from priority instead of calling both mechanisms “fallback.”
- `JsonRepository` retains the selected factory's `INamingProvider` and resolves the ambient partition through it. It
  no longer calls `AdapterNaming` and therefore cannot re-enter provider/source election from its file-operation path.
- The unused public `AddJsonData<TEntity,TKey>` bypass is deleted. JSON's unused `DefaultPageSize` option,
  configurator branch, constants, and startup setting are also deleted; paging remains owned by the shared Entity/Data
  contract rather than an inert adapter knob.
- `InMemoryDataStore` is internal host-owned state. Its unused public reset methods are deleted, while source, Entity,
  and partition isolation continue through the common key-value family and fresh test hosts.
- Focused connector suites pass JSON 21/21 and InMemory 56/56. Both provider and test projects build warning-clean
  after a current restore. The existing JSON partition and persistence cells prove naming/file behavior through the
  selected provider path, including corrective corrupt-store handling.
- Both Release packages contain their DLL/XML, package-owned README, canonical icon, symbols, and build-transitive
  composition metadata. Package-quality now reports both connectors structurally ready with no findings.
- The canonical product surface adds one verified local-provider claim backed by the two package companions and
  connector suites. Generated truth now contains 111 packages: 26 repair-required, 46 review-required, and 39
  structurally ready across 16 claims. Public documentation truth passes across 204 current files and 38 navigation
  targets.

The local Data provider family passes this R11-05 slice. No release candidate, full certification run, package feed,
or remote state was created; the complete release ratchet remains the single R11-07 boundary.

### Data pagination-ownership discovery

**Task:** make pagination ownership semantically honest across Data: caller-facing capabilities may choose and bound a
default page, while Data adapters only execute an explicit `QueryDefinition` page and never invent a row limit.

**Application intent:** `await Product.All()` means the complete visible set; `await Product.Page(2, 100)` means exactly
the requested page. A Web or MCP surface may deliberately apply its own documented page policy before calling Data.

**Public expression:** `Product.All(ct)` is unpaged and `Product.Page(page, size, ct)` is explicitly paged. HTTP uses
`[Pagination(...)]` or its Web-owned default policy. No adapter reference, adapter option, provider configuration, or
backend-specific decoration is part of pagination intent.

**Guarantee/correction:** an unpaged Data query reaches every adapter without an implicit `LIMIT`, `Take`, or provider
default. Explicit pages preserve their requested size. A consumer boundary may refuse an unsafe unpaged response or
apply a declared page policy, but it must encode that decision into `QueryDefinition`; an adapter cannot silently
truncate it.

**Complete intent surface:** choose `All`, `Page`, `FirstPage`, an explicitly paged `QueryDefinition`, a provider-bounded
Entity stream, or a consumer policy such as Web pagination. There is no Data-adapter pagination configuration. Large
unbounded materialization remains an explicit caller decision; Web's absolute response safety limit remains a
consumer-owned corrective refusal.

**Public concepts:** `QueryDefinition` carries explicit pagination; Entity statics express application intent;
`PaginationAttribute`/Web endpoint options express HTTP policy; vector `DefaultTopK`/`MaxTopK` remain vector-search
candidate semantics and are not row pagination. `IAdapterOptions` carries readiness only.

**Docs read:** `docs/engineering/index.md` requires Entity-first access and explicit paging/qualified streams for large
sets; `docs/architecture/principles.md` assigns application intent to the application and backend mechanics to adapters;
`docs/reference/data/index.md` names `All`, `Page`, and streams as distinct cost choices;
`docs/guides/data/entity-access-and-streaming.md` explicitly defines `Product.All()` as the full set;
`DATA-0107` requires explicit provider-bounded candidate pages for streams; `docs/reference/web/http-api.md` assigns
pagination defaults and bounds to Web.

**Code read:** `QueryDefinition` represents pagination only when both page and size are positive; `Data<TEntity,TKey>`
passes unpaged `All` unchanged and centralizes explicit page finalization; `FilterPushdownCoordinator`,
`KeyValueStore`, Mongo, Couchbase, and ordinary relational query paths only page when `HasPagination` is true;
`EntityController`/`EntityEndpointService` deliberately compile Web policy into an explicit page; relational raw
predicate paths instead apply `_defaultPageSize` when shaping is unpaged, silently truncating results. The shared
`IAdapterOptions.DefaultPageSize`, configurator, boot report, and query extensions otherwise have no honest ordinary
query consumer. Vector adapters alias unrelated `DefaultTopK` into that property solely to satisfy the interface.

**Reusing:** `QueryDefinition.HasPagination`, `WithPagination`, and `WithoutPagination`; Entity `All`/`Page` statics;
central Data query planning/finalization; Web `PaginationPolicy` and safety bounds; provider-bounded stream contracts.

**Creating new:** none in production. Focused regression methods are added to the existing SQLite provider-paging,
PostgreSQL CRUD, and SQL Server CRUD specifications because those are the three distinct relational implementations.

| New code | Location | Justification |
|---|---|---|
| Unpaged raw-query regression method | Existing SQLite provider-paging spec | Proves SQLite does not append an adapter-owned default limit. |
| Unpaged raw-query regression method | Existing PostgreSQL CRUD spec | Proves the shared Npgsql implementation used by PostgreSQL/Cockroach is unbounded. |
| Unpaged raw-query regression method | Existing SQL Server CRUD spec | Proves SQL Server's separate dialect is unbounded. |

**Coalescence:** the closest correct pattern is every adapter's ordinary `Query(QueryDefinition)` path: it accepts an
explicit page and otherwise returns the full visible set. This is framework law, owned by `QueryDefinition` plus the
Data coordinator. Keep explicit paging and Web policy; absorb no policy into adapters; rebuild relational raw-query SQL
to append paging only when requested; delete `IAdapterOptions.DefaultPageSize`, Core paging configuration/reporting
helpers, provider page-size options/config/provenance, relational `_defaultPageSize`, and vector compatibility aliases.
The application/consumer is too narrow to enforce adapter honesty, while each adapter is too narrow to own the common
meaning.

**Ergonomics:** humans and coding models can trust the verb: `All` means all and `Page` means page. IntelliSense no
longer advertises provider knobs that cannot honestly affect ordinary Entity queries, and vector search no longer
pretends top-K is row paging. Module authors implement readiness plus real provider mechanics rather than ceremony.

**Constraints satisfied:** Entity-first public examples; no HTTP route work; no new literals/options/types; explicit
paging or qualified streams remain the guidance for large sets; current docs and package companions will be aligned;
no placeholder, repository-first, or inline-endpoint path is introduced.

**Risks:** removing public pre-1.0 adapter option properties is intentionally breaking. Raw predicate queries without
shaping can return materially more rows than before; that is the requested honest behavior, but documentation must
continue to steer large operations toward explicit pages or qualified streams. Vector top-K remains separately bounded
because nearest-neighbor candidate count is part of that operation's own semantics, not adapter row pagination.

### Document Data provider-family discovery

**Task:** graduate MongoDB and Couchbase as the server-backed document family by preserving their common Entity
semantics, centralizing identical route mechanics, making readiness participation-owned, and deleting public/config
surfaces that do not affect behavior.

**Application intent:** “Reference MongoDB or Couchbase, define an Entity, and use normal Entity verbs against the
selected document store. Discover local infrastructure automatically when appropriate, or configure an exact
provider endpoint and physical database/bucket when placement is a business or operations decision.”

**Public expression:** reference `Sylin.Koan.Data.Connector.Mongo` or
`Sylin.Koan.Data.Connector.Couchbase`, retain the application's single `AddKoan()`, and define the Entity. A concrete
`ConnectionStrings:Mongo` / `ConnectionStrings:Couchbase` plus `Koan:Data:<Provider>:Database|Bucket` is the shortest
explicit placement; `auto` uses provider-owned discovery. `[DataAdapter]`, an Entity/source selection, or the
configured default is required only when multiple direct providers make placement ambiguous. Entity `Get`, `Query`,
`Page`, supported streams, `Save`, `Remove`, and the existing expert `QueryRaw` escape hatch remain the complete code
surface. There is no connector registration, repository, public serializer, or provider-specific query wrapper in
ordinary application code.

**Guarantee/correction:** Data selects one provider/source route and caches its repository. The selected factory owns
connection pooling and supplies its naming decision directly to the document-family base; a repository operation must
not re-elect a provider merely to resolve its container. A referenced but unelected/unused connector remains available,
non-critical, and connection-free. Once selected, an unavailable endpoint, bucket/database, required collection/index,
or unsupported guarantee fails through the existing corrective Data/readiness boundary rather than silently using a
different provider or reporting healthy. Mongo/Couchbase streaming remains provider-bounded only within DATA-0107's
explicit sort/buffer/snapshot limits.

**Complete intent surface:** package reference, `AddKoan()`, Entity definition, and reachable provider runtime.
Optional exact provider/source selection, endpoint, Mongo database, Couchbase bucket/scope/collection, naming policy,
Couchbase query timeout/durability, credentials, and discovery controls express real placement or guarantee choices.
Adapter-owned `DefaultPageSize` options, Couchbase's ignored separator, generic cross-provider configuration aliases,
public BSON conventions/store formatters, and `CouchbaseQueryDefinition` are not required actions and are removed.

**Public concepts:** Entity verbs express document persistence; Data adapter/source expresses physical placement;
provider options express endpoint and native database/bucket hierarchy; Couchbase durability changes acknowledged-write
semantics; query timeout bounds provider work; naming overrides change physical container identity; `QueryRaw` is the
existing explicitly provider-specific escape hatch. Provider factories, connection providers, fixed options monitors,
BSON conventions, N1QL definition carriers, and identifier sanitizers are module-author/runtime mechanics.

**Docs read:** `docs/engineering/index.md` requires Entity-first access, one constants owner, typed effective options,
package companions, and focused validation; `docs/architecture/principles.md` requires one compiled route, thin hot
paths, participation-owned defaults, and visible corrective failures; `docs/reference/data/index.md` defines exact
selection order and denies universal provider parity; `docs/engineering/adding-a-connector.md` requires reference-driven
modules, autonomous discovery, health, orchestration, real integration evidence, and no application registration, but
its “repository per request” and removed typed-helper guidance are stale; `docs/reference/data/adapter-diagnostics.md`
describes the retired mutable augmenter model and is evidence of documentation drift, not current runtime authority.
Both provider README/TECHNICAL companions were read fully: Mongo is compact but lacks a complete meaningful-result
expression/evidence; Couchbase lacks package-title/install/meaningful-use structure and overstates swapability and eager
readiness.

**Code read:** `DocumentStore<TEntity,TKey>` already owns the common operation, readiness, schema, batch, capability,
and telemetry shell but leaves both dialects to call `AdapterNaming`, which re-runs route election on their operation
path. `MongoAdapterFactory`/`CouchbaseAdapterFactory` already own source-specific connection pooling and identical
fixed-options-monitor mechanics. Mongo's selection-aware health is the correct closest pattern. Couchbase instead
registers its connection provider as an eager initializer/readiness service and has an always-critical bespoke health
contributor. Both `DefaultPageSize` options are never read and the cross-family pagination discovery above assigns no
default-page policy to adapters; Couchbase's `Separator` is ignored in favor of the required
`_`; `CouchbaseQueryDefinition.Statement` is ignored by the public `QueryRaw` path. Mongo spreads vocabulary across
`Constants`, `ConfigurationConstants`, and unused `MongoConstants`; several BSON types are public without consumers;
`JObjectSerializer` captures an ambient host logger in a process-global driver registration and silently changes a
failed document serialization into a string. Live searches found no application/sample consumers of those leaked
types or removed option/config branches.

**Reusing:** `DocumentStore`, `INamingProvider.ResolveStorage`, ambient Entity partition, Data's provider/source
catalog and repository cache, `DataAdapterHealthContributorBase`, provider-owned discovery/orchestration, standard
.NET options/DI, existing per-source provider pools, native Mongo/Couchbase translators and drivers, DATA-0107 stream
coordination, connector integration suites, and package-quality/product-surface compilers.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| `FixedOptionsMonitor<T>` | `src/Koan.Data.Core/Configuration/FixedOptionsMonitor.cs` | Mongo and Couchbase need the identical immutable per-source `IOptionsMonitor` mechanism; Data route construction is the narrow shared owner, while Core-wide or adapter-local ownership is respectively too broad or duplicated. |
| Couchbase participation specs | `tests/Suites/Data/Connector.Couchbase/.../Specs/Health/` | Docker-free owner tests must prove package availability does not create eager connection/readiness, while existing real-container cells continue to prove selected behavior. |

**Coalescence:** keep `DocumentStore` as the document-family semantic owner and add only selected-provider naming to
that base; pass each factory into its dialect and delete both `AdapterNaming` re-election calls. Absorb the two local
options-monitor clones into one Data Core mechanism. Rebuild Couchbase health on Mongo's Data-owned participation base,
extract its route decision for both repository construction and health, and delete eager initializer/readiness aliases.
Consolidate Mongo's stable vocabulary into its existing `Infrastructure.Constants`, delete dead option/provenance and
legacy alias branches in both providers, internalize provider mechanics, and make JObject serialization fail normally
instead of retaining a host logger or silently changing storage shape. Do not merge Mongo and Couchbase connection
providers, translators, schema logic, or repositories: their driver lifecycle, physical hierarchy, query language,
identity encoding, and transaction semantics differ materially; `DocumentStore` is already the correct shared depth.

**Ergonomics:** application C# does not grow. Humans and models retain “reference provider, AddKoan, Entity,” with
standard .NET connection strings and only effective native knobs. IntelliSense loses dead paging/separator/query-wrapper
branches and BSON/identifier machinery. Operators see an unused Couchbase connector as available but non-critical,
then see the exact selected sources become readiness dependencies. Module authors reuse one fixed-route monitor and one
document naming seam; hot operations consume the selected factory instead of renegotiating it.

**Constraints satisfied:**

- Entity statics remain the common data path; no HTTP/controller surface changes;
- stable provider/config IDs move to one project constants owner; effective tunables remain typed options;
- DATA-0107 streaming constraints and provider-specific native query behavior remain explicit;
- no new application abstraction, registration helper, attribute, or magic activation metadata is introduced;
- README/TECHNICAL, current connector guidance, topology, generated product truth, focused tests, packages, and
  vulnerability evidence update together; ADRs remain dated and untouched;
- only Data Core/document and Mongo/Couchbase focused proof runs before R11-07.

**Risks:** Couchbase's real integration suite requires a healthy Docker engine and has slower bucket/index startup;
removing broad legacy config aliases is intentionally breaking under the greenfield mandate; selected-factory naming
must preserve ambient partition isolation; Couchbase participation health must reuse exactly the same per-source route
as repositories; BSON serializer visibility/failure cleanup must preserve valid JObject round trips. Support maturity
will not be promoted beyond the exact focused evidence observed in this slice.

### Data pagination-ownership evidence

- `IAdapterOptions` now carries readiness only. The dead Core paging configuration/reporting path, provider
  `DefaultPageSize` options and provenance, relational repository defaults, and vector compatibility aliases are
  deleted. A production-code search finds no remaining adapter-owned row-page default.
- SQLite, SQL Server, and shared Npgsql raw-predicate paths append paging SQL only when the caller supplied explicit
  shaping. Focused regressions seed 75 rows and prove that an unpaged request returns all 75 while an explicit page
  returns the requested 7 on SQLite, PostgreSQL, and SQL Server (1/1 each).
- Data Core, Npgsql, Mongo, Couchbase, PostgreSQL, CockroachDB, SQLite, SQL Server, Redis, Qdrant, Milvus, Weaviate,
  Elasticsearch, and OpenSearch build with zero warnings and errors after the contract removal.
- Current Data reference prose states the invariant directly: `All()` means the complete visible set; consumer
  boundaries such as Web may compile a documented policy into an explicit `QueryDefinition`, while adapters only
  execute that intent. The generated adapter matrix no longer advertises adapter row-pagination guardrails.

### Document Data provider-family evidence

- `DocumentStore` consumes the naming provider selected by its factory, so Mongo and Couchbase resolve ambient Entity
  partitions without re-entering provider election on the operation path. Their identical immutable per-source
  options-monitor clones are replaced by one Data Core `FixedOptionsMonitor<T>`.
- Mongo retains selection-aware participation and passes its complete focused connector suite 68/68. Its BSON support
  is internal driver machinery, and failed `JObject` serialization now fails honestly rather than changing the stored
  shape to a string.
- Couchbase no longer registers an eager adapter initializer/readiness alias. Three Docker-free participation cells
  prove that an available but unelected connector is non-critical and connection-free, while selected participation
  becomes critical. One real-container CRUD cell passes through lazy cluster, bucket, collection, and query setup.
- The real Couchbase first-use cell took approximately 64 seconds. This is recorded as an observed operational rough
  edge, not a performance guarantee. The full connector suite exceeded this slice's five-minute focused budget without
  failure output and is not claimed as certified; the exact release ratchet remains R11-07's responsibility.
- Both package companions now present the exact package identity, install/reference expression, smallest meaningful
  Entity result, placement and configuration rules, complete-set pagination contract, readiness behavior, and native
  limitations. Both packages are structurally ready with zero objective quality findings. Focused Release packages
  contain the connector DLL/XML, build-transitive composition metadata, package-owned README, canonical icon, and
  symbols; current NuGet audit reports no known vulnerable direct or transitive packages for either connector.
- Generated product truth contains 111 packages: 26 repair-required, 45 review-required, and 40 structurally ready
  across 17 claims. The new demonstrated document-provider claim names both packages and their focused evidence.
  Strict documentation generation passes, and public documentation truth passes across 204 current files and 38
  navigation targets.

The pagination invariant and document Data provider family pass this R11-05 slice. No release candidate, full
certification run, package feed, or remote state was created; the complete release ratchet remains the single R11-07
boundary.

### Search-engine vector provider-family discovery

**Task:** graduate the Elasticsearch/OpenSearch vector family by making their already-shared Lucene runtime the one
family owner for configuration, discovery, factory construction, health, naming, and boot projection, leaving each
connector with only its native dialect and service identity.

**Application intent:** “Reference Elasticsearch or OpenSearch, keep `AddKoan()`, and use the ordinary Entity vector
ring. Koan provisions the Entity index, stores embeddings and metadata, pushes supported filters into kNN search, and
reports the selected backend only when that Entity actually uses it.”

**Public expression:** reference `Sylin.Koan.Data.Connector.ElasticSearch` or
`Sylin.Koan.Data.Connector.OpenSearch`; retain the existing Entity/AI/vector semantics. Configure an exact
`ConnectionStrings:<Provider>` or `Koan:Data:<Provider>:Endpoint` only when placement is explicit. Use
`[VectorAdapter]` or `Koan:Data:VectorDefaults:DefaultProvider` only when multiple vector providers make the intended
backend ambiguous. No repository, health registration, discovery adapter, or search-engine helper belongs in
application code.

**Guarantee/correction:** Vector owns provider election and records provider/source participation. A referenced but
unused search connector is available, non-critical, and connection-free. Once selected, its exact endpoint/index
route becomes critical and failures name the provider/source; Koan does not substitute another vector backend.
Supported metadata filters are pushed into the native kNN request; unsupported filters fail loudly. A source is
physically isolated by index naming on the configured cluster; per-source cluster endpoints remain unsupported and
must not be implied. Explicit `IndexName` continues to warn when it defeats active partition/source naming.

**Complete intent surface:** provider reference, `AddKoan()`, Entity vector/AI use, optional exact provider selection,
endpoint and credentials, index prefix or deliberate index pin, fields, similarity, dimension, refresh mode, timeout,
and index auto-create. Top-K is the requested nearest-neighbour candidate bound, not row pagination. Generic
`Koan:Data:ConnectionString`, duplicate casing aliases, inert `BaseUrl`, provider-specific health classes, and direct
use of `SearchEngineFilterTranslator` are not application decisions.

**Public concepts:** the Entity vector ring expresses business intent; Vector provider/source expresses placement;
the two provider packages express native availability; the transitive `Sylin.Koan.Data.SearchEngine` package is a
module-free shared implementation substrate analogous to relational mechanics, not a capability an application must
reference directly. `ISearchEngineDialect`, shared options/configuration, REST repository, and filter translator are
module-author/runtime mechanics and will be hidden from ordinary IntelliSense where compatibility permits.

**Docs read:** the provider READMEs show a useful vector result but lack limitations; both TECHNICAL companions repeat
the same runtime/configuration account. The SearchEngine README uses the wrong package title, teaches applications to
call the translator directly despite saying the package is not application-facing, and incorrectly says repositories
remain separate even though `SearchEngineVectorRepository` already owns both. The old VectorAdapterSurface README's
0/25 OpenSearch note is historical drift contradicted by current native-dialect tests.

**Code read:** `SearchEngineVectorRepository` already centralizes REST/auth/bulk/index/scroll/filter behavior behind
the correct three-member dialect seam. In contrast, the two connectors duplicate their options configurators,
150-line boot reports, discovery normalization and health probes almost line-for-line. Their health contributors are
always critical by reference, bypassing the existing Vector participation base used by Qdrant/Milvus/Weaviate.
`Endpoint` and `BaseUrl` are read but discarded by both configurators when `ConnectionString` remains `auto`, so the
documented endpoint key can be silently ignored. The generic `Koan:Data:ConnectionString` alias lets one provider read
another concern's placement. Both factories repeat construction/naming and the repository calls
`VectorAdapterNaming.GetOrCompute`, which can reselect a provider after `VectorService` already selected the factory.

**Reusing:** `VectorService`, `VectorProviderCatalog`, `IVectorAdapterParticipation`,
`VectorAdapterHealthContributorBase`, `StorageNameGenerator`, the existing shared repository/filter translator/dialect
seam, provider-owned `KoanService` metadata, standard .NET configuration/options/HTTP factory, Core safe logging and
redaction, and the two live vector conformance projects.

**Creating new:** one module-free search-engine connector descriptor plus one shared connector-mechanics owner in
`Koan.Data.SearchEngine`. It compiles the common exact configuration, autonomous discovery, selected-factory repository
construction, participation-aware authenticated health probe, and boot projection. These are one coherent family
responsibility currently copied twice; provider assemblies retain thin annotated factories/modules and native
dialects. `VectorAdapterNaming` receives one selected-provider overload so search operations consume the already-made
decision instead of negotiating again.

**Coalescence:** keep three packages because they have three real responsibilities: one transitive family mechanism
and two independently referenceable native providers. Delete the six duplicated configurator/discovery/health classes,
collapse the two large reports and factories onto the shared owner, and keep only dialect mapping/service metadata in
the leaves. Do not merge Elasticsearch and OpenSearch dialects: their kNN query and index mapping shapes differ
materially. Do not move Lucene REST mechanics into general Vector Core: other vector stores do not share them.

**Ergonomics:** application C# does not grow. Humans and models retain “reference provider, AddKoan, use Entity
semantics”; exact endpoint configuration finally behaves as documented. IntelliSense stops suggesting a translator
package as an application capability. Operators see one effective redacted endpoint and only active providers affect
readiness. Module authors add a future Lucene-family provider by supplying service identity plus the narrow dialect,
not by copying configuration/discovery/health/reporting machinery.

**Constraints satisfied:** no application abstraction, registration call, endpoint, or decoration is added; selected
provider/source and isolation stay Vector-owned; provider identities and exact sections remain stable; removed generic
aliases are intentionally breaking under the greenfield mandate; ADRs remain untouched; focused shared/unit/provider,
package, docs, and vulnerability proof only before R11-07.

**Risks:** secured automatic discovery still cannot prove credentials before options are compiled and must be stated;
Elasticsearch API-key auth is native while OpenSearch normally uses Basic/security-plugin auth; explicit per-source
clusters are unsupported; live suites depend on Docker and large JVM containers; the shared mechanism package remains
publicly installable because NuGet dependencies require it, so its package page must clearly say “transitive/module
authors,” not invent a direct application result.

### Search-engine vector provider-family evidence

- `Koan.Data.SearchEngine` is now the one family owner for exact configuration, autonomous discovery, authenticated
  HTTP setup, participation-aware health, startup projection, selected-provider naming, repository construction,
  common REST operations, and filter translation. Elasticsearch and OpenSearch retain thin provider modules,
  discovery identities, orchestration declarations, and their three-member native dialects. The change removes
  duplicated configurators, health contributors, large startup reports, and factory construction rather than adding
  another application concept.
- Exact `Koan:Data:<Provider>:Endpoint` configuration is exercised without test-only post-configuration and now reaches
  the repository. The unsafe generic Data connection-string alias and inert `BaseUrl` branches are gone. A real host
  observes an unused provider as non-critical, connection-free `Unknown`; provider/source participation is the only
  transition into readiness dependency.
- Vector-only composition no longer forces construction of a record-Data default plan when no record provider exists.
  The Data Core participation suite passes 4/4, including that regression. Naming now consumes the factory already
  selected by `VectorService`; it does not re-enter provider election on the operation path.
- Both provider-owned AODB conformance paths pass 4/4 after the health/configuration changes. The complete focused
  matrix passes 29 with 4 capability-gated skips for Elasticsearch 8.13.4 and 29 with 4 skips for OpenSearch 2.13.0.
  The skips state the same honest boundary for both: embedding retrieval and hybrid search are unsupported. The old
  public OpenSearch 0/25 note is retired. Both test projects still emit `MSB9008` for a retired test-only project
  reference; the repository-wide cleanup is preserved as PMC-032 rather than mixed into provider production work.
- All three packages build and pack in Release. Their nupkgs contain the library/XML, build-transitive composition
  metadata, package-owned README, and byte-identical canonical icon; current NuGet audit reports no known vulnerable
  direct or transitive packages. Package quality reports all three structurally ready with no objective findings.
- The public claim remains `demonstrated`, not `verified`: native container matrices prove the current vector floor,
  while secured automatic discovery, per-source cluster endpoints, embedding retrieval, hybrid search, and a full
  release certification remain explicitly outside this slice. Generated truth now contains 111 packages: 26 repair,
  42 review, and 43 structurally ready across 18 claims. Strict documentation generation and the public truth gate
  pass across 204 current files and 38 navigation targets.

The search-engine vector provider family passes this R11-05 slice. No release candidate, full certification run,
package feed, or remote state was created; the complete release ratchet remains the single R11-07 boundary.

### Local vector provider-family discovery

**Task:** graduate the in-memory/sqlite-vec vector family as one meaningful progression: a zero-infrastructure
semantic floor that works from a reference, followed by an embedded durable provider that preserves the same Entity
intent while changing only placement and guarantees.

**Application intent:** “Reference the in-memory vector package and use the ordinary Entity vector/AI ring with no
server or configuration. When durability matters, reference sqlite-vec (and SQLite for record storage when wanted),
keep the same application code, and let Koan place vectors in-process in the local SQLite store.”

**Public expression:** retain `AddKoan()` and the existing Entity vector/AI methods. A direct reference to either local
provider must bring the Vector runtime it implements. In-memory is the lowest-priority automatic floor. sqlite-vec
automatically pairs with SQLite provider identity and may be placed explicitly with the standard
`ConnectionStrings:SqliteVec` or `Koan:Data:SqliteVec:ConnectionString` keys. Multiple vector providers still require
the existing `[VectorAdapter]` or Vector default-provider decision; no new registration call or application service is
introduced.

**Guarantee/correction:** the provider chosen by `VectorService` owns the operation; local repositories consume that
selected factory and the centrally folded source/partition name without re-election or private naming rules. An
available but unused sqlite-vec package is connection-free and non-critical; once selected, its exact routed database
is a readiness dependency and failures identify the source. In-memory is ephemeral and process-local. sqlite-vec is
durable on the three embedded native RIDs only, supports pure kNN but not metadata-filtered scoped reads, and therefore
continues to fail closed for tenant/Shared search rather than implying isolation it cannot execute.

**Complete intent surface:** provider reference; `AddKoan()`; Entity vector/AI calls; optional provider pin; optional
exact sqlite-vec connection; distance metric; ambient partition and Database source routing; startup projection;
selection-aware readiness; native RID support; persistence and filter limitations. Vector Top-K remains the caller's
nearest-neighbour bound, not adapter pagination. Test reset controls and native extraction mechanics are not
application concepts.

**Public concepts:** the Entity ring expresses business intent; Vector owns election, source participation, naming,
isolation decoration, and repository lifetime; in-memory expresses the ephemeral managed guarantee; sqlite-vec
expresses embedded durability. SQLite row storage and sqlite-vec vector storage are independent providers that can
share a file, not one merged data abstraction.

**Docs read:** the root README and architecture principles establish reference-as-intent, meaningful small steps,
local-first defaults, inspectability, and provider negotiation. The Vector README/TECHNICAL and vector reference card
name in-memory as the zero-infrastructure floor and GardenCoop C2 as the local ONNX/sqlite-vec step. Neither local
provider owns a README or TECHNICAL companion today. sqlite-vec package prose overstates automatic row/vector
co-location and NativeAOT support relative to current proof; the in-memory floor claim is not represented by
`IsAutomaticFloor` in code.

**Code read:** `VectorProviderCatalog` selects direct references first and otherwise only candidates declaring
`IsAutomaticFloor`; JSON is the existing honest floor pattern. `VectorService` memoizes provider/source selection and
participation. `VectorAdapterNaming` is the shared selected-factory name fold. Both local repositories currently bypass
it: in-memory prefixes source manually and sqlite-vec omits source from table naming while routing database files
separately. `AdapterConnectionResolver` is the shared source-ownership-aware placement mechanism. sqlite-vec currently
resolves placement only in its factory, reports a different raw default at boot, and has no participation health;
`VectorAdapterHealthContributorBase` is the established selection-aware readiness pattern. Both provider projects
reference Vector abstractions but not the functional Vector runtime, unlike the network and search-engine providers.
`InMemoryVectorAdapterFactory.ClearAll` is public solely for test reset. `Vec0Native` embeds v0.1.9 for win-x64,
linux-x64, and linux-arm64 and fails explicitly elsewhere; extraction currently trusts any same-length cached file.

**Constants/options/DTO inventory:** in-memory has no options, DTOs, or stable constants. sqlite-vec has one public
`SqliteVecOptions`, one public section constant, and one private native version constant; it has no request/response
DTOs. Stable provider aliases, section/connection keys, native version/RIDs, and store defaults need one internal
project vocabulary owner. The typed options remain the complete application configuration surface; no new public DTO
or module contract is justified.

**Reusing:** `VectorProviderCatalog`, `VectorService`, `IVectorAdapterParticipation`,
`VectorAdapterHealthContributorBase`, selected-provider `VectorAdapterNaming`, `AdapterConnectionResolver`,
`DataSourceRegistry`, standard .NET options/configuration/DI, Core redaction and startup provenance, the shared vector
AODB ledger, explicit native bootstrap, and GardenCoop C2 golden proof.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| sqlite-vec route decision | `src/Connectors/Data/Vector/SqliteVec/SqliteVecRoute.cs` | Factory, health, and startup projection require one side-effect-free, source-aware placement owner; three independent reads would recreate distributed policy. |
| sqlite-vec health contributor | `src/Connectors/Data/Vector/SqliteVec/SqliteVecHealthContributor.cs` | Embedded storage is still an operational dependency after selection; the existing Vector participation base supplies the correct availability-to-readiness transition. |
| package companions | each provider's `README.md` and `TECHNICAL.md` | Both public packages currently fail the objective quality contract and lack exact result, placement, limitations, and operator behavior. |

**Coalescence:** make Vector's selected-provider naming the sole local collection identity owner; delete in-memory's
manual source prefix and sqlite-vec's direct naming call. Make one sqlite-vec route decision feed repository creation,
health, and boot projection. Remove the public test reset seam and isolate tests by service-provider/factory lifetime.
Keep two providers and two repositories because ephemeral concurrent managed search and durable serialized native SQL
have different guarantees and mechanics. Do not move SQLite-specific routing/native loading into general Vector Core.

**Ergonomics:** application C# does not grow. One provider package reference becomes complete; the in-memory reference
really is the automatic semantic floor, and swapping to sqlite-vec preserves the Entity code. IntelliSense loses a
test-only reset method. Operators see the same effective de-identified route in boot facts and readiness, and an unused
embedded provider does not create a file or fail the app. Module authors reuse existing Vector election,
participation, naming, and health seams rather than inventing local counterparts.

**Constraints satisfied:** Entity remains the business surface; no HTTP/controller or application abstraction is
added; provider/source/isolation decisions remain in their existing DDD owners; stable local vocabulary is centralized;
route resolution is side-effect free and native/database I/O remains repository/health-owned; README/TECHNICAL,
current public guidance, focused conformance, package evidence, and generated truth move together; ADRs remain dated
and untouched; no release certification suite runs before R11-07.

**Risks:** adding the functional Vector dependency intentionally changes package composition; sqlite-vec native loading
must remain lazy until selection; sharing the SQLite default must not steal a source explicitly owned by another data
provider; source and partition isolation must remain behaviorally identical; native extraction hardening must be
bounded and cross-process safe; unsupported RIDs and metadata filters must be stated rather than hidden. Maturity will
not be promoted beyond focused evidence observed in this slice.

### Local vector provider-family evidence

- Both provider packages now bring the functional Vector runtime they implement. Their inspected nupkgs declare the
  `Sylin.Koan.Data.Vector` dependency and contain the provider DLL/XML, build-transitive composition metadata,
  package-owned README, and canonical icon. Current NuGet audit reports no known vulnerable direct or transitive
  packages for either provider.
- In-memory declares `IsAutomaticFloor = true` at priority -100, consumes the provider already selected by Vector for
  naming, and no longer publishes a process-wide test-reset method. A fresh service-provider lifetime is the reset
  boundary. Its complete provider matrix passes 34/34, including the explicit automatic-floor contract.
- sqlite-vec has one side-effect-free route owner for repository creation, readiness, and boot projection. Exact
  sqlite-vec placement wins; otherwise it pairs with SQLite placement and finally the shared local
  `.koan/data/Koan.sqlite` fallback. Repository and health own directory/database/native effects. The new participation
  cell proves no file and non-critical `Unknown` before use, then paired persistence, critical healthy readiness,
  embedded vec0 loading, and embedding retrieval after selection. Its full focused AODB/participation project passes
  5/5.
- Both local repositories use Vector's selected-provider partition/source name fold rather than maintaining adapter
  naming rules or re-entering provider election. sqlite-vec keeps its honest no-filter boundary, so tenant/Shared reads
  still fail closed; the existing AODB isolation ledger remains green.
- GardenCoop C2 no longer repeats SQLite placement under a second sqlite-vec key. Its wider golden test currently stops
  before the vector story because transitive Storage activation eagerly requires an unused profile; that independent
  layered-activation defect is preserved as PMC-033 rather than hidden with sample-only configuration. The sqlite-vec
  pairing/first-use behavior itself is proved in the provider-owned host.
- Native extraction now validates the embedded payload hash and repairs through a unique temporary file before atomic
  placement. Public/package prose limits support to the actually embedded win-x64, linux-x64, and linux-arm64 assets and
  removes the unproved NativeAOT claim.
- Both packages are structurally ready with no objective quality findings. Generated truth contains 111 packages: 24
  repair-required, 42 review-required, and 45 structurally ready across 19 claims. Public documentation truth passes
  across 208 current files and 38 navigation targets.

The local vector provider family passes this R11-05 slice. No release candidate, full certification run, package feed,
or remote state was created; the complete release ratchet remains the single R11-07 boundary.

### External vector provider-family discovery

**Task:** graduate Qdrant, Milvus, and Weaviate after making Vector query intent and selected-provider naming
framework-owned, then leave each connector with native placement, schema, filter, consistency, and wire mechanics.

**Application intent:** “Reference one external vector provider, keep `AddKoan()` and the ordinary Entity vector/AI
ring, optionally configure its endpoint and native guarantees, and receive exactly the nearest-neighbour count I asked
for without adapter rewriting.”

**Public expression:** reference `Sylin.Koan.Data.Vector.Connector.Qdrant`, `.Milvus`, or `.Weaviate`; use the existing
`Vector<TEntity>` / Entity AI semantics. Configure one standard `ConnectionStrings:<Provider>` or exact
`Koan:Data:<Provider>:Endpoint` only when placement is explicit. Use `[VectorAdapter]` only when multiple candidates
make automatic selection ambiguous. Provider-specific fields, metric, consistency, quantization, credentials, and
static collection pins remain deliberate native options where they change real guarantees.

**Guarantee/correction:** `VectorQueryOptions` owns one positive default Top-K of 10. An explicit positive count reaches
the backend unchanged; zero/negative intent rejects at the query contract and no adapter silently clamps it. The
factory already selected by `VectorService` owns naming for the operation, including partition/source folds. Explicit
connection or endpoint placement wins discovery and boot reporting; generic Data connection strings and casing aliases
cannot accidentally place a different concern. Unsupported filters/features fail through declared capabilities; an
explicit provider or placement never falls back silently.

**Complete intent surface:** connector reference, `AddKoan()`, Entity vector/AI calls, optional exact provider pin,
endpoint/credentials, collection/schema field overrides, similarity metric, dimension only where pre-creating a native
collection requires it, consistency/write-visibility/quantization, ambient partition/source context, readiness, and
startup facts. Per-source endpoints remain unsupported; routed sources isolate by collection/class naming on the one
configured backend. Adapter-owned default/max Top-K, generic `Koan:Data:ConnectionString`, duplicate lowercase
connection keys, Weaviate `BaseUrl`/`Key`, and its inert dimension setting are not application decisions.

**Public concepts:** Entity/Vector expresses semantic intent; `VectorQueryOptions.TopK` expresses requested result
cardinality; Vector owns election, validation, naming, isolation, participation, and lifetime; the connector reference
expresses backend availability; provider options expose only native placement/guarantee choices. Discovery and Zen
Garden remain layered capability contributors, not alternate application APIs.

**Docs read:** engineering guardrails require Entity-first surfaces, stable constants, and package companions;
architecture principles require reference-as-availability, pillar-owned meaning, thin adapters, immutable decisions,
and semantic honesty. Vector README/TECHNICAL establish one provider election/naming/health chokepoint. The vector card
teaches the correct provider-neutral application expression but still presents raw repositories too prominently.
Milvus/Weaviate companions lack full placement/readiness/limits; Qdrant owns none.

**Code read:** `VectorQueryOptions` currently makes Top-K nullable, so every repository invents 10; Qdrant/Milvus expose
unused `MaxTopK`, while Weaviate silently caps explicit values at 200. `ScopedVectorRepository` is already the one query
chokepoint and can enforce positive intent. All three factories receive the selected source but discard it; their
repositories call the generic `VectorAdapterNaming` overload and re-enter election. Qdrant/Milvus configurators read
`Endpoint` but ignore it during automatic resolution, accept a generic Data connection string and duplicate casing
aliases, while boot reporting can claim that endpoint as effective. Weaviate correctly preserves layered Zen Garden
intent but retains the same generic/casing aliases plus legacy `BaseUrl`, `Key`, and `Weaviate:Endpoint`. Its Dimension
option is absent from schema creation and first-vector discovery overrides it, making the public knob inert; dimension
changes currently log and continue toward a backend failure rather than reject correctively.

**Constants/options/DTO inventory:** each connector already owns an Infrastructure constants vocabulary and typed
options. Stable provider/HTTP/health/config identifiers exist but Qdrant/Milvus still duplicate literals in discovery
and modules, and Weaviate constants are unnecessarily public. Repositories use internal dictionaries/JSON objects
rather than public request/response DTOs. No new DTO, registration abstraction, or shared provider package is needed.

**Reusing:** `VectorQueryOptions`, `ScopedVectorRepository`, selected-provider `VectorAdapterNaming`,
`VectorAdapterHealthContributorBase`, participation, Core discovery coordinator/reporting, provider constants/options,
native filter translators/repositories, three provider AODB/matrix suites, package compiler, and public truth gate.

**Creating new:** none. The correct owners and types already exist; the slice changes their contracts and deletes
adapter-local policy rather than adding another mechanism.

**Coalescence:** closest patterns are the selected naming/participation corrections already proven in local and
search-engine providers. Rebuild `VectorQueryOptions` as the single result-cardinality and validation owner so raw and
decorated paths share one contract; delete five provider default/max knobs, the dead Data Core duplicate, and
Weaviate's inert dimension branch. Pass the selected
factory/source into each native repository and delete generic naming calls. Correct each provider's existing
configuration owner rather than inventing an “HTTP vector provider” package: discovery orchestration is framework-wide
but native authentication, connection normalization, schema, and failure shapes differ, so a three-provider shared
runtime would be a name-based abstraction at the wrong specificity. Retain native repositories/translators; remove
false aliases, ignored settings, and misleading prose.

**Ergonomics:** application code does not grow. Humans/models get one stable rule—Top-K is the count requested, 10 only
when omitted—and endpoint configuration behaves as reported. IntelliSense loses dead caps/dimension and compatibility
aliases. Operators see exact redacted placement and participation-owned readiness. Adapter authors accept a compiled
query/naming decision and implement only native mechanics.

**Constraints satisfied:** no HTTP surface; Entity/Vector remains the application path; no new magic identifiers or
public abstraction; large row-data rules are untouched; exact provider limitations remain fail-loud; package
README/TECHNICAL, current Vector guidance, focused behavior, artifacts, audit, and generated truth move together; ADRs
remain dated and untouched; no release certification before R11-07.

**Risks:** changing nullable Top-K and removing options is intentionally breaking under the greenfield mandate;
provider suites require Docker images and may expose backend-version drift; static collection pins still defeat source
folds and must retain warnings; per-source endpoints remain explicitly unsupported; Weaviate's layered Zen Garden path
must stay inert unless the engine is active. Maturity cannot exceed focused evidence actually observed.

### External vector provider-family evidence

- `VectorQueryOptions` is now the sole result-cardinality contract: omitted Top-K is 10, non-positive values reject at
  construction (including `with` expressions), and 250 reaches the selected repository unchanged. The obsolete Data
  Core `VectorDefaultsOptions` duplicate and every connector default/cap are gone. Focused Core proof passes 10/10.
- Qdrant, Milvus, and Weaviate repositories receive the factory/source already selected by Vector and use the shared
  naming fold without re-entering election. Exact `Koan:Data:<Provider>:Endpoint` settings now configure real
  `AddKoan()` hosts without `PostConfigure` pins; generic Data fallbacks and casing/legacy aliases are removed.
- Qdrant and Milvus no longer guess a 1536-dimensional model. First write supplies collection dimension; explicit
  pre-creation requires an explicit dimension. Weaviate's inert dimension knob is deleted and a changed embedding
  dimension now fails before backend I/O. Milvus discovery now preserves/probes its actual REST endpoint rather than
  translating it into a nonfunctional gRPC-shaped URI.
- Real provider matrices pass: Qdrant 39 with 2 honest capability skips; Milvus 25 with 8 honest skips; Weaviate 34/34.
  The Weaviate isolation fixture also shed duplicate manual provider composition and now proves reference-driven module
  activation directly. Qdrant/Milvus do not claim hybrid search; Milvus additionally states its absent embedding read,
  export, continuation, stats, and immediate-delete guarantees.
- All three packages build and pack warning-free with their own README/TECHNICAL contract, canonical icon, provider
  DLL/XML, functional `Sylin.Koan.Data.Vector` dependency, and exact package metadata. Weaviate's optional layered
  integration depends only on `Sylin.Koan.ZenGarden.Contracts`. Current NuGet audit reports no known vulnerable direct
  or transitive packages.
- All three packages are structurally ready with zero objective findings. Generated truth contains 111 packages: 23
  repair-required, 40 review-required, and 48 structurally ready across 20 claims. Strict docs generation has no
  errors (the existing repository-wide front-matter warnings remain non-gating), and public documentation truth passes
  across 210 current files and 38 navigation targets.

The external vector provider family passes this R11-05 slice. No release candidate, full certification run, package
feed, or remote state was created; the complete release ratchet remains the single R11-07 boundary.

### AI runtime and local-inference provider-family discovery

**Task:** graduate the functional AI runtime with Ollama, LM Studio, and ONNX after replacing mutable, provider-owned
startup registration with one host-compiled activation plan. Move Hugging Face and the Zen Garden orchestrator onto
the same activation law so AI has one topology even though those packages retain separate product assessments.

**Application intent:** “Reference one AI provider, call `AddKoan()`, then use `Client.Chat`, `Client.Embed`, or the
Entity AI ring. With one eligible provider, Koan makes the obvious route work; explicit source/model intent wins; when
several choices remain meaningful, Koan asks for one routing decision rather than guessing.”

**Public expression:** the provider package reference brings the functional AI pillar. Application code does not call
provider registration extensions. Ollama and LM Studio discover their conventional local/container endpoints when no
placement is supplied; ONNX activates only when its model path is configured. Exact provider configuration remains
under `Koan:Ai:<Provider>` and standard `ConnectionStrings:<Provider>` where a single endpoint is meaningful.

**Guarantee/correction:** structural provider availability compiles once during `AddKoan()`. The AI runtime owns
activation order, duplicate identity rejection, adapter/source consistency, routing-index mutation, and startup
projection. Provider activators own only configuration, endpoint discovery, native/client construction, and native
capabilities. The routing registry never owns or disposes adapters; every registered adapter is rooted in DI and is
disposed exactly once with its host. Explicit placement never loses to discovery, and a referenced but presently
unavailable local runtime remains inspectable without manufacturing a healthy source.

**Complete intent surface:** provider references; `AddKoan()`; chat/embed/stream/model-management intent; optional
source, member, model, endpoint, credentials, request/concurrency limits, ONNX model/vocabulary paths, discovery
policy, readiness, startup facts, and URL overrides. Runtime registry mutation, contributor execution order,
registration timestamps, provider-specific registration extensions, duplicate configuration aliases, and legacy
Ollama reporting inside AI Core are not application decisions.

**Public concepts:** AI categories express business operation; sources express logical routing groups; members
express endpoints within a source; adapters translate the elected request to one native protocol; provider references
express availability; provider configuration expresses deliberate placement or native guarantees. Structural module
contribution and runtime provider activation are framework/module-author mechanics, hidden from ordinary IntelliSense.

**Docs read:** engineering and architecture guardrails require reference-as-availability, functional pillar ownership,
standard .NET configuration, immutable compiled decisions, package-owned companions, and Entity-first application
surfaces. Current AI package pages instead teach nonexistent `AddOllama`, `AddLMStudioFromConfig`, and legacy `Engine`
setup; claim retry/TLS/multi-instance weighting behavior not represented by current options; and describe Ollama as a
default backend even though the router elects sources by configured capability and priority.

**Code read:** AI currently has a mutable adapter registry with public `Add`/`Remove`, silent duplicate suppression,
registration-time ordering, and a second mutable source registry. Functional AI executes arbitrary
`IAiAdapterContributor` callbacks during `Start`; providers both discover and mutate those registries. Ollama and LM
Studio duplicate discovery gating, member/source construction, probing, capability mapping, policy defaults,
singleton HTTP/adapter creation, and boot narration. LM Studio additionally performs endpoint resolution three ways:
an options configurator, a Core discovery adapter, and its contributor. AI Core still parses and reports legacy
Ollama configuration. Provider packages reference only contracts, so a provider reference alone does not carry the
functional runtime whose startup callback it requires. ONNX creates a disposable inference session outside DI and the
registry has no defensible lifetime contract; this is the recorded PMC-030 defect.

**Constants/options/DTO inventory:** stable AI capability, request/result, source/member, route, and adapter contracts
already live in `Sylin.Koan.AI.Contracts`. Core already owns the generic semantic-contribution compiler and provider
identity catalog. Each connector owns typed options and provider constants, but Ollama omits its endpoint/source shape
from options while LM Studio maintains long/short/generic aliases and unused Weight/Labels. No new public application
DTO or registration extension is needed.

**Reusing:** Core semantic contribution compilation; `KoanModule`; `ProviderCatalog<T>` identity mechanics; existing AI
request/result, source/member, category/router, health, provenance, provider adapters, service discovery, Zen Garden
contracts, focused AI unit/bootstrap suites, package compiler, and public truth gate.

**Creating new:** one AI-owned contribution target, immutable activation plan, and provider-activation result contract.
These replace the callback collection and public registry mutation rather than forming a parallel provider system. A
small framework-facing scheduling extension may expose Core's existing contribution compiler to functional pillars;
it must remain hidden from application IntelliSense and preserve one compiler per target.

**Coalescence:** compile provider activator identities/types through the existing generic contribution engine at
`AddKoan()` time. At `Start`, the AI runtime resolves the plan in deterministic order, asks each provider for a
DI-owned adapter plus zero or more discovered/configured sources, validates the whole contribution, then commits it
through one registry chokepoint. Delete `IAiAdapterContributor`, registry removal, silent duplicate behavior, and
registration timestamps. Use Core discovery once per HTTP provider instead of retaining provider-local competing
election machinery; retain native probes/protocols and source-member semantics where they change actual routing.

**Ergonomics:** ordinary application code shrinks to reference + `AddKoan()` + business call. Module authors implement
one provider activator and declare it from their existing `KoanModule`; they do not mutate registries, invent module
IDs, or coordinate startup ordering. Humans/models get one routing vocabulary and corrective ambiguity. Operators see
which providers were available, activated, inactive, or rejected and why, with source/member placement and capability
truth projected from the same plan used at runtime.

**Constraints satisfied:** Entity/Client remain the public intent surfaces; no provider-specific application
registration ceremony; contracts stay isolated; no inert functional references; discovery remains layered and only
activates when its engine is present; explicit intent fails correctively; ADRs remain dated and untouched; focused
proof only during the slice; no release certification before R11-07.

**Risks:** changing module and registry contracts is intentionally breaking under the greenfield mandate; existing
tests that mutate registries after host startup must become real test providers or use an internal builder before plan
freeze. Ollama/LM Studio discovery must not turn endpoint absence into startup failure. ONNX must dispose one native
session exactly once across repeated hosts. Zen Garden activation must remain inert without its runtime and must not
capture a stale endpoint. Maturity cannot exceed focused behavior observed in this slice.

### AI runtime and local-inference provider-family evidence

- Core's existing semantic-contribution compiler now exposes one framework-facing, IntelliSense-hidden scheduling
  seam. AI uses it to compile one deterministic provider plan from referenced modules. Duplicate provider ids/types,
  adapter identity drift, non-DI adapter instances, and source/provider disagreement reject composition before the
  adapter catalog or provider sources are published.
- The public adapter registry is read-only and the callback/registration model is gone: no `Add`, `Remove`, silent
  duplicate suppression, timestamps, weights, descriptor attribute, or `IAiAdapterContributor` remains. Providers
  return one DI-owned singleton plus source descriptions; AI owns validation and the single commit boundary. The
  isolated host proof confirms provider references alone bring functional AI, explicit placement survives discovery
  being off, and a disposable adapter is disposed exactly once.
- Ollama and LM Studio now share Core discovery, the AI-owned endpoint-source builder, exact provider option sections,
  standard `ConnectionStrings:<Provider>`, deterministic member naming, and the same explicit-versus-automatic law.
  LM Studio's configurator, second probe loop, orchestration evaluator, no-op registration extension, generic aliases,
  fictitious container promise, and provider-local weighting metadata are deleted. It is truthfully an external
  runtime. Ollama's duplicated probe/source builder is likewise deleted.
- Explicit endpoint meshes are authoritative in every environment and conflict with a simultaneous connection string.
  Invalid or unresolved explicit intent fails startup correctively; an absent automatic runtime is normal inactivity.
  The AI concern no longer translates provider options into a shadow legacy `Default` Ollama source.
- ONNX remains safely inert without `ModelPath`. Configuring a model is explicit intent: missing model/vocabulary now
  fails boot instead of being swallowed. The adapter and native `InferenceSession` are DI-owned and disposed with the
  host. Ollama and LM Studio memoize their per-endpoint clients and own their disposal.
- Hugging Face and the Zen Garden orchestrator now obey the same compiled provider-activation law without receiving a
  maturity promotion. The Zen Garden AI connector remains inert unless its functional engine resolves an offering,
  aligns source/provider identity, owns its client lifetime, and refuses to advertise a capability catalog it could
  not observe. Their distinct package/docs/product assessments remain future slices.
- `DiscoveryContext.RequiredCapabilities` is the neutral typed handoff for layered sources. Ollama and LM Studio
  declare model/capability requirements once; the Zen Garden discovery contributor turns them into capability-bearing
  offering intent and keeps wish scheduling inside the layered engine. Focused Zen Garden discovery passes 3/3.
- The complete focused AI unit project passes 160/160; AI integration passes 49/49; clean-host provider activation
  passes 2/2. The affected Data.AI project compiles and passes 77/87; all ten host-start failures are the already
  recorded PMC-033 eager unused-Storage activation, before the AI test body, and are not hidden or charged to this
  provider slice. The bootstrap infrastructure project builds with only the recorded PMC-032 stale test reference.
- Release packs for AI Contracts, AI Core, Ollama, LM Studio, and ONNX contain their DLL/XML, package-owned README,
  canonical icon, and build-transitive composition metadata. Current NuGet audit reports no known vulnerable direct
  or transitive package for all five. All five are structurally ready with no objective quality findings.
- Public package pages now teach reference plus `AddKoan()` plus `Client`, exact placement and failure semantics, and
  honest runtime/model boundaries. The `local-ai-provider-composition` claim is conservatively `demonstrated`; broad
  AI/vector semantics remain `experimental`. Generated truth contains 111 packages: 22 repair-required, 37
  review-required, and 52 structurally ready across 21 claims. Public documentation truth passes across 212 current
  files and 38 navigation targets.

The AI runtime and local-inference provider family passes this R11-05 slice. No release candidate, full certification
run, package feed, or remote state was created; the complete release ratchet remains the single R11-07 boundary.

### AI semantic-input and HTTP-projection discovery

**Task:** graduate the prompt semantic and AI Web projection after separating the always-available prompt value from
the optional Entity-backed catalog, then make a Web package reference fully express endpoint availability without an
extra registration call.

**Application intent:** “Compose an inspectable prompt and send it through Koan AI without activating Data. If the
application deliberately references the prompt-catalog package, store and resolve versioned prompts as Entities. If it
references the Web projection, expose the provider-neutral AI HTTP surface through ordinary Koan Web composition.”

**Public expression:** `Prompt.Parse(...)` and `Prompt.Create(...)` remain the in-memory semantic value used by
`Client.Chat(...)`. `Sylin.Koan.AI.Prompt` earns a narrower, optional reference intent for `PromptEntry` and catalog
resolution. `Sylin.Koan.AI.Web` plus `AddKoan()` makes its controllers and health participation available; no
`AddKoanAiWeb()` ceremony is an application responsibility.

**Guarantee/correction:** functional AI depends only on inert AI contracts for its prompt value and never activates
Entity/Data merely because chat is available. Catalog lookup is owned by the optional catalog package and fails with
the requested prompt identity when no active/versioned entry exists. AI Web registers its controller application part
and health participant exactly once through its `KoanModule`; HTTP operations use the same compiled provider registry
and pipeline as in-process calls. Provider exceptions are not silently converted into invented success or capability.

**Complete intent surface:** raw and structured prompts, variables/defaults, constraints, examples, structured-output
shape, immutable modification, optional catalog name/version/selection, AI HTTP health/capabilities/models/chat/
stream/embed/OCR/model-management routes, cancellation, startup facts, and provider-native failure. Assembly placement,
manual hosted-service registration, MVC application-part mechanics, and a transitive Data runtime are not application
decisions.

**Public concepts:** a prompt is an immutable AI request semantic; a prompt entry is an Entity-backed application
record; a catalog resolves deliberate persisted identity; AI Web is a provider-neutral projection. These are separate
DDD/SoC spaces even when the same application uses all three.

**Docs read:** architecture and engineering guidance require business-intent APIs, reference-as-availability, inert
contracts, standard .NET composition, one owner per decision, and truthful package-owned docs. The current prompt page
mixes value construction and unproved random rollout/version storage, shows nonexistent overloads and `{{variable}}`
syntax, and makes AI transitively reference Data. The current AI Web README is only an install fragment; its technical
page promises configurable route bases, quotas, CORS, authentication codes, timeouts, and backpressure that no code or
options implement. The current public AI reference page is marked current while teaching nonexistent package IDs,
APIs, options, and provider behavior, and is not navigated from the public TOC.

**Code read:** four prompt value files are BCL-only, but they share an assembly with `PromptEntry : Entity<>`,
`PromptStrategy`, and catalog lookup. `Koan.AI` references that functional package solely for `Client.Chat(Prompt)`, so
every AI provider inherits Data Core. Catalog loading has no direct behavior test; random A/B/canary selection has no
stickiness or routing context. AI Web has no `KoanModule`, controller-discovery registration, or owned tests; its only
extension registers a hosted health subscriber and must be called manually despite the framework's reference-intent
law. Its controller catches all model-list failures and its technical prose describes options and guarantees absent
from the assembly. The closest correct projection pattern is `MediaWebModule`/`CanonWebModule` using
`AddKoanControllersFrom<T>()` and provenance from the same module.

**Constants/options/DTO inventory:** AI request/result/provider DTOs and route-hint contracts already live in
`Sylin.Koan.AI.Contracts`; the prompt value needs no Data or runtime service. AI Web owns one internal route vocabulary
and consumes existing AI DTOs. No route-options type, provider registry, prompt-contract package, or new application
registration extension is justified.

**Reusing:** existing prompt value/builder/output types and unit/integration cases; `PromptEntry` Entity semantics;
AI contracts, pipeline, adapter registry, health aggregator, `KoanModule`, `AddKoanControllersFrom<T>()`, provenance,
the isolated provider host pattern, package compiler, and public truth gate.

**Creating new:** one catalog facade in the existing optional Prompt package and one `AiWebModule` in the existing Web
package. Both replace misplaced/static registration ownership; neither creates a package or parallel mechanism.

**Coalescence:** move the BCL-only prompt semantic into AI Contracts while retaining its public namespace, remove the
functional Prompt reference from AI, and make the optional package own only Entity catalog behavior. Replace
`AddKoanAiWeb()` with a module that registers controllers and health participation once. Delete or rebuild stale public
AI guidance from the surviving code contract; do not preserve rollout claims, knobs, or error mappings that have no
implementation.

**Ergonomics:** ordinary AI code remains prompt construction plus `Client.Chat`; applications that never persist
prompts lose an accidental Data dependency. Catalog users add one package and call one intent-named catalog surface.
Web users add one package and keep only `AddKoan()`. IntelliSense groups value composition, persistence, and projection
at their real boundaries; agents do not infer setup calls or configuration that do not exist; operators see one
module-owned route/participation report.

**Constraints satisfied:** no new package or bespoke identifier; contracts remain inert; Entity is used only for the
optional persisted concern; Web is a thin projection over the AI runtime; standard MVC/DI/module mechanics are reused;
ADRs remain dated and untouched; focused proof only during the slice; no release certification before R11-07.

**Risks:** moving types between assemblies changes binary ownership even when namespaces stay stable; removing static
`Prompt.Load` and random rollout helpers is intentionally breaking under the greenfield mandate; catalog tests must
use one isolated Data host because Entity resolution is process-global; Web tests must prove real controller discovery,
not direct controller construction. Public docs must distinguish exposed HTTP transport from authorization, quota,
and production-edge guarantees Koan AI Web does not currently supply.

### AI semantic-input and HTTP-projection evidence

- The BCL-only `Prompt`, builder, examples, and output-shape types now live in inert AI Contracts with their public
  namespace preserved. Functional AI no longer references the Entity-backed Prompt package: its release artifact
  depends only on AI Contracts, Core, and standard Microsoft extensions, so chat availability does not activate Data.
- `Sylin.Koan.AI.Prompt` now has one earned reference intent: optional Entity-backed named/versioned storage.
  `PromptCatalog.Load(name)` resolves the newest active entry; the exact-version overload is deliberate and status
  neutral. Blank identity, invalid version, and missing entries reject correctively. Unproved random A/B/canary
  selection and the static `Prompt.Load` fusion are deleted. Prompt value/catalog proof passes 26/26 through a real
  `AddKoan()` host and InMemory Entity provider; focused prompt integration passes 7/7.
- `AiWebModule` is the single Web activation owner and uses the standard controller application-part seam. The manual
  `AddKoanAiWeb()` extension and duplicate health subscriber are deleted; AI Core remains the health owner. A package
  reference plus `AddKoan()` exposes the real controller surface, and a providerless host reports the projection as
  `Inactive` rather than unhealthy. The isolated TestServer proof passes 1/1.
- Adapter/capability inspection now projects each adapter's declared capability set. Model inventory returns explicit
  provider failures alongside partial results instead of swallowing every exception. Public prose no longer promises
  authentication codes, quotas, route options, backpressure, retry, budget, fallback, OpenAI providers, or error
  normalization absent from current code.
- AI Unit remains green at 160/160 and the dependent Orchestration build is warning-clean after its catalog call moves
  to `PromptCatalog`. Prompt and Web are terminal `keep` boundaries: the former owns optional persisted identity; the
  latter owns HTTP projection; neither duplicates AI runtime/provider policy.
- Release packs for AI Contracts, AI, Prompt, and AI Web contain their DLL/XML, package-owned README, canonical icon,
  and build-transitive composition metadata. Inspected nuspecs prove AI shed its Prompt/Data dependency while Prompt
  owns AI Contracts/Core/Data Core and Web owns AI/Contracts/Web/SSE explicitly. Current NuGet audit reports no known
  vulnerable direct or transitive package for all four.
- Prompt and AI Web are structurally ready with zero objective findings. Generated truth contains 111 packages: 22
  repair-required, 35 review-required, and 54 structurally ready across 22 claims. Public documentation truth passes
  across 214 current files and 40 navigation targets; the stale current AI pillar and agentic-code-generation pages
  were replaced or removed rather than patched around legacy APIs.

The AI semantic-input and HTTP-projection family passes this R11-05 slice. No release candidate, full certification
run, package feed, or remote state was created; the complete release ratchet remains the single R11-07 boundary.

## AI retired-topology discovery

**Task:** implement the already accepted AI topology for the two boundaries that have no earned Koan V1 product
intent: the providerless Training facade and the legacy Zen Garden AI connector.

**Application intent:** no application should reference a package that advertises training while having no training
runtime, or a Koan-side connector for orchestration mechanics now owned by the external Zen Garden product. Removing
both false choices makes package selection smaller and semantically honest.

**Docs read:** the greenfield package mandate requires a distinct, meaningful reference intent and current executable
proof. The accepted AI topology assigns training/evaluation workflows to Agyo and model/resource orchestration to Zen
Garden. Historical assessments and ADRs remain evidence and are not rewritten as current product guidance.

**Code read:** `Sylin.Koan.AI.Training` has no in-repository `ITrainingRuntime` implementation or behavior consumer;
its list path returns an empty result and its estimates/runtime paths are placeholders. Its only external references
are unused project references in two broad AI test hosts. `Sylin.Koan.AI.Connector.ZenGarden` has no tests or source
consumer and is already absent from the solution; its adapter forwards all AI capabilities to orchestration mechanics
owned by the sibling Zen Garden repository.

**Constants/options/DTO inventory:** AI Contracts Shared retains dependency-free lifecycle exchange vocabulary because
that is an accepted cross-repository contract. Capability tokens are not deleted in this slice. No replacement Koan
runtime, compatibility facade, forwarding package, deprecation attribute, or inert activation mechanism is justified.

**Closest pattern:** the implemented `Core.Adapters`, Cache analyzer, relational Dapper, and CLI Core retirements remove
an unearned package identity, references, solution membership, generated inventory, and current public claims while
preserving dated architecture evidence.

**Coalescence and placement:** training workflow ownership belongs outside Koan rather than behind an empty facade.
Zen Garden owns its own orchestration boundary rather than requiring a reverse connector in Koan. The remaining Koan
AI runtime stays provider-neutral; shared lifecycle contracts remain inert at the existing contracts boundary.

**Ergonomics:** developers and agents see fewer install choices and cannot mistake a compilable facade for a supported
workflow. Operators no longer receive a provider identity whose operational contract belongs to another product.
Future integrations must enter through an explicit, evidenced cross-repository contract rather than resurrecting
either retired package.

**Constraints satisfied:** no sibling repository mutation; no new package or compatibility mechanism; no ADR edits;
only focused dependent builds and generated-truth checks during the slice; release certification remains reserved for
R11-07.

**Risks:** removal is intentionally source- and package-breaking under the Koan 1.0 greenfield mandate. Current README
cross-links and one model-service error mention Training and must be corrected so no surviving surface directs users
to an absent API. Generated inventories must prove both package IDs disappear.

**Dependent-test correction:** the focused EndToEnd compile exposed a stale fixture that still attempted to mutate the
now read-only production `IAiAdapterRegistry`. The fixture now supplies its mutable fake through standard DI before
`AddKoan()` and keeps mutation on that test-owned concrete type. Production provider-plan ownership remains immutable;
no compatibility method was restored to make an obsolete test compile.

### AI retired-topology evidence

- `Sylin.Koan.AI.Training` and `Sylin.Koan.AI.Connector.ZenGarden` are absent from source project ownership, the
  solution, active ProjectReferences, and the regenerated quality/product-surface inventories. No forwarding or
  compatibility package replaces them.
- Shared lifecycle DTOs remain in inert AI Contracts Shared. Surviving model behavior no longer tells users to call
  the absent Training facade; unsupported LoRA merge instead requests a model adapter that declares merge support.
- AI Models, the AI Integration host, and the AI EndToEnd host build warning-clean. The corrected EndToEnd adapter
  resolution cell passes 7/7 using a DI-supplied test registry without reopening production mutation.
- Generated truth contains 109 packages: 21 repair-required, 34 review-required, and 54 structurally ready across 22
  claims. Public documentation truth passes across 213 current files and 40 navigation targets. No release candidate,
  artifact, package feed, sibling-repository mutation, or full certification run was created.

The two accepted retirements pass this R11-05 slice. The remaining AI vertical still requires explicit terminal
topology and cross-repository ownership decisions before any package prose is polished.

## AI compute-topology discovery

**Task:** determine whether the accepted cut-in-favor-of-Zen-Garden decision for `Sylin.Koan.AI.Compute` can be
implemented independently without moving or losing a real Koan application capability.

**Application intent:** applications that need hardware inventory or workload placement should use the product that
owns garden resources. Koan should not offer a package called a compute fabric when it only probes the local machine
and cannot satisfy network, model, or non-local placement requirements.

**Docs read:** ARCH-0089 assigns Compute to a cut in favor of Zen Garden and requires committed resource coverage to
be confirmed before deletion. Koan's package law requires executable guarantees rather than aspirational names. The
current README advertises fleet discovery and workload routing beyond the implementation.

**Code read:** Compute has no application, sample, production-module, or package consumer. Its only consumer is a
six-cell EndToEnd test file. `Fleet()` always contains one local resource; network readiness is hard-coded false;
required-model readiness is hard-coded false; capability checks assume only inference; and `Resolve()` returns the
local resource as `Target` even when it explicitly does not satisfy the requirement. Local probing shells out and
swallows all failures, so it cannot support the advertised guarantee or operator truth.

**Sibling evidence:** read-only inspection of Zen Garden's committed `HEAD` confirms hardware inventory, two-tier
capabilities, GPU/VRAM facts, garden inspection, resource collection, load, election, and AI-orchestrator resource
domains. The sibling worktree is dirty and remains untouched; only committed paths and symbols were inspected.

**Constants/options/DTO inventory:** `Accelerator`, `ComputeLocation`, and `ComputeRequirement` remain in dependency-free
AI Contracts Shared and have their own contract tests. They are cross-repository exchange vocabulary, not evidence
that Koan must own a compute runtime. Compute-specific resource, resolution, service, status, and readiness types have
no external source consumer and leave with the false package.

**Closest pattern:** the just-completed Training retirement removes a package whose name exceeds its mechanics while
retaining independently useful inert lifecycle contracts. This cut likewise removes the functional promise without
inventing a forwarding shim or making Koan depend upward on Zen Garden.

**Coalescence and placement:** Zen Garden is the one resource/hardware owner. Koan AI consumes provider capabilities
for inference but owns no garden-wide compute plane. There is no replacement Koan service, adapter, HTTP client, or
static facade in this slice.

**Ergonomics:** developers and agents lose a misleading install choice and an API that reports fallback as placement.
Operators are no longer shown a locally guessed singleton as a fleet. Applications needing this concern choose the
explicit Zen Garden boundary; applications using Koan AI inference change nothing.

**Constraints satisfied:** standard package removal expresses the decision; shared contracts stay inert; no sibling
mutation, compatibility package, new identifier, ADR edit, release artifact, or broad certification run.

**Risks:** this is intentionally package- and source-breaking. Current cross-links, solution membership, the one test
file, generated inventories, and topology ledgers must all leave together. The surviving AI vertical is not implied
safe to remove: Eval/Review/Agents/Orchestration contain real capability awaiting Agyo migration, while Models and
Hugging Face await the larger Zen Garden port.

### AI compute-topology evidence

- `Sylin.Koan.AI.Compute` and its sole Compute-specific test consumer are absent from source ownership, the solution,
  surviving references, and regenerated package/product inventories. No forwarding service or compatibility facade
  replaces it.
- The surviving AI EndToEnd host builds warning-clean with the false capability removed. AI Contracts Shared passes
  9/9, proving dependency-free accelerator/location/requirement exchange vocabulary remains available without a Koan
  compute runtime.
- Generated truth contains 108 packages: 21 repair-required, 33 review-required, and 54 structurally ready across 22
  claims. Public documentation truth passes across 212 current files and 40 navigation targets. No sibling worktree,
  release artifact, package feed, or remote state was mutated, and no full certification run occurred.

The accepted Compute cut passes this R11-05 slice. The safe local retirements are complete; the remaining vertical
must wait for evidenced Agyo/Zen Garden destination work rather than being deleted or polished prematurely.

## AI cross-repository handoff gate

The remaining AI topology is now terminal without pretending the migration is complete:

- Koan keeps provider-neutral semantic inference, Entity-first Data.AI, prompt values/catalog, HTTP projection, and
  the Ollama, LM Studio, and ONNX inference batteries.
- Agents plus Orchestration retire only after their behavior is green inside `Agyo.Rag`.
- Eval and Review retire only after `Sylin.Agyo.Eval` and `Sylin.Agyo.Review` exist with their current behavior and
  tests. Read-only inspection confirms neither destination exists today.
- Models plus Hugging Face retire only after Zen Garden owns equivalent model lifecycle and Hub behavior and the
  Entity-backed catalog boundary has been deliberately decoupled.

The Agyo repository is clean and already contains substantial Rag abstractions, ingestion, retrieval, evaluation,
health, jobs, tools, and tests, but no migrated Koan agent loop or standalone Eval/Review package. Zen Garden remains
dirty and untouched. Therefore no remaining vertical code is deleted, documented as a permanent Koan V1 product, or
support-promoted in this cycle. R08-05 treats these package identities as a release blocker, not as review debt.

This handoff gate prevents two equally harmful outcomes: losing real capability before its destination exists, or
spending Koan product-polish effort on packages already assigned to another product. Future work resumes from the
destination evidence, not from another Koan-side architecture assessment.

## MCP family discovery

**Task:** graduate MCP Core, Explorer, and Operations as one agent-facing progression, first deciding whether the two
leaf packages earn separate reference intent and whether any public/internal ceremony should coalesce.

**Application intent:** “Reference MCP and describe an Entity or business tool; `AddKoan()` exposes a caller-honest
agent surface. Optionally reference Explorer for a human console over that same projection, or Operations for
explicitly enabled, grant-gated Jobs/Cache control-plane verbs.”

**Public expression:** `[McpEntity]` and `[McpTool]` remain the business declarations. The package reference plus
`AddKoan()` is the only application registration path. Explorer is a reference-selected human projection with a
Development-safe default. Operations is layered availability: reference makes its dangerous toolsets available,
configuration explicitly enables each one, an `AgentGrant` authorizes the caller, and `confirm:true` settles a
destructive invocation.

**Guarantee/correction:** tool listing, `koan://self`, Explorer, and dispatch share one caller-aware projection and the
same governed Entity endpoint seam. Explorer's privileged access map fails closed outside Development. Operations
tools are absent while disabled, fail loudly without the exact grant, dry-run destructive calls without confirmation,
and audit mutations. STDIO remains trusted local transport for ordinary tools but cannot invent an operational
subject/grant.

**Complete intent surface:** Entity and custom tools, schemas, resources, caller-specific visibility, dry-run,
Code Mode, STDIO and Streamable HTTP, explicit legacy transport, sessions/auth/resource identity, Explorer map/try-it,
and Jobs/Cache operational verbs. Manual service registration, endpoint mapping, duplicate transport policy, and
package-internal discovery are not application decisions.

**Docs read:** current MCP public guides correctly teach reference plus `AddKoan()` and forbid `AddKoanMcp()`, but the
method remains public and multiple tests still use it. Core's package page lacks the exact package title/install and a
recognized limitations section. Explorer and Operations have no owned README or technical companion. Operations'
project description calls reference intent while also saying every feature defaults off, without explaining layered
security activation. Core boot reporting ends with a permanent “diagnostics unavailable” note; Explorer reports only
its version; Operations reports enabled toolsets but not the available/default-off posture.

**Code read:** MCP Core is a substantial, evidenced runtime with conformance, streamable transport, relationship,
auth, custom-tool, and code-mode suites plus the GoldenJourney consumer. Explorer owns only embedded human UI,
caller-map, privileged access-map, and in-process try-it projection. Operations alone pulls Jobs and Cache and owns the
grant/confirm/audit control-plane policy. Merging either leaf into Core would add unrelated UI or operational weight
to the shortest agent path. The only duplicate registration owner is public `AddKoanMcp()`, called by `McpModule` and
test fixtures despite being explicitly unsupported for applications.

**Constants/options/DTO inventory:** `McpServerOptions` already owns transport, exposure, code-mode, entity policy,
and generic operational-toolset enablement. Explorer owns only its enabled/admin options. No new options hierarchy,
provider engine, identifier attribute, or package is needed. Existing `KoanModule`, DI, endpoint-contributor,
provenance, and package-owned docs patterns are sufficient.

**Closest patterns:** AI Web removed its public manual registration extension and made its module the one activation
owner. Optional Web projections remain separate when their reference adds real surface/dependencies. Layered provider
capabilities may be available by reference but inactive until the dangerous mechanism is deliberately enabled.

**Coalescence and placement:** keep all three packages. Make `McpModule` the only public activation owner and reduce
the old extension to internal service composition with no configuration override path. Keep one Core options/policy
owner; keep Explorer and Operations thin projections. Improve each module's report rather than creating a second
facts registry.

**Ergonomics:** application code becomes exactly reference + declaration + `AddKoan()`, matching public guidance and
agent expectations. Explorer communicates whether/where it is live and who can inspect walls. Operations communicates
available versus enabled toolsets and the grant/confirmation posture. Package pages give developers and agents one
smallest result plus honest production/security boundaries.

**Constraints satisfied:** no package merge/split/new identifier; no weaker operational default; standard .NET DI and
configuration remain the mechanism; one module lifecycle per assembly; Core owns MCP policy while leaves own their
projection; focused family tests only; no release certification.

**Risks:** isolated test fixtures must move from the deleted public helper to canonical `AddKoan()` without silently
changing behavior. Explorer defaults depend on environment/container posture and must be reported truthfully.
Operations prose must distinguish package availability, configuration enablement, grants, and confirmation rather
than collapsing them into “reference equals execution.” Code Mode remains in Core for this slice; a split would add a
new install decision and is not justified without a measured size/security/use-case comparison.

## Acceptance

1. every active package receives a terminal R11-02 disposition before prose graduation;
2. no contracts package depends on a functional engine or projection solely for borrowed vocabulary;
3. every survivor has package-specific identity, install/reference expression, meaningful result, and boundaries;
4. focused behavior and artifact proof is proportional to its role;
5. generated quality and product-surface truth agree after every family slice;
6. the full release ratchet remains a single R11-07 boundary.
