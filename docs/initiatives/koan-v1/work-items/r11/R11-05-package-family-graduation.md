---
type: SPEC
domain: framework
title: "R11-05 - Graduate Package Families"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-18
framework_version: source-first
validation:
  date_last_tested: 2026-07-18
  status: in-progress
  scope: dependency-ordered family graduation; Canon implementation and focused package proof complete
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

### MCP family evidence

- `McpModule` is now the only public activation owner. The manual `AddKoanMcp()` and `MapKoanMcpEndpoints()` surfaces
  are internal mechanics; every affected fixture uses canonical `AddKoan()` and the endpoint-contributor path.
- Core conformance passes 75/75, Explorer 16/16, Operations 5/5, and Code Mode 27/27. These cover caller projection,
  direct-invocation enforcement, resources/self-description, browser negotiation, access-map gating, real Jobs ledger,
  exact grants, dry-run, audit, and supported composition.
- Core reports effective MCP configuration without the permanent false “diagnostics unavailable” note. Explorer
  reports inactive versus route/access-map posture. Operations reports available and enabled toolsets plus exact
  grant/destructive-confirm requirements.
- Package-owned Core, Explorer, and Operations pages state install, smallest result, layered activation, production
  security, and unsupported guarantees. All three are structurally ready with zero objective findings.
- Exact-HEAD packages contain DLL/XML, package-owned README, canonical icon, and build-transitive composition metadata.
  The inspected graph keeps Explorer dependent only on MCP while Operations explicitly adds Cache, Jobs, and MCP.
  Current NuGet audit reports no known vulnerable direct or transitive packages for all three.
- Generated truth contains 108 packages: 19 repair-required, 32 review-required, and 57 structurally ready across 22
  claims. Public documentation truth passes across 216 current files and 40 navigation targets. No release candidate,
  feed, remote state, or full certification run was created.

The MCP family passes this R11-05 slice. Core, Explorer, and Operations are terminal `keep` boundaries with less public
ceremony and more truthful activation/inspection.

### Web projection family discovery

**Task:** Graduate `Sylin.Koan.Web.Extensions`, `Sylin.Koan.Web.OpenApi`, and `Sylin.Koan.Web.Sse` by rebuilding the
host-composition, OpenAPI, and stream-result chokepoints before publishing their package contracts.

**Application intent:** “Expose richer Entity HTTP behavior, describe the resulting API, and stream live results by
adding only the package reference and the smallest business-facing declaration.”

**Public expression:** Each reference composes through the application's existing `AddKoan()` call. Web Extensions
keeps `[RestEntity] public sealed class Todo : Entity<Todo>;` as the terse REST projection and the existing
`EntityAuditController<T>`, `EntityModerationController<T>`, and `EntitySoftDeleteController<T>` realizations for
explicit richer surfaces. OpenAPI requires no application code: the reference publishes the OpenAPI document and, in
Development, its UI. SSE becomes one expression from a controller action: `return Sse.Stream(events);`, whether the
stream contains typed values, text, or explicit `SseEnvelope` values.

**Guarantee/correction:** Generic controller registrations are owned by one service collection/host and cannot leak
into another host; an explicit `EntityController<T>` still wins over `[RestEntity]`. OpenAPI has one option model, one
startup owner, one document route, and one UI policy; its document remains faithful to the Newtonsoft REST wire. The
UI defaults on only in Development and, when explicitly enabled elsewhere, fails closed for an unauthenticated request
unless the application deliberately disables that requirement. SSE writes one normalized wire contract through both
MVC and framework transport contexts, flushes each frame, and follows request cancellation; it does not claim replay,
delivery, buffering immunity, or heartbeat behavior it does not implement. Invalid or unsupported configuration is
reported with the effective posture rather than silently selecting a legacy path.

**Complete intent surface:** Beyond the package reference and existing `AddKoan()`, Web Extensions requires only the
exposure attribute or explicit capability-controller declaration the business surface needs. OpenAPI requires no code;
configuration is an optional deliberate override. SSE requires only returning `Sse.Stream(...)` from a controller.
Authentication/authorization remains an application guarantee and is required when an OpenAPI UI is deliberately
published outside Development.

**Public concepts:** `[RestEntity]` means terse full-CRUD exposure; explicit capability controller bases mean audit,
moderation, or soft-delete HTTP intent; `KoanOpenApiOptions` carries the real document/UI choices; `Sse` is the single
stream factory; `SseResult` bridges ASP.NET MVC and framework transport execution without two helper vocabularies;
`SseEnvelope` carries explicit event/id/retry fields. Each concept maps to a distinct application or wire decision.

**Docs read:**

- `docs/architecture/principles.md` makes business-to-code mapping, reference-as-availability, host-owned composition,
  semantic honesty, startup explanation, and standard .NET reuse binding constraints.
- `docs/reference/web/http-api.md` establishes controller-first Entity HTTP behavior and the existing paging,
  transformer, and error contract that this slice must not disturb.
- `docs/decisions/WEB-0035-entitycontroller-transformers.md` establishes that OpenAPI must describe actual negotiated
  REST media types without duplicating controller behavior.
- `docs/engineering/index.md` requires controller routes, typed options/constants, per-package README/TECHNICAL pages,
  and focused validation.
- `src/Koan.Web.Extensions/README.md` is relevant but materially inaccurate: it names nonexistent configuration and
  public types and does not state installation or honest boundaries.

**Code read:**

- `WebExtensionsModule`, `RestEntityRegistration`, and `GenericControllers` discover/materialize terse and explicit
  generic controllers, but store registrations in a process-static dictionary rather than host-owned composition.
- `EntityAuditController`, `EntityModerationController`, and `EntitySoftDeleteController` are real optional capability
  projections; their existing controller actions and Entity/data semantics remain the package's earned reference value.
- `OpenApiModule`, `KoanOpenApiStartupFilter`, `SwaggerBootstrap`, and `KoanSwaggerStartupFilter` currently divide one
  product decision across two option sections, two startup filters, duplicate mapping paths, and legacy public wiring.
- `KoanOpenApiOptions` is the natural single public owner, but currently omits UI route/security decisions while the
  legacy `KoanWebSwaggerOptions` carries them separately.
- `SseResults` and `SseActionResult` duplicate projection, headers, normalization, writing, and flushing; only their
  ASP.NET result interface differs. Current AI/SnapVault consumers use MVC while MCP uses the infrastructure result.

**Reusing:** `KoanModule`, `AddKoanOptions<T>`, ASP.NET MVC `IActionResult`, ASP.NET `IResult`, OpenAPI document and
operation transformers, `SseEnvelope`, `SseFormatter`, request cancellation, and the existing explicit-controller
precedence rule already exist. Existing constants/options are retained only for settings that actually affect runtime.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| Host-owned generic controller registry | `src/Koan.Web.Extensions/GenericControllers/GenericControllerRegistry.cs` | One composition instance shared by the module, feature provider, convention, and service-collection extensions; no process-static host leakage. |
| Unified OpenAPI document/UI runtime | `src/Koan.Web.OpenApi/Hosting/KoanOpenApiStartupFilter.cs` and `Options/KoanOpenApiOptions.cs` | The OpenAPI pillar owns its document, UI, security posture, mapping, and reporting in one standard ASP.NET startup path. |
| `Sse` factory | `src/Koan.Web.Sse/Sse.cs` | One discoverable application expression maps typed, text, and envelope streams to the wire. |
| `SseResult` | `src/Koan.Web.Sse/SseResult.cs` | One execution engine implements both required ASP.NET result contracts and centralizes response semantics. |
| Host-isolation/OpenAPI-policy/SSE-unification specs | Existing focused Web test projects | Pin the corrected guarantees without creating another test topology. |

**Coalescence:** The closest patterns are `GenericControllers`, the two OpenAPI startup filters, and the two SSE result
helpers. Specificity is the Web capability/package family: Core is too wide, individual controllers/transports are too
narrow, and application code must not own framework assembly mechanics. Rebuild the generic registry around one
host-owned instance; absorb all Swagger behavior into `OpenApiModule` and its one option/filter; rebuild SSE around one
factory/result pair. Delete the static registration dictionary, legacy `AddKoanSwagger`/`UseKoanSwagger`, the second
Swagger option/config/provenance family and startup filter, manual `AddKoanSse`, duplicated `SseResults`/
`SseActionResult`, and unused SSE enablement/heartbeat claims.

**Ergonomics:** Humans and coding models see one intent per reference: decorate/realize a richer Entity surface, inspect
OpenAPI, or `Sse.Stream(...)`. IntelliSense begins at a business noun rather than hosting extensions. Optional policy
appears only when it changes exposure/security. Operators see document route, UI route, enablement, and authentication
posture at boot; no setting is reported unless runtime honors it.

**Constraints satisfied:**

- Entity-first behavior and the base `EntityController<T>` contract remain unchanged.
- Application HTTP remains controller-first; OpenAPI/MCP transport mapping is framework infrastructure, not public
  application endpoint guidance.
- Stable configuration keys live in project constants and tunables in typed options.
- No placeholder or compatibility surface survives the greenfield cut.
- Large Entity reads are not introduced; existing capability-controller paging remains explicit.
- Package README/TECHNICAL pages, current reference truth, and the R11 matrix will be updated after focused proof.

**Risks:** OpenAPI UI authentication must remain middleware-order-safe across Koan auth combinations; focused real-host
tests must prove Development default, explicit disable, non-Development fail-closed, and explicit opt-out. The public
SSE cut is intentional and requires all in-repository AI, MCP, and sample consumers to move atomically. Baseline focused
tests pass 111/111, 10/10, and 5/5; the first parallel baseline attempt only collided on shared MSBuild outputs, so all
subsequent build/test proof remains sequential.

### Web projection family evidence

- Web Extensions generic controller declarations now live in one registry per `IServiceCollection`; module,
  registration extensions, MVC feature provider, and route convention share that host-owned instance. Duplicate same-
  route declarations are idempotent and conflicting routes fail with an explicit-controller correction. The full
  focused suite passes 113/113, including host-isolation and conflict evidence.
- OpenAPI has one public option family, module, startup filter, configuration section, document route, UI route, and
  report. Legacy Swagger registration, pipeline, option, namespace, configuration, and provenance paths are deleted.
  Real-host evidence passes 12/12, covering Development default, explicit disablement, non-Development absence,
  authentication fail-closed, deliberate open override, document dominance, custom route alignment, application
  identity, and Newtonsoft wire fidelity.
- SSE now exposes one `Sse.Stream(...)` expression and one `SseResult` implementing both MVC and infrastructure result
  contracts. Duplicated helpers and unused enablement/heartbeat claims are deleted. SSE passes 5/5; AI Web passes 1/1;
  MCP conformance passes 75/75; SnapVault upload progress passes 3/3 after a forced restore corrected stale evaluated
  test state. AI Web, MCP, and SnapVault build warning-clean.
- All three packages own exact README/TECHNICAL contracts, have zero generated quality findings, and are terminal
  `keep` boundaries. Current public guidance contains no old Swagger or split SSE path; historical ADR/assessment
  records remain dated evidence rather than curriculum.
- Exact HEAD `a32d23a28` packages contain DLL/XML, package-owned README, canonical icon, and build-transitive metadata.
  Extensions depends on Core, Data abstractions/runtime, and Web; OpenAPI depends on Core/Web plus Microsoft OpenAPI and
  Swagger UI; SSE depends on Core/Web plus Newtonsoft. Current direct/transitive NuGet audits report no known
  vulnerabilities for all three.
- Generated truth contains 108 packages: 17 repair-required, 31 review-required, and 60 structurally ready across 23
  claims. Public documentation truth passes across 221 current files and 40 navigation targets. No feed, release
  candidate, tag, remote mutation, or full release certification run was created.

The Web projection family passes this R11-05 slice. It preserves three independently meaningful references while
reducing process-global state, startup owners, public activation concepts, result vocabularies, and false settings.

### Web edge projection discovery

**Task:** Graduate `Sylin.Koan.Media.Web` and `Sylin.Koan.Web.OpenGraph` around their smallest application intent,
and retire `Sylin.Koan.Web.Backup` unless its HTTP surface can earn a responsible independent product promise.

**Application intent:** “Define one media Entity and reference Media Web to serve its originals and recipes without a
registration incantation. Declare an Entity's social-card projection while Koan composes the application and let the
referenced OpenGraph package enrich matching HTML navigations. Keep backup/restore as a domain capability until an
HTTP control plane can make honest durability, cancellation, authorization, and resource guarantees.”

**Public expression:** The ordinary media path is `public sealed class Photo : MediaEntity<Photo>;` plus the package
reference and the application's existing `AddKoan()`; `AddMediaSource<Photo>()` remains only the meaningful override
when several media Entity types or a custom source make selection ambiguous. OpenGraph remains the business-readable
`SocialCards.For<Article>("/articles/{id}", Article.Get).Title(x => x.Title)` declaration inside `AddKoan(() => ...)`
or an application `KoanModule`; the package reference contributes the middleware automatically. No Web Backup
expression is promoted: its reference currently publishes unauthenticated destructive endpoints, permissive CORS,
placeholder results, process-local tracking, and tracking-only cancellation.

**Guarantee/correction:** Exactly one discovered `MediaEntity<T>` becomes the default access/tenant-scoped source; an
explicit `IMediaSource` or `AddMediaSource<T>()` always wins. Zero or several candidates produce a corrective source
selection error rather than a generic DI failure. Social-card declarations and their lifecycle plans belong to the
exact composing host, so one host cannot retain another host's resolvers/selectors and tests need no global reset.
OpenGraph remains inert without a configured shell or matching declaration. Web Backup is removed rather than
claiming operational guarantees its code and tests do not provide; `Koan.Data.Backup` remains independently intact.

**Complete intent surface:** Media requires only one concrete media Entity for the default path; multiple types or a
non-Entity source require one explicit source choice. OpenGraph requires the route, resolver, and only the metadata
selectors the application actually owns; shell/site defaults remain typed configuration. There is no manual
middleware call. Backup/restore runtime policy remains outside this projection slice and no replacement endpoint is
invented.

**Public concepts:** `MediaEntity<T>` is the stored media business object; `IMediaSource` is the necessary custom
source seam; `AddMediaSource<T>()` expresses ambiguity resolution, not framework activation. `SocialCards.For<T>`
expresses route-to-Entity projection and its fluent selectors express actual metadata decisions. No backup-specific
web DTO, tracker, notifier, Swagger filter, CORS helper, or fake persistence helper survives without an earned
guarantee.

**Docs read:**

- `docs/architecture/principles.md` requires reference-as-availability, host-owned composition, business-to-code
  mapping, startup explanation, and semantic honesty; it is binding for both retained projections.
- `docs/engineering/index.md` requires controller-owned routes, typed options/constants, focused tests, and owned
  package companions; it is binding for implementation and graduation.
- `docs/reference/media/index.md` establishes one source, Entity-layer access/tenant gating, recipe negotiation, and
  durable derivations; it currently overstates the need for manual registration in the one-Entity case.
- `docs/decisions/WEB-0070-opengraph-social-cards.md` preserves the accepted social-card semantics and historical
  middleware decision; current automatic pipeline contribution supersedes only its manual-use curriculum.
- The three package READMEs/technical pages reveal accurate Media constraints, OpenGraph's missing package-owned
  product contract, and Web Backup's unusually honest unsupported posture; the latter is evidence for retirement,
  not a documentation-only repair.

**Code read:**

- `MediaWebModule`, `MediaEntitySource<T>`, `MediaController`, and the Media Web tests show a sound Entity-gated
  serving pipeline whose only common-path ceremony is selecting a source manually.
- `AssemblyCache` plus Web Extensions' `RestEntityRegistration` provide the existing boot-time, safe assembly-closure
  discovery pattern needed to find concrete media Entity types; no new discovery subsystem is warranted.
- `SocialCards`, `SocialCardRegistry`, `OpenGraphCardRenderer`, and the OpenGraph tests show that terse declarations
  currently write application closures into one process-static registry and rely on `Reset()` plus assembly-wide
  test serialization.
- `KoanCompositionScope` and `EntityLifecycleBuilder` are the closest canonical pattern: static semantic facets locate
  the current `IServiceCollection`, create one host-owned plan, and execute through host-owned lifecycle services.
- Web Backup's module, controllers, tracker, middleware, Swagger extension, and README expose unauthenticated global
  backup/restore, detached `Task.Run`, non-cancelling cancellation, in-memory ZIPs and operation state, permissive
  CORS, placeholder histories/counts/status, fake enhancement helpers, and no dedicated test or sample consumer.

**Reusing:** `KoanCompositionScope`, standard `IServiceCollection` singleton/replace semantics, `AssemblyCache`,
`MediaEntity<T>`, `Entity<T>.Lifecycle`, MVC controllers, typed Media/OpenGraph options, the Koan Web pipeline
contributor, runtime provenance, and existing focused Media/OpenGraph suites already exist. Media/OpenGraph constants,
options, source handles, recipes, social-card value objects, request projection, and snapshot Entity already exist.
No Backup HTTP contract is reused because no supported consumer or guarantee justifies one.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| Media source discovery/selection | `src/Koan.Media.Web/Routing/MediaSourceDiscovery.cs` | Reuse the compiled assembly closure once at composition, automatically select the only concrete media Entity, and centralize corrective zero/multiple-candidate behavior. |
| Media automatic/override specs | `tests/Suites/Media/Koan.Media.Web.Tests/MediaSourceSelectionSpec.cs` | Prove the zero-ceremony default, explicit dominance, and corrective ambiguity without widening the test topology. |
| Host-isolated social-card specs | `tests/Koan.Web.OpenGraph.Tests/SocialCardRegistryTests.cs` | Prove two service collections may independently declare the same Entity without leakage or reset. |
| Package-owned OpenGraph docs | `src/Koan.Web.OpenGraph/README.md` and `TECHNICAL.md` | Give the retained package an exact install/result/boundary and runtime-owner contract. |

**Coalescence:** The closest patterns are Web Extensions' assembly-closure scan and Data Core's composition-owned
Entity lifecycle plan. Specificity remains at each projection pillar: Core is too wide, while controllers and samples
are too narrow. Rebuild Media's source choice around one boot-time selector and absorb the one-Entity default into the
package module. Rebuild OpenGraph's registry as a host-owned instance obtained through `KoanCompositionScope`; capture
each host registration directly in its host-owned lifecycle plan. Delete `SocialCards.Reset()`, the static registry,
manual media ceremony in the golden consumer, and stale manual OpenGraph middleware curriculum. Retire Web Backup in
full; rebuilding it responsibly belongs with a future Jobs-backed, authorized backup control-plane design, not a
cosmetic package pass.

**Ergonomics:** Human and model readers see one business noun produce one serving surface, and one social-card
declaration produce one navigation projection. IntelliSense keeps the explicit media-source override available only
for a real ambiguity. The same expressions remain legible in an application module or `AddKoan(() => ...)`.
Operators see the selected media source/candidate posture and social-card declaration count at startup. Removing Web
Backup prevents agents and operators from mistaking a broad route list for a production recovery guarantee.

**Constraints satisfied:**

- Entity-first access remains the source gate; internal `Data<T>` calls in Media source resolution will move to the
  equivalent first-class Entity static where the generic constraint permits it.
- All application HTTP remains controller-owned; no inline endpoint is added.
- Existing stable routes and headers remain centralized; tunables remain in typed options.
- No placeholder, compatibility, fake persistence, or inert enhancement surface survives.
- No large unbounded data path is introduced; media bytes continue to use streams and the existing explicit render
  buffer limitation remains documented.
- Both retained reusable packages receive exact README/TECHNICAL contracts; current docs and generated topology are
  regenerated after the package cut.

**Risks:** Media discovery must use the already compiled assembly closure and remain reflection-load tolerant. An
explicit source must dominate regardless of module registration order. OpenGraph lifecycle execution still relies on
Koan's ambient host for Entity operations, but application closures and registration state will no longer leak across
hosts. Removing Web Backup changes package count and release lineage; the R11 matrix must preserve the implemented
retirement decision. Baseline evidence is Media Web 4/4 and OpenGraph 39/39; both current builds expose a stale removed
Core.Adapters reference from evaluated restore state, and Web Backup has build-only evidence with no controller suite.

### Web edge projection evidence

- Media Web now discovers concrete `MediaEntity<T>` candidates once from the compiled application closure. Exactly one
  candidate becomes the default `IMediaSource`; an explicit source always dominates, while zero or several candidates
  produce one corrective selection error. The source itself uses the first-class Entity and Media semantics rather
  than reaching through the Data facade. Media Web passes 7/7 and SnapVault's unchanged golden path passes 1/1 with
  no application source-registration ceremony.
- OpenGraph now stores declarations in the exact composing host. `SocialCards.For<T>` uses the composition scope,
  Entity lifecycle plans capture that host's registration directly, and rendering consumes the same host-owned
  registry. The process-static dictionary, `Reset()` test crutch, assembly-wide test serialization, and public manual
  middleware call are gone. OpenGraph passes 39/39, including independent same-route declarations in two service
  collections; DevPortal's unchanged golden path passes 1/1.
- Web Backup is retired in full. It had no supported application consumer or controller suite and could not honestly
  guarantee authorization, durable execution, cancellation, bounded resources, or recovery. `Koan.Data.Backup`
  remains an independent domain capability and passes 7/7 after its aggregate-discovery proof was aligned with the
  current provider catalog. Any future HTTP control plane must be rebuilt around explicit authorization, durable
  Jobs, resource bounds, and tested recovery—not revived from this projection.
- Media Web and OpenGraph now own exact README/TECHNICAL contracts. Current public guidance teaches reference plus
  `AddKoan()` and one media Entity, or one `SocialCards.For<T>` declaration; it names the meaningful ambiguity and
  security/operational boundaries without exposing framework wiring. Both generated package-quality records have
  zero findings and are structurally ready.
- Exact HEAD `6d9b0f8fa` packages contain DLL/XML, package-owned README, canonical icon, and build-transitive metadata.
  Media Web's dependency closure is Data Core, Media Abstractions/Core, Storage Abstractions/runtime, and Web;
  OpenGraph's is Cache Abstractions, Core, Data Core, and Web. Current direct/transitive NuGet audits report no known
  vulnerabilities for either package.
- Generated truth contains 107 packages: 16 repair-required, 29 review-required, and 62 structurally ready across 24
  claims. Public documentation truth passes across 222 current files and 40 navigation targets. No feed, release
  candidate, tag, remote mutation, or full release-certification run was created.

The Web edge projection slice passes. Media Web and OpenGraph remain separate, meaningful references with automatic
composition and host-owned decisions; Web Backup no longer turns an unsupported route collection into a product
promise.

### Identity and authentication foundation discovery

**Task:** Straighten the Identity/Web Auth vertical in dependency order, beginning with the shared sign-in contracts
and provider decision owner before assessing durable identity factors, roles, the OAuth server, or service-auth leaves.

**Application intent:** “Reference one real authentication provider, configure only its credentials, and keep the
application's existing `AddKoan()`; Koan should expose provider discovery and controller-owned challenge/sign-out
flows, choose the only eligible default, establish a secure session, and explain every availability/selection
decision. In Development, referencing the Test connector should supply the same complete flow locally.”

**Public expression:** The ordinary expression is the provider package reference plus the provider's credentials under
`Koan:Web:Auth:Providers:{id}` and the application's existing `AddKoan()`. No `AddKoanWebAuth`, middleware call, route
mapping, scheme registration, or provider descriptor attribute belongs in application code. `PreferredProviderId`
is the meaningful override when several eligible providers exist and the application wants a particular default;
explicit `/auth/{provider}/challenge` remains the provider-specific intent. Referencing `Koan.Identity` alone must
remain useful in a non-Web host and must not activate the functional Web Auth runtime merely to borrow contracts.

**Guarantee/correction:** A referenced connector describes availability; it becomes eligible only when its required
configuration is complete, except for an explicitly automatic local provider such as the Development Test connector.
One host-owned immutable plan is the authority for scheme seeding, default election, controllers, startup/facts, and
the OAuth server's provider projection. Explicit incomplete/unknown intent fails during composition with the missing
fields and a correction; an unconfigured optional connector stays visible but inert and cannot displace a working
provider. Sign-in, principal-validation, bootstrap, challenge, and denied-flow handler failures fail closed instead
of silently issuing or retaining a session; sign-out cleanup remains best-effort after the framework clears identity.

**Complete intent surface:** Beyond the package reference, existing `AddKoan()`, and provider credentials, no user
action is required. Multiple eligible providers may be challenged explicitly; `PreferredProviderId` changes only the
default challenge. Test auth requires only its reference in Development and remains unavailable outside Development
unless deliberately enabled. Durable identities, MFA/password factors, the embedded OAuth server, roles, and
service-to-service authentication remain separately referenced decisions and are not implied by Web Auth core.

**Public concepts:** `AuthProviderDefinition` is isolated module-to-pillar vocabulary describing protocol defaults and
automatic-vs-configured activation; it is not application ceremony. `IAuthProviderCatalog` is the isolated read-only
cross-module projection required by the OAuth server. `IKoanAuthFlowHandler` and its contexts are the existing
module-extension seam and move intact to the inert contracts boundary. `IUserStore`, `IExternalIdentityStore`, and
their small data contracts likewise move because Identity implements them without owning Web Auth activation.
`PreferredProviderId` remains because it expresses a real application choice. The duplicate descriptor attribute,
public manual registration call, scoped registry/election pair, and generic OIDC connector do not express distinct
business decisions and will not survive.

**Docs read:**

- `docs/architecture/principles.md` requires Reference = Intent, inert cross-module contracts, one host-owned compiled
  decision, corrective failures, and one runtime explanation; it is binding for the cut.
- `docs/engineering/index.md` requires controller-owned HTTP, typed options/constants, package companions, and focused
  evidence; the Test connector and later OAuth Server slices must remove endpoint-mapping exceptions.
- `docs/architecture/koan-identity-design.md` explains the person/sign-in/tenancy composition thesis but is explicitly
  superseded and cannot be used as current capability truth.
- `docs/decisions/SEC-0007-koan-identity-module.md` makes durable Identity a headless Entity capability that composes
  with sign-in rather than duplicating it; current source violates that separation by referencing functional Web Auth
  for borrowed contracts.
- `docs/decisions/WEB-0051-auth-provider-discovery-and-election.md` preserves provider availability/election intent,
  while its bespoke registry/reflection/report mechanisms are superseded by the later generic provider substrate.
- `docs/decisions/WEB-0066-auth-flow-handler-pipeline.md` preserves the single lifecycle seam but its fail-open error
  policy is unsafe for reconciliation, session revocation, and step-up enforcement.
- `src/Koan.Web.Auth/README.md`, `TECHNICAL.md`, and `docs/guides/authentication-setup.md` materially understate Koan's
  automatic path while teaching manual ASP.NET wiring, nonexistent routes/options/SAML, hand-built user entities,
  unsafe token parsing, and zero-config service-token claims; they require a greenfield truth rewrite after code proof.

**Code read:**

- `AuthModule`, `ProviderRegistry`, `AuthProviderElection`, and `AuthSchemeSeeder` currently compose the same provider
  set through reflection, scoped DI, lazy election, and startup seeding, while `Report` independently reconstructs it;
  they are the duplicated decision owners to replace.
- `ServiceCollectionExtensions` owns cookie/auth lifecycle wiring correctly but exposes unsupported manual activation,
  registers provider decisions per scope, and adds a second startup filter even though base Web already owns
  authentication/authorization middleware order.
- Google/Microsoft/Discord modules contribute small useful defaults, but each also carries two dead duplicate assembly
  descriptors. The OIDC connector contributes no defaults or mechanics because Web Auth core already supports any
  explicitly configured OIDC provider; it has no independent reference result.
- The Test connector has real local OAuth/OIDC behavior but repeats activation between advertisement and a startup
  filter, retains an unused configurable route base, and maps conventional controller routes after startup instead of
  letting attribute-routed controllers own the stable local protocol surface.
- `SecIdentityModule` and `IdentityAuthFlowHandler` prove that durable Identity is an Entity-first consumer of auth
  contracts. Its functional Web Auth dependency is accidental coupling; dispatcher exception swallowing can issue a
  cookie after reconciliation/session failure or retain a principal after validation failure.
- Core `ProviderCatalog<TProvider>` plus Storage's provider catalog/routing/composition pattern is the closest current
  mechanism: typed identities compile once, pillar policy owns eligibility/election, runtime consumers share the same
  immutable plan, and composition facts project the actual receipt.
- Focused baselines pass Auth 32/32, real Test OAuth/OIDC round-trip 4/4, Identity 114/114, and OAuth Server 50/50.
  All exposed stale removed-Core.Adapters restore warnings; Identity Web also has one current member-hiding warning.
  Roles and Auth Services have no owned behavior suite or real application consumer and remain explicit later
  disposition questions rather than inherited support claims.

**Reusing:** `ProviderCatalog<TProvider>`, `ProviderSelectionReceipt`, standard DI/options/authentication handlers,
`KoanCompositionBuilder`, `KoanModule.Start`/`ReportComposition`, `KoanRegistry`'s one-time flow-handler discovery,
the current cookie/return-url/userinfo mechanics, controller discovery, and the four focused suites already exist.
Existing auth route/config constants and typed options remain authoritative where runtime actually honors them.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| Inert auth contract package and companions | `src/Koan.Web.Auth.Abstractions/` | Hold every cross-module auth contract without activating Web Auth; preserve existing namespaces where useful and use ordinary .NET project identity. |
| `AuthProviderDefinition` / `IAuthProviderCatalog` | `src/Koan.Web.Auth.Abstractions/Providers/` | Give thin connectors one immutable standard-DI declaration and give optional consumers one safe read-only runtime projection. |
| `AuthProviderPlan` | `src/Koan.Web.Auth/Providers/AuthProviderPlan.cs` | Compile definitions, configuration, eligibility, default election, reasons, and corrections once per host over Core's generic catalog. |
| Auth composition projection | `src/Koan.Web.Auth/Composition/AuthCompositionFacts.cs` | Project the exact provider plan into canonical facts instead of recomputing it in module reporting. |
| Provider/fail-closed specifications | `tests/Koan.Web.Auth.Tests/AuthProviderPlanTests.cs` and `AuthFlowDispatcherTests.cs` | Prove inert unconfigured references, explicit corrections, deterministic/default behavior, host isolation, and security failure posture. |

**Coalescence:** Closest pattern is Storage's `ProviderCatalog` → immutable pillar plan → composition facts. Specificity
is the Web Auth pillar: Core already owns identity/ranking mechanics but cannot know credential completeness,
interactive protocols, Development-only activation, or challenge semantics; individual connectors are too narrow and
must not elect/report themselves. Rebuild the scoped registry/election pair and reflective `Report` copy into one
singleton `AuthProviderPlan`; make modules register thin definitions through standard DI; seed schemes eagerly from
that plan in the retained module lifecycle; project the same decisions through facts. Split cross-module contracts
into `Koan.Web.Auth.Abstractions` so headless Identity becomes genuinely layered. Delete the public
`AddKoanWebAuth`, second auth startup filter, descriptor attribute and ten duplicate assembly declarations, Test
startup filter/conventional route mapper/configurable route base, and behaviorless OIDC connector. Keep the three real
external defaults and the Test simulator as separate provider references. Defer Roles/Server/Services dispositions
and Auth Server's inline-endpoint rebuild to their own focused slices after this foundation is stable.

**Ergonomics:** A human or model reads “provider reference + credentials” directly from the project/configuration;
there is no framework wiring branch. An unconfigured connector no longer hijacks Development login, while an explicit
mistake points to the exact missing provider fields at startup. IntelliSense shows only the deliberate provider choice
and stable auth flow extension seam. Operators and agents see available, inactive, eligible, and default providers
from one receipt, with no disagreement between startup, discovery, facts, and OAuth projection. Module authors write a
normal `KoanModule` and register one immutable provider definition; no descriptor attributes, reflection rules, or
parallel election/report code are required.

**Constraints satisfied:**

- Entity-first access remains in durable Identity and OAuth entities; this foundation introduces no repository layer.
- Test-provider routes become attribute-routed controllers. Auth Server's existing inline protocol endpoints are an
  observed violation reserved for its separately proved slice; no new inline route is added here.
- Stable auth/Test routes remain project constants and tunables remain typed options; the unused route-base knob is
  deleted rather than retained as false configurability.
- Cross-module contracts move to one inert package with no functional module or activation metadata.
- Provider discovery/composition/election runs once per host; request handlers consume the immutable plan.
- No large Entity read or stream path is introduced.
- Package README/TECHNICAL pages, current auth guides, generated topology, and the disposition matrix will be aligned
  after focused code proof. ADRs remain dated records.

**Risks:** Dynamic scheme registration must still occur before the Web pipeline handles a request; resolving/seeding
the plan in `AuthModule.Start` uses Koan's existing one-module lifecycle and requires a real-host regression. Existing
configuration-only provider IDs must remain supported without resurrecting the empty OIDC package. Moving public
types between assemblies preserves namespaces but is a binary package break, intentionally acceptable for V1; every
dependent project and exact artifact closure must update together. Fail-closed dispatcher behavior is security-correct
but may reveal handlers that relied on swallowed failures; focused Identity and real-host tests must exercise it.

### Identity and authentication foundation implementation

Status: `implemented and focused-proof complete`; Roles, Auth Services, Identity Web coalescence, and Auth Server's
inline protocol surface remain separately bounded follow-on slices.

- Added inert `Sylin.Koan.Web.Auth.Abstractions` and moved every shared lifecycle, provider, store, DTO, and projection
  contract there without a module or activation metadata. Headless `Koan.Identity` now references that boundary and
  no longer depends on functional Web Auth; its existing access/JIT code declares its direct `Koan.Web` dependency.
- Rebuilt provider composition around one singleton `AuthProviderPlan` over Core's `ProviderCatalog<T>`. Connector
  definitions, application overlays, eligibility, exact corrections, explicit/default priority, scheme seeding,
  discovery, OAuth Server projection, startup logging, and composition facts now consume one host-owned decision.
- Reduced Google, Microsoft, and Discord to one ordinary-DI `AuthProviderDefinition` each. Removed contributor
  interfaces, scoped registry/election, descriptor attributes/assembly copies, reflection-backed module reporting,
  duplicated provenance, dead production gating, unused `SecretRef`, and the second Web Auth startup filter.
- Retired `Sylin.Koan.Web.Auth.Connector.Oidc`: config-only OIDC/OAuth2 is a native Web Auth capability and the package
  produced no independent behavior. Removed the corresponding dead auth-provider projection from the orchestration
  source generator so static manifests cannot disagree with the runtime plan.
- Rebuilt the Test connector around stable attribute-routed `/.testoauth` controllers and two immutable automatic
  definitions. Removed its startup-order attribute, startup filter, conventional endpoint mapper, configurable route
  base, and separate discovery contributor. One `IsActive` predicate now gates every local protocol controller and
  provider availability.
- Changed security-bearing lifecycle failures to fail closed: bootstrap/sign-in/challenge/denied errors propagate,
  validation errors reject the principal, explicit sign-in rejection prevents cookie issuance, missing external
  subject and identity-link failures reject authentication, while sign-out cleanup alone remains best-effort.
- Rewrote Web Auth, all surviving connectors, both current public auth guides, and the Auth reference card to the
  greenfield expression: connector reference + credentials + `AddKoan()`. Public docs now state exact routes,
  automatic-vs-explicit election, config-only providers, corrections, inspectability, and unsupported SAML/secret-ref/
  provider-token-store scenarios. Public truth lint passes `222` current files and `40` navigation targets.

Focused proof after the rebuild:

| Proof | Result |
|---|---:|
| `Koan.Web.Auth.Tests` (provider plan, lifecycle, helpers) | 39/39 |
| local OAuth2/OIDC HTTP integration, discovery, and facts agreement | 5/5 |
| embedded OAuth Server integration | 50/50 |
| durable Identity integration | 114/114 |
| AddKoan auth catalog registration | 1/1 |
| orchestration generator and four surviving connector builds | 0 errors |
| affected NuGet artifacts (Abstractions, Web Auth, four connectors, Identity) | 7/7 packed; dependency boundaries inspected |
| generated package quality | 107 packages: 16 repair, 23 review, 68 structurally ready |
| generated product surface | 24 claims across 107 packages |

Identity still has two independently visible polish items: the pre-existing `ImpersonationController.Request`
member-hiding warning, and its package's pre-existing missing NuGet README warning. Both belong to the separately
bounded Identity package-graduation slice rather than being hidden or superficially papered over here. The inspected
Identity nuspec now depends on `Core`, `Data.Core`, `Web`, and inert `Web.Auth.Abstractions`—not functional Web Auth.

### Service authentication leaf discovery

**Task:** Decide whether `Sylin.Koan.Web.Auth.Services` owns an independently meaningful V1 capability after the
Trust/authentication foundation rebuild, or whether keeping it would preserve a second, incomplete service-identity
model.

**Application intent:** “Call another Koan workload with the current security context or a workload credential, bound
to the destination and enforced on receipt.” The application should use ordinary `HttpClient` for HTTP; Koan should
own only the security-context carrier and trust guarantees that are reusable across HTTP, MCP, Messaging, and Jobs.

**Public expression:** There is no supported expression to preserve in this slice. No current application source uses
`IKoanServiceClient`, `IServiceAuthenticator`, `IServiceDiscovery`, `[KoanService]`, or `[CallsService]`. GardenCoop
chapters 1 and 2 reference the package but never consume it. A future expression must be proved by a real distributed
golden scenario before Koan introduces a transport-specific convenience API.

**Guarantee/correction:** The current package cannot honestly guarantee its README claims. Its registration method and
default `HttpClient` configuration are no-ops; registry discovery/register operations return empty/do nothing;
`EnableAutoDiscovery`, token cache duration, invalidation, token endpoint, client secret, certificate validation, and
retry settings do not control the advertised behavior; guessed ports use process-randomized `string.GetHashCode()`;
and token acquisition failure is swallowed so a request can leave without authentication. This is fail-open security,
not progressive disclosure.

**Docs and code read:** The package README, TECHNICAL, 900-line SAMPLES document, complete source, project graph,
GardenCoop references/lockfiles, `SEC-0001`, superseded `DEC-0053`, and current `Koan.Security.Trust` issuer/inbound
contracts were inspected. The package builds without warnings but owns no tests, product claim, current code consumer,
or demonstrated application result. `SEC-0001` makes one verifiable security-context envelope across four channels the
current architectural owner; `IIssuer.Issue(..., audience)` already owns audience-bound credential issuance. ADRs
remain dated records and are not rewritten.

**Closest pattern and coalescence:** Standard `IHttpClientFactory`/typed `HttpClient` is the HTTP substrate;
`Koan.Security.Trust` is the concern chokepoint for workload identity, audience binding, verification, delegation, and
future cross-channel carriers. Retaining a Web.Auth leaf with its own service attribute, discovery system, token
cache, client abstraction, and options would distribute one security concern across two authorities. No replacement
abstraction is created now: a future Trust-owned outbound carrier must emerge from an executable multi-workload use
case and share the same envelope semantics with Communication, MCP, and Jobs.

**Disposition:** Retire `Sylin.Koan.Web.Auth.Services` in V1. Remove it from the active solution and package graph;
remove its unused GardenCoop references and AOT root; let the supported build target regenerate both sample
composition lockfiles; regenerate package-quality/product-surface truth. Historical ADRs, archived proposals,
assessment evidence, and attic content remain historical. Roles is explicitly separate: it owns real Entity/admin/
bootstrap behavior and requires its own Identity-overlap assessment rather than inheriting this result.

**Ergonomics:** Developers and agents no longer discover an attractive API whose advertised behavior is mostly inert.
GardenCoop returns to Reference = Intent: the Test connector expresses local authentication without redundant direct
Web Auth or unused service-auth references. Operators are no longer shown configuration knobs and discovery facts that
do not control runtime guarantees.

### Service authentication leaf implementation

Status: `retired with focused proof complete`.

- Removed `Sylin.Koan.Web.Auth.Services` from the solution and active package graph. Its empty registrar, speculative
  endpoint guessing, duplicate client/token layers, false options, fail-open handler, and unproved attribute model no
  longer constitute a V1 package promise.
- Removed the unused package reference from GardenCoop chapters 1 and 2 and the chapter-1 AOT root. Also removed each
  chapter's redundant direct Web Auth reference: the Test connector is the developer's authentication intent and
  brings the runtime dependency transitively.
- Both GardenCoop projects build with zero warnings/errors. Their composition lockfiles were regenerated by the
  supported build target and now record the actual Auth Abstractions/Test-connector closure without Services, the
  redundant direct Web Auth intent, or previously stale transitive modules.
- After clean restore of the changed graph, the owned GardenCoop chapter-1 and chapter-2 golden-path suites each pass
  1/1. The public documentation truth gate passes across 220 current files and 40 navigation targets.
- Canonical generated truth now contains 106 packages: 16 repair-required, 22 review-required, and 68 structurally
  ready; product surface remains 24 claims across 106 packages. Auth Services had no claim to migrate.
- No Trust-owned outbound convenience was invented. The future acceptance bar is a real multi-workload scenario that
  proves destination audience, inbound enforcement, delegation/actor preservation, failure posture, and one envelope
  across more than HTTP. Standard typed `HttpClient` plus the current Trust issuer remain the honest building blocks.

Historical ADRs, archived proposals, assessment evidence, and attic content remain intact as dated evidence; current
solution, samples, package inventories, and architecture inventory no longer present the retired package as active.

### Role administration leaf discovery

**Task:** Decide whether `Sylin.Koan.Web.Auth.Roles` is the effective role catalog promised by Identity/auth
architecture, should coalesce into Identity, or should retire until a real role-definition use case exists.

**Application intent:** “Grant a person a role and have authorization honor it.” Koan already expresses this directly:
`IdentityRole` is the global person-to-role binding, `Membership.Roles` is the tenant binding, sign-in/request pipelines
project both as standard .NET role claims, and ASP.NET authorization consumes those claims. A separate role-definition
catalog is meaningful only if it constrains grants, drives policy compilation, or produces a proved admin experience.

**Public expression:** Keep the effective expression already owned by Identity and standard .NET authorization. Do not
require a catalog package, store interfaces, snapshot provider, import/export controller, custom requirement strings,
or manual policy-registration helper merely to use role claims. A future catalog may be an Entity-first Identity/admin
capability, but only when a golden application needs role metadata or governed assignment.

**Guarantee/correction:** The current package does not make its catalog effective. No source outside the package
consumes `Role`, `RoleAlias`, `RolePolicyBinding`, their store interfaces, or the snapshot. The named
`auth.roles.admin` policy protecting its controller is never registered; persisted `RolePolicyBinding` values never
compile into ASP.NET policies; aliases never normalize a grant or principal; role keys are not validated against the
catalog; and `RowVersion` is copied but not enforced. The controller's export reads configured aliases rather than
stored aliases. Default aliases can make the stores non-empty before any configured role exists, preventing later
first-run role seeding.

**Security/operational findings:** The optional first-user bootstrap is not atomic across nodes: check and mark are
separate Entity operations, so concurrent sign-ins can both elevate. It catches store failures and silently skips
instead of producing an inspectable correction. The hosted seeder infers production from a process environment
variable instead of `IHostEnvironment`, catches all startup failures, and reports success only through logs. The
package has no owned tests or application consumer; its build has two warnings and its README/TECHNICAL teach the
removed `IKoanAuthEventContributor` contract. Generated product truth assigns it no claim.

**Docs and code read:** All package source and companions, current consumers, solution/project graph, Identity's
`IdentityRole`/management/reconciliation/access tests, Web.Extensions policy registration, Auth lifecycle options,
the superseded Identity design note, current product truth, and relevant history were inspected. The closest honest
pattern is the existing Entity-first Identity binding projected to ordinary `ClaimTypes.Role`, not the package's
interface-wrapped Entity stores or inert string DSL.

**Coalescence:** Retire the package rather than move its concepts. Remove the now-orphaned `AdminBootstrap` option from
the inert Auth contracts; keep the independent role-list sign-in handler because it has real behavior and no Roles
dependency. Do not add a `Role` Entity to Identity merely for structural symmetry: strings are the standard .NET role
contract today, while a catalog is a future business capability requiring validation, assignment UX, policy effects,
and tests. This keeps one effective authorization path and avoids turning speculative metadata into framework law.

**Disposition:** Retire `Sylin.Koan.Web.Auth.Roles` in V1. Remove it from the active solution/package graph and remove
the dead bootstrap configuration vocabulary. Update current architecture inventory and generated truth; retain ADRs,
archived/assessment evidence, and the explicitly superseded long-form Identity plan as dated design history, with a
current correction where needed. Prove Auth Abstractions/Web Auth and Identity's role behavior after the cut.

**Ergonomics:** Developers and models see one direct rule: grant role strings through Identity or Membership and use
standard `[Authorize(Roles = ...)]`; reading code is reading the access decision. Operators no longer see an admin
route, policy-binding store, alias snapshot, and bootstrap knobs that do not govern authorization. Framework authors
gain no replacement interfaces or lifecycle mechanism.

### Role administration leaf implementation

Status: `retired with focused proof complete`.

- Removed `Sylin.Koan.Web.Auth.Roles` from the solution and active package graph. No catalog entities, store wrappers,
  snapshot cache, seed hosted service, unregistered admin policy/controller, manual capability-policy helper, or
  non-atomic first-user elevation remains as a V1 promise.
- Removed the orphaned `AdminBootstrap` configuration shape from inert Web Auth contracts. The independently useful
  role-list sign-in handler remains in Web Auth; it continues to operate on standard role claims without this package.
- Web Auth passes 39/39 after the contract cut. Identity passes 114/114, including global `IdentityRole`, tenant
  `Membership.Roles`, effective-access explanation, role stamping, and authorization behavior; the known independent
  `ImpersonationController.Request` member-hiding warning remains visible for Identity graduation.
- Current architecture prose now describes the effective model—standard role keys with Entity-first global/tenant
  bindings—rather than the removed catalog assumption. ADRs and explicitly historical evidence remain unchanged.
- Canonical truth now contains 105 packages: 16 repair-required, 21 review-required, and 68 structurally ready;
  product surface remains 24 claims across 105 packages. No support claim required migration because the package was
  unassessed and had no product claim.

A future governed role catalog is not prohibited. Its admission bar is a golden consumer whose role definitions
validate grants, drive actual authorization policy, produce a safe assignment/admin flow, and remain inspectable; it
should then live with Identity's person/access domain rather than reappear as a disconnected Web Auth projection.

### Embedded authorization-server discovery

**Task:** Graduate `Sylin.Koan.Web.Auth.Server` without changing its exercised OAuth 2.1 wire contract, and remove its
known exception to Koan's controller-surfaced HTTP architecture.

**Application intent:** “Reference the authorization-server leaf and let an OAuth/MCP client obtain an audience-bound
token through standard discovery, Authorization Code + PKCE or Device flow.” The application keeps its existing
`AddKoan()` and owns only the consent and terminal pages. No endpoint mapping, bearer wiring, issuer construction, or
protocol service registration belongs in application code.

**Public expression:** Package reference + `AddKoan()` activates `/oauth/...` and `/.well-known/...`. Optional typed
configuration under `Koan:Web:Auth:Server` supplies canonical issuer, app-page paths, lifetimes, DCR, development,
refresh, and key-lifecycle choices. Existing current guidance already teaches this exact expression and remains the
public route contract.

**Guarantee/correction:** Keep the complete exercised protocol surface, ES256 issuance/JWKS, persisted rotating production
keys, fail-closed ephemeral-key guard, PKCE-S256, browser-bound consent, audience binding, loopback-only/rate-limited
DCR, device polling protections, rotating refresh/reuse detection, grant revocation, Development-only token/client
affordances, and request-host/explicit-issuer behavior. The current 50-spec real-host suite is the executable boundary.

**Docs and code read:** The complete project/source inventory, `AuthServerModule`, typed options, all endpoint handlers,
current OAuth guide, Web/Auth/MCP consumers, 50-spec suite, package-quality/product records, engineering guardrails,
and closest Test-provider/controller patterns were inspected. The package has a real independent result and strong
tests, but no package-owned README/TECHNICAL and currently registers five `IKoanEndpointContributor` instances.

**Reusing:** Existing protocol handlers, Entity-backed OAuth artifacts, `IAsymmetricIssuer`, persisted key store and
rotation, `AddKoanControllersFrom`, attribute routing, options, module lifecycle/reporting, and the integration suite
remain authoritative. No protocol DTO, repository layer, middleware, or application hook is introduced.

**Creating new:** One `OAuthServerController` declares every framework-owned protocol route and delegates to the
existing handlers; one `AuthServerRoutes` constants owner supplies both attribute templates and advertised URLs.
Package-owned README/TECHNICAL companions describe activation, meaningful result, route/configuration/security
contracts, inspectability, and unsupported public-IdP/federation scenarios.

**Coalescence:** Remove the five endpoint-contributor registrations and their `Map` methods. Routing belongs at one MVC
chokepoint; protocol complexity remains in concern-specific handlers. Centralize the stable route strings currently
duplicated across mappers, metadata, cookies, and reporting. This is a net reduction in lifecycle mechanisms and keeps
the controller thin rather than moving 600 lines of OAuth mechanics into transport code.

**Ergonomics:** Developers and agents continue to get “reference + AddKoan” with no changed application code.
Maintainers can inspect the complete HTTP surface in one controller and the complete URL vocabulary in one constants
type. Operators keep the same startup posture and discovery documents; package presentation finally states the
production key and app-page obligations before installation.

**Risks:** Attribute routing must preserve exact methods/templates, raw form/JSON handling, response headers, status
codes, cookies, redirects, and API-explorer exclusion. The real-host suite is therefore the acceptance proof; no broad
release certification runs in this slice.

### Embedded authorization-server implementation

Status: `keep with focused proof complete`.

- Replaced five endpoint-contributor registrations and five distributed `Map` methods with one
  `OAuthServerController`. The controller is now the complete route/method inventory and delegates directly to the
  existing concern-specific handlers; OAuth mechanics did not move into the HTTP boundary.
- Added one internal `AuthServerRoutes` vocabulary. Attribute routes, advertised metadata, browser-binding cookie
  paths, and startup reporting now consume the same constants instead of repeating protocol paths.
- Removed `OAuthClient.IsPublic`, a write-only flag that implied unsupported confidential-client behavior. The model,
  metadata, DCR, tests, and public docs now agree: every supported client is public, token endpoint authentication is
  `none`, and client secrets are unsupported. Entity-first pre-registration remains the deliberate path for known
  clients and non-loopback redirects.
- Added package-owned README and TECHNICAL companions covering reference-only activation, the two-page application
  seam, route and configuration ownership, Entity-backed persistence, production key posture, inspectability, and
  unsupported federation/confidential-client scenarios. The current public guide now records the Entity-first
  pre-registration expression and the verified 50-spec boundary.
- `Koan.Web.Auth.Server.IntegrationTests` passes 50/50 against a real host after the routing and model changes. The
  proof covers discovery, JWKS, Authorization Code + PKCE, Device, DCR, refresh rotation/reuse, keys, host/issuer,
  cookies, redirects, and hardening without a broad release-certification run.
- Canonical generated truth remains 105 packages and 24 product claims: Auth Server moves from repair-required to
  structurally ready, producing 15 repair-required, 21 review-required, and 69 structurally ready packages. The
  verified authentication/authorization claim now names the package, its current guide, and its 50-spec evidence
  instead of leaving a proved capability unassessed. The packed artifact contains the expected DLL/XML, owned README,
  icon, build-transitive props, and six direct Koan dependencies.
- Public documentation truth passes across 220 current files and 40 navigation targets; structural docs lint reports
  zero errors. The package builds with zero warnings and `git diff --check` is clean.

The result preserves the application expression—package reference plus the existing `AddKoan()`—while reducing five
activation/routing moving parts to one conventional MVC chokepoint and making the supported security boundary
explicit to developers, agents, and operators.

### Durable Identity core and Web discovery

**Task:** Graduate `Sylin.Koan.Identity` and `Sylin.Koan.Identity.Web` around the behavior they actually enforce,
removing public placeholder capabilities before writing their package contracts.

**Application intent:** “When a user signs in, keep one durable person, enforce their lifecycle and sessions, and let
the user or an operator inspect and manage the result.” Adding the Web leaf should expose those proven operations
without application endpoint wiring.

**Public expression:** Reference `Sylin.Koan.Identity` beside the application's Web Auth provider and keep the existing
`AddKoan()` bootstrap. A selected Data provider persists the Entity-backed identity plane. Optional
`Koan:Identity:{Posture,SeedDevUsers,DevUser,HashChainAudit}` values change explicit posture, local seeding, and audit
tamper evidence. Reference `Sylin.Koan.Identity.Web` to add authenticated `/api/identity/me` self-service and the
`koan:identity-operator`-gated `/api/identity/admin` surface. No registration, repository, middleware, or endpoint-map
call is part of the application expression.

**Guarantee/correction:** A successful Web Auth sign-in reconciles `(provider, subject)` to one durable person without
email auto-merge, records a durable cookie session, stamps global roles, and rejects later cookie validation when the
session is revoked or the person is inactive. Development defaults Open and may seed local people; every other
environment defaults Closed, and forcing Open outside Development refuses startup. Invalid enum configuration is a
standard .NET binding failure. Identity Web scopes self-service to the authenticated subject and keeps destructive
operator and impersonation operations role-gated and actor-attributed. A durable Data provider is required for durable
behavior; absence/failure is not disguised as an Identity-local store.

**Complete intent surface:** Core requires only package reference, the ordinary `AddKoan()`, an active Data provider,
and Web Auth when automatic sign-in reconciliation is desired. Web requires its package reference, an authenticated
principal for self-service, and a standard role claim `koan:identity-operator` for operator APIs. Configuration is
optional unless deployment posture differs from the environment-derived default. Client token issuance is a separate
intent expressed by `Sylin.Koan.Web.Auth.Server`.

**Public concepts:** `Identity`, `IdentityEmail`, and `ExternalIdentityLink` express person and explicit factor linkage;
`Session` expresses enforced cookie-session revocation; `IdentityRole` expresses standard global role binding;
`AuditEvent`/`AuditChain` express best-effort mutation evidence and optional tamper detection; effective-access and
impersonation types express proved access explanation and dual-control acting-as. `IdentityOptions` contains only
deployment decisions. `ApiToken`, `ApiTokenService`, and `Group` do not survive: the former has no authentication
consumer and duplicates the OAuth Server's token concern, while the latter contributes nothing to access despite
claiming bulk-role semantics. `Identity.Epoch` also does not survive because no verifier consumes it.

**Docs read:** `docs/engineering/index.md` requires Entity-first data, controller-only HTTP, typed options/constants,
and package companions; `docs/architecture/principles.md` requires one business expression, reference intent, standard
.NET reuse, semantic honesty, and one current path; `SEC-0007` supplies the accepted durable-person and layering
decisions but remains dated ADR evidence; `authentication-setup.md` establishes the current Web Auth expression and
separates sign-in from token issuance; Canon's README/TECHNICAL provide the closest current package-companion pattern.

**Code read:** `SecIdentityModule` owns options, discovered reconciliation/access gates, management services, audit
hooks, dev seeding, and startup reporting; `IdentityReconciler` and `IdentityAuthFlowHandler` own explicit account
linking, sign-in reconciliation, session creation/validation, and role stamping; `SessionService`,
`IdentityLifecycleService`, `AccessExplainer`, and `ImpersonationService` are the effective day-two chokepoints;
Identity Web's four controllers are conventional attribute-routed projections; the 114-spec real-host family suite
proves core, Web, tenancy, passwords, and MFA together. Source/consumer searches show no enforcement reader of
`Identity.Epoch`, `ApiToken.SecretHash`, or `Group.MemberIdentityIds`: the Tenancy bridge only incremented the dormant
epoch, while token and group behavior ended at their CRUD-oriented services/tests.

**Reusing:** Entity statics and relationships; the inert Web Auth lifecycle contracts; standard `ClaimTypes.Role` and
ASP.NET controller/authorization primitives; `IdentityPosture`, typed options binding, module lifecycle/reporting;
existing reconciliation, session guard, role/access, audit, and impersonation implementations; OAuth Server for real
client-token issuance; the focused 114-spec suite and generated package/product compilers.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| Package quick contract | `src/Koan.Identity/README.md` | State the smallest activation, meaningful result, configuration, and honest boundaries. |
| Package technical contract | `src/Koan.Identity/TECHNICAL.md` | State ownership, lifecycle, persistence, failure, access, audit, and unsupported scenarios. |
| Web package quick contract | `src/Koan.Identity.Web/README.md` | State route groups, role/subject gates, and reference-only activation. |
| Web package technical contract | `src/Koan.Identity.Web/TECHNICAL.md` | Inventory controller ownership, projections, security, and non-UI boundary. |
| Focused honesty specs | existing Identity spec files | Prove typed posture and complete core-dependent deletion without inventing another test mechanism. |

**Coalescence:** Closest posture pattern is typed `TenancyOptions.Posture`; rebuild Identity's string parser as the
same standard nullable enum. Closest token owner is the now-verified OAuth Server; delete Identity's unusable personal
token record/endpoints instead of building a competing issuer/verifier. Delete Group until a real group-to-access
contributor and management use case exists. Keep one headless Identity domain owner and one thin Web projection;
`Koan.Web` remains an explicit dependency because the current `IAuthorize`/`AgentGrant` floor lives there. Extracting a
framework-neutral authorization package would affect several pillars and belongs to a separately assessed logical
block, not a hidden side effect of package documentation.

**Ergonomics:** A human or model reads “reference Identity; sign in; a durable person and enforceable session exist.”
Entity types remain directly discoverable through IntelliSense, standard role claims remain the authorization
vocabulary, and the Web leaf adds routes rather than setup verbs. Operators see effective posture and inspect actual
entities/APIs. Removing inert tokens, groups, and epoch eliminates three branches that looked security-bearing but
created no guarantee.

**Constraints satisfied:** Entity statics remain the data path; all HTTP stays in attribute-routed controllers; stable
role/claim identifiers remain centralized; posture becomes a typed option; no large unbounded path is added (existing
operator search is separately bounded for this slice); package companions are added; ADRs remain unchanged; no broad
release certification is planned.

**Risks:** Public types are intentionally removed in a greenfield pre-1.0 release. Current Identity deletion also
under-counts global roles and impersonation grants; the repair must make its report cover every core-owned dependent
while retaining audit evidence. The family integration project combines six packages, so final counts will fall when
placeholder-only facts are removed; evidence must state the new count rather than preserve a vanity number.

### Durable Identity core and Web implementation

Status: `keep with focused proof complete` for `Sylin.Koan.Identity` and `Sylin.Koan.Identity.Web`; optional Identity
leaves remain separately assessed work.

- Removed the public `ApiToken` Entity, `ApiTokenService`, self-service token routes, and their CRUD-only tests. No
  verifier, authentication handler, or request path accepted those secrets; real client-token issuance remains the
  separately referenced, verified OAuth Server instead of a second security-looking record store.
- Removed the public `Group` Entity and read-only operator route. Member IDs had no effective-access contributor or
  bulk-role behavior. A future group capability must prove assignment, nesting semantics, authorization effects, and
  management before re-entering the product.
- Removed `Identity.Epoch` and the Tenancy bridge's write-only increment. No bearer verifier read it, so deactivation
  continues to state the exact live guarantee: inactive status plus durable cookie-session revocation. Already-issued
  bearer tokens remain valid until their issuer's normal expiry/revocation rule.
- Replaced Identity's custom string posture parser with nullable `IdentityPosture`, matching standard .NET options and
  the existing Tenancy posture shape. Invalid configuration now refuses host startup with the Identity configuration
  path; forced Open outside Development retains the explicit fail-closed guard.
- Corrected lifecycle deletion to remove every core-owned dependent: emails, sessions, external links, global roles,
  and impersonation grants where the person is actor or target. Audit evidence is deliberately retained and reported
  boundaries state that optional modules own their own deprovisioning.
- Renamed the impersonation request action to stop hiding `ControllerBase.Request`; Identity, Identity.Web, and the
  affected Identity.Tenancy bridge now each build with zero warnings. All HTTP remains controller-owned.
- Moved the global operator role from a Web-only constants type to core `IdentityRoles` and hardened the
  Identity-Tenancy role-projection chokepoint to strip both tenancy-fleet and identity-plane operator roles. A tenant
  membership can no longer unlock the global Identity operator API; ordinary tenant roles continue to project.
- Added owned README/TECHNICAL contracts for core and Web. They teach reference + existing `AddKoan()`, Data/Web Auth
  prerequisites, typed posture, enforced sessions/roles/access/impersonation, route authority, startup reporting, and
  explicit non-support for personal tokens, group access, bearer revocation, bundled UI, and transactional cross-module
  deletion. Current surface truth now calls the Web leaf management APIs rather than generated consoles.
- The focused family suite passes 111/111 after removing three placeholder-only facts and adding invalid-posture plus
  complete-dependent-deletion proof. Core, Web, and the affected Tenancy bridge build with zero warnings/errors.
- Canonical truth remains 105 packages and 24 product claims. Core and Web move from repair-required to structurally
  ready, yielding 13 repair-required, 21 review-required, and 71 structurally ready packages. The verified
  authentication/authorization claim now explicitly includes Identity.Web and the 111-spec family evidence.
- Both packed artifacts contain DLL/XML, owned README, mascot icon, build-transitive props, and their expected direct
  dependencies. Identity depends on Core, Data.Core, Web.Auth.Abstractions, and the current Web authorization floor;
  Identity.Web depends only on Identity and Web plus `Microsoft.AspNetCore.App`.

This cut makes the developer promise smaller and stronger: sign-in becomes a durable person with enforceable cookie
sessions and standard roles; the optional Web reference exposes management APIs. Security-shaped concepts that did
not participate in security are gone rather than documented as future magic.

### Account-security factor family discovery

**Task:** Decide whether Credentials, Passwords, and MFA are complete, distinct V1 package capabilities, then either
graduate their supported path or remove the incomplete product surface.

**Application intent:** Reference a local-password and/or MFA capability, keep `AddKoan()` as the only bootstrap, and
receive a complete sign-in ceremony: primary proof, an honest step-up continuation when required, factor verification,
and a normal enforceable Koan session.

**Public expression:** No honest complete expression exists today. `Sylin.Koan.Identity.Passwords` exposes
`PasswordCredentialService.SetPasswordAsync/VerifyAsync`; `Sylin.Koan.Identity.Mfa` exposes TOTP and recovery-code
services; `Sylin.Koan.Identity.Credentials` can reject cookie sign-in and mint a `StepUpTicket`. An application must
still invent the local-login controller, challenge response, ticket transport, TOTP/recovery controller, resumed
`SignInAsync`, rate limiting, return handling, and browser security policy. Tests bypass that missing journey by
manually stamping `amr` claims and redispatching the sign-in lifecycle.

**Guarantee/correction:** V1 must not claim local authentication, two-phase sign-in, or MFA enforcement unless one
supported application path proves the complete ceremony. The safe correction is to retire the three current packages
and remove their gate from Identity rather than leave a confirmed enrollment able to abort a real provider callback
with an exception no production surface translates. Reintroduction requires one Web Auth-owned ceremony engine with
factor contributors and end-to-end browser/API evidence.

**Complete intent surface:** The desired future common path is references plus existing `AddKoan()`; enrollment and
policy choices remain explicit business actions. Today the hidden application responsibilities listed above are
mandatory, so the current surface fails the completeness test.

**Public concepts:** Password hashes, TOTP enrollments, recovery codes, `amr`/`acr`, step-up requirements, and security
checkup signals each represent legitimate security concepts. They do not survive as V1 public types because their
composition does not produce a supported authentication result. A future build should expose factor contracts from an
isolated contracts assembly, keep factor storage/mechanics in opt-in leaves, and let Web Auth alone own the ceremony,
continuation, cookie issuance, corrective failures, and HTTP security posture.

**Docs read:** `docs/engineering/index.md` requires controller-owned HTTP, Entity-first data, typed options/constants,
and package companions; `docs/architecture/principles.md` requires one business expression, semantic honesty, standard
.NET reuse, compile-once composition, and one current path; `SEC-0007` records the dated factor design and remains as
ADR history; `src/Koan.Identity/README.md` defines the current durable-person/authentication boundary; R11-02 requires
every package to have distinct reference intent and a meaningful result.

**Code read:** `CredentialsModule`, `StepUpService`, and `StepUpSignInGate` own the split requirement/ticket machinery;
`PasswordsModule`/`PasswordCredentialService` own portable BCrypt storage and verification but no authentication
terminal; `MfaModule`, `TotpService`, and `RecoveryCodeService` own real factor mechanics but no challenge terminal;
`IdentityAuthFlowHandler` invokes gates inside cookie `OnSigningIn`; Web Auth's cookie event converts any rejection to
an `InvalidOperationException`; the factor specs simulate completion by manufacturing proof claims rather than driving
a public controller or provider.

**Reusing:** The future rebuild should retain the existing Web Auth dispatcher/cookie owner, standard ASP.NET Core
`SignInAsync`, controller routing, rate limiting and antiforgery/same-origin posture; Entity-backed factor persistence;
typed options; provider-independent `amr`/`acr`; and conditional-write capabilities where the selected Data provider
can make single-use guarantees. No implementation is retained in V1 merely because these individual pieces work.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| Deferred rebuild contract | `docs/initiatives/koan-v1/POST-CYCLE-TODO.md` | Preserve the full ceremony requirement, correct owner, and evidence gate without shipping partial security behavior. |

**Coalescence:** The closest complete pattern is Web Auth's provider challenge/callback/cookie pipeline, not the current
Identity-side gate. Ceremony state, continuation, corrective response, and session issuance have identical lifecycle
for external and Koan-managed primary factors, so Web Auth is the one future pillar owner. Factor packages should be
thin contributors plus storage mechanics; cross-module factor vocabulary must live in an inert contracts package.
Disposition: delete the present Credentials/Passwords/MFA packages and the now-unused Identity gate. Do not create a
fourth controller package or preserve `StepUpTicket` as a compatibility shell.

**Ergonomics:** Retirement is less delightful than a complete factor journey, but substantially more responsible than
making a developer or coding agent discover hidden authentication plumbing after a package reference. It restores one
honest current sentence: Koan Identity persists and governs a person after authentication; Web Auth and its verified
connectors establish sign-in. The future factor surface earns re-entry only when references + `AddKoan()` produce the
whole ceremony and startup/facts explain the selected factors and guarantees.

**Constraints satisfied:** No new HTTP is introduced; existing false/incomplete security paths are removed rather than
scaffolded; Entity-first behavior remains for supported Identity records; stable identifiers disappear with their
unsupported owner; ADR history is retained; current docs and generated truth will be updated; only the focused Identity
family suite will run before the R11-07 release boundary.

**Risks:** This intentionally removes substantial tested mechanics and three package identities before 1.0. Rebuilding
too soon inside R11 would instead create a security-critical auth product without enough end-to-end, rate-limit,
CSRF/origin, replay, key-persistence, provider-callback, and restart evidence. The deferred card must make those gates
explicit so retirement cannot be mistaken for rejecting the capability itself.

### Account-security factor family implementation

Status: `retire implemented` for `Sylin.Koan.Identity.Credentials`, `Sylin.Koan.Identity.Passwords`, and
`Sylin.Koan.Identity.Mfa`.

- Removed all three package projects and solution/test graph entries. No compatibility shell or forwarding package
  remains in the greenfield V1 graph.
- Removed `ISignInGate` and the gate loop from durable Identity. A referenced provider can no longer reach an
  Identity-owned sign-in rejection that no production challenge/continuation surface translates.
- Removed the four factor-only spec classes. They proved useful BCrypt/TOTP/recovery mechanics, but their successful
  continuation manufactured `amr` claims and called the dispatcher directly; they were not executable evidence of a
  supported application ceremony.
- Removed stale current claims from Identity package docs, capability truth, module inventory, and `SURFACES.md`.
  Historical ADRs remain unchanged; the older architecture draft now labels itself historical rather than current
  product truth.
- Added PMC-034 with the future owner and full evidence gate: one Web Auth ceremony, inert cross-module contracts,
  opt-in factor mechanics, real controller/provider round trips, continuation/replay/concurrency, key persistence,
  lockout/rate limiting, browser security, and matching runtime explanation.
- The remaining Identity integration family passes 91/91. Generated truth contains 102 packages: 10 repair-required,
  21 review-required, and 71 structurally ready; all 24 product claims still resolve.

This is architecture coalescence rather than feature denial. Authentication ceremony has one future owner; Identity
retains only guarantees it currently completes. Developers and agents no longer encounter three polished-looking
packages whose missing work begins exactly where security becomes application-critical.

### Identity × Tenancy bridge discovery

**Task:** Graduate `Sylin.Koan.Identity.Tenancy` as the one application bridge between a durable person and Koan's
tenant control plane, removing any security-shaped workflow whose guarantee ends before the application result.

**Application intent:** Reference the bridge and keep the existing `AddKoan()` bootstrap so an authenticated tenant
member can select a tenant by claim, header, subdomain, or path; receive only that membership's tenant roles; and lose
that scope on the next request when the person or seat is deprovisioned.

**Public expression:** Reference `Sylin.Koan.Identity.Tenancy` beside Identity, Tenancy, Web Auth, and a durable Data
provider. The reference contributes request resolution and effective-access facts automatically. Optional standard
configuration changes carrier names/hosts/prefixes. There is no application middleware registration, endpoint map,
repository, contributor registration, or authorization-bypass switch.

**Guarantee/correction:** A client-supplied tenant candidate becomes ambient only when the authenticated subject has a
current durable membership and an active durable Identity. The membership query both authorizes and supplies projected
tenant roles; reserved host roles never project. Missing, forged, inactive, or removed subjects proceed unscoped so
tenant-managed data fails closed without disclosing tenant existence. Full deactivation closes durable cookie sessions
and tenant scope; already-issued bearer tokens remain governed by their issuer outside tenant scope. Deprovisioning is
a fail-closed multi-write workflow with an integrity-checked operation receipt, not a database transaction, append-only
ledger, external-state proof, or global bearer revocation mechanism.

**Complete intent surface:** Carrier configuration is optional: claim `tenant`, header `X-Koan-Tenant`, and path
`/t/{code}` work by default; subdomain resolution becomes live only when `BaseHosts` is configured. Every carrier is
membership-authorized. Public/anonymous tenant routing is not a weakened mode of this capability and must return later
as a separately named business capability if a real use case and isolation contract justify it.

**Public concepts:** `Membership` remains the tenant seat and tenant-role source; `ITenantResolver` remains the carrier
extension seam; `TenantResolutionMiddleware` remains the single inbound authorization/scoping chokepoint;
`MembershipAccessContributor` composes the same roles into Identity's effective-access explanation;
`DeprovisioningService` and `DeprovisioningReceipt` remain the explicit lifecycle operation and integrity-checked
record. `InviteAcceptanceService` does not survive this slice: the current check-then-write flow cannot enforce one
accepting identity per invite under multi-node contention, and deterministic membership IDs only prevent duplicate
seats for the same person.

**Docs read:** `docs/engineering/index.md` requires Entity-first data, controller-only HTTP, typed options/constants,
and owned package companions; `docs/architecture/principles.md` requires reference intent, standard .NET reuse,
semantic honesty, and one business expression; Identity's current README establishes durable-person/session boundaries;
the Tenancy and Identity ADRs remain historical evidence and will not be edited.

**Code read:** `IdentityTenancyModule` owns carrier/contributor/service composition and startup reporting;
`TenantResolutionMiddleware` owns carrier order, membership authorization, role projection, and ambient scope;
the four resolvers own only signal extraction and code-to-id resolution; `MembershipAccessContributor` plugs into
Identity's already-discovered contributor registry; `InviteAcceptanceService` checks durable verified email then
performs non-atomic invite/membership writes; `DeprovisioningService` performs lifecycle writes and hashes a receipt;
the focused Identity suite covers carriers, forged/nonmember behavior, role projection, invitation happy paths, and
deprovisioning but does not prove cross-node invite claiming or bearer-path deactivation.

**Scoped inventory:** Stable carrier defaults and the configuration path already exist in
`TenancyResolutionOptions`; receipt surface strings are currently repeated magic values and need one internal owner.
The package has one options type and no competing options or validator. Its only public result DTO is
`InviteAcceptResult`, which leaves with the incomplete invitation workflow; no replacement request/response DTO is
needed because the retained capability is automatic request composition plus Entity-backed receipts.

**Reusing:** Standard DataAnnotations/`OptionsBuilder.Validate` for startup validation; the registry's
`[KoanDiscoverable]` interface discovery for effective-access contributors; standard ASP.NET claims/middleware;
Entity statics and deterministic membership identity; Identity status/session services; Tenancy's ambient scope and
resolver contract; module provenance and focused real-host Identity evidence.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| Receipt surface constants | `src/Koan.Identity.Tenancy/Deprovisioning/DeprovisioningSurfaces.cs` | Give stable receipt vocabulary one owner and make its tenant/cookie scope explicit. |
| Package quick contract | `src/Koan.Identity.Tenancy/README.md` | Teach the reference-only path, carrier defaults, meaningful result, and honest boundaries. |
| Package technical contract | `src/Koan.Identity.Tenancy/TECHNICAL.md` | State request ordering, status/membership enforcement, lifecycle failure semantics, and unsupported scenarios. |
| Deferred invitation contract | `docs/initiatives/koan-v1/POST-CYCLE-TODO.md` | Preserve the correct claim-state/concurrency evidence gate without shipping a misleading security workflow. |

**Coalescence:** Remove `[After]`; project dependencies already express assembly availability and the involved
registrations are order-independent. Remove the bridge's explicit `IEffectiveAccessContributor` registration because
Identity's one registry already discovers it. Remove `RequireMembership`; it creates a second unsafe authorization
mode inside the same request chokepoint. Keep carrier resolution in this bridge for now: the functional intent is not
generic host parsing but durable Identity membership authorization. Do not add a keyed local lease or bespoke CAS
fallback for invitations; a future invitation ceremony needs one explicit claim-state owner and provider-independent
failure/recovery semantics.

**Ergonomics:** The developer expression remains package reference plus existing `AddKoan()`. Humans and models can
read one invariant—“a tenant carrier scopes only an active member”—without learning activation ordering or a dangerous
escape switch. Operators see effective carrier settings and whether subdomain routing is live. Reviewers can inspect
one middleware for inbound isolation, one contributor for access explanation, and one lifecycle service for closure.

**Constraints satisfied:** Entity statics remain the data path; no HTTP controller is invented; stable receipt values
gain one constant owner; options use standard .NET validation; package companions are added; current docs and generated
truth will be aligned; ADRs remain unchanged; only the focused Identity family suite runs before R11-07.

**Risks:** Removing invitation acceptance leaves the existing Tenancy `Invite` record without a supported acceptance
ceremony; current docs must say so plainly until the future state machine exists. Identity status adds one durable read
after a matching membership is found; it is intentionally not memoized because deactivation must affect the next
request. Deprovisioning remains multi-write and may partially complete before a later provider failure; status is set
first so full deactivation fails closed, and no receipt is emitted unless the requested workflow completes.

### Identity × Tenancy bridge implementation

Status: `keep with focused proof complete` for `Sylin.Koan.Identity.Tenancy`.

- Removed module `[After]` ordering and the duplicate effective-access registration. Project dependencies express
  availability, Identity's discovered contributor registry owns contributor composition, and the bridge retains only
  its thin registrations.
- Removed `RequireMembership`. Claim, header, subdomain, and path inputs now share one invariant: only an authenticated,
  active durable person with a current membership can establish the tenant axis. Carrier names/hosts/prefixes use
  standard startup validation; subdomain routing remains visibly inert until `BaseHosts` is configured.
- Centralized inbound lifecycle enforcement in `TenantResolutionMiddleware`. The existing membership query authorizes
  and yields roles; an Identity read then rejects missing, suspended, or deactivated people, including stale bearer
  principals. Reserved host roles remain stripped at the same projection chokepoint.
- Made full deactivation close every tenant seat as well as Koan cookie sessions. Stable receipt surface names now say
  `tenant-data`, `tenant-storage`, `tenant-cache`, and `cookie-sessions`; `HasValidHash()` states the exact verification
  operation. Public comments and docs no longer call the ordered multi-write workflow atomic, append-only, signed, or
  external proof.
- Retired `InviteAcceptanceService` and its nine happy-path/mechanics facts. A deterministic seat ID did not prevent one
  invite granting two different identities under distributed contention. PMC-035 preserves the correct claim-state,
  token, recovery, assurance, and controller evidence gate.
- Dogfeeding forced the same honesty into SnapVault. Its gallery flow now uses one operator-owned
  `GalleryGrantService`: grant a known active durable person access to an event, then let stored membership plus
  `GalleryGrant` constrain tenant entry and photo reads. The token/accept controller, duplicated `GalleryInvite` row,
  pending-invite cleanup, and misleading erasure-proof language are gone. The SPA now shares an event link only after
  the explicit grant. This keeps the business story while removing a sample-only security ceremony.
- Added owned package README/TECHNICAL contracts covering the shortest reference path, carrier order, runtime
  chokepoint, startup inspectability, lifecycle failure semantics, and unsupported scenarios. The package description
  now names only supported results.
- Canonical truth remains 102 packages and 24 claims. Identity Tenancy moves from repair-required/unassessed to
  structurally ready and joins the verified authentication/authorization claim, yielding 9 repair-required,
  21 review-required, and 72 structurally ready packages.
- The rebuilt focused Identity suite passes 85/85; the rebuilt SnapVault application and test project are warning-free,
  and all 33 SnapVault dogfood facts pass. A focused restore refreshed the executable dependency graph after Identity's
  earlier contracts split; no sample/test-only framework reference was retained.

The result is one readable rule rather than two security postures: a tenant carrier is only a candidate; active durable
membership is authority. The bridge and its dogfood application now expose fewer concepts while preserving the useful
tenant/gallery lifecycle end to end.

### Tenancy core and Web family discovery

**Task:** Graduate `Sylin.Koan.Tenancy` and `Sylin.Koan.Tenancy.Web` around one complete V1 promise, coalescing
ambient isolation and operator administration while deleting lifecycle-shaped surfaces whose effects stop at the
control-plane registry.

**Application intent:** Reference Tenancy, keep the existing `AddKoan()` bootstrap, and have ordinary Entities isolate
by the current tenant across every active pillar; optionally reference Tenancy Web to administer the tenant registry
and durable membership seats through one inspectable, authorized operator surface.

**Public expression:** Core remains a package reference plus existing `AddKoan()`. An ordinary `Entity<T>` becomes
tenant-scoped; `[HostScoped]` marks the deliberate global exception; trusted work uses `using (Tenant.Use(id))` or
`Tenant.None()`. Referencing `Sylin.Koan.Tenancy.Web` additionally mounts `/tenancy` and
`/api/tenancy/admin`; no application controller registration, middleware, repository, lifecycle job, or activation
call is required. The Web surface explicitly creates/renames registry tenants and grants/revokes memberships for
known subject IDs; it does not imply invitation, tenant suspension, product-data erasure, or server-side act-as.

**Guarantee/correction:** Every non-host Entity operation consumes the same hard `tenant` segmentation contribution;
missing scope in Closed posture fails at the owning pillar, while Development receives the stable local `dev`
fallback. Adding Tenancy does not require an HTTP resolver in a headless worker: inbound carrier resolution is a
separate Identity Tenancy responsibility. The operator projection admits only the configured host operator authority,
rejects reserved host roles in memberships, uses deterministic seat identity for idempotent grants, and records each
supported mutation. Ambiguous tenant codes fail closed at resolution. Unsupported lifecycle intent has no endpoint or
type that can be mistaken for a complete guarantee; documentation supplies the corrective boundary.

**Complete intent surface:** Core requires the Tenancy reference, existing `AddKoan()`, a capable isolating Data
provider for tenant-scoped Entities, and a trusted scope source (`Tenant.Use`, captured Jobs context, or the optional
Identity Tenancy request bridge). Development needs no configuration. Production remains Closed by default but does
not assume the host is HTTP. The optional Web projection requires Web, authentication that can issue the host operator
role or configured operator identity, and a durable Data provider. Exposure host/header and operator grants remain
typed optional configuration under `Koan:Tenancy:Console`.

**Public concepts:** `Tenant`, `TenantContext`, `[HostScoped]`, `TenancyPosture`, and `TenancyOptions` each express a
scope or safety decision; `TenantRecord`, `Membership`, `TenancyRoles`, and `TenantAuditEntry` form the minimal durable
control plane; `TenancyConsoleOptions` separates forgeable exposure from authority. `ITenantResolver` and
`TenantResolutionRequest` remain public extension vocabulary but move to Identity Tenancy, their actual web-bridge
owner. `Invite`, `InviteStatus`, `TenantStatus`, `TenantBootstrap`, `TenantBootstrapPolicy`, `TenantOperation`, and the
current lifecycle service do not survive: today they imply ceremonies or effects the framework does not complete.

**Docs read:** `docs/engineering/index.md` establishes Entity-first access, controller-owned HTTP, standard typed
options/constants, and owned package companions; `docs/architecture/principles.md` requires business-to-code intent,
one composition owner, local-first behavior, semantic honesty, and standard .NET reuse; the root `README.md` defines
references plus `AddKoan()` and Entity semantics as the product grammar; `samples/CATALOG.md` redirects to the current
sample portfolio rather than stale examples; `docs/reference/cards/tenancy.md` documents the strong isolation kernel
but still understates the now-built membership/request bridge; `docs/guides/tenancy-howto.md` accurately separates
current isolation and active-member routing from deferred invitation/status/lifecycle guarantees.

**Code read:** `TenancyModule` composes posture, dev fallback, context carriage, segmentation, preflight, and reporting
but currently also checks an HTTP resolver and creates unused dev owner/signing state; `Tenant`/`TenancyAmbient`/
`TenancyRuntime` are the small hot-path scope owner; `TenantRecord`/`Membership` are the minimal durable registry while
`Invite`, bootstrap, and status types have no complete application terminal; `KoanTenancyWebModule` auto-mounts the
console but duplicates project ordering and registers jobs/leases for registry-only erase; `TenancyOperatorController`
and `TenantLifecycleService` expose a broad lifecycle vocabulary including invite, suspend, erase, and act-as whose
effects do not reach the claimed application guarantees; `TenantResolutionMiddleware` proves carrier interfaces are
owned by the Identity Tenancy bridge and already fails ambiguous codes closed.

**Reusing:** Core's semantic segmentation contribution, `KoanContext`, context carrier, `TenancyRuntime`, standard
Options, Entity statics, `[HostScoped]`, deterministic membership keys, module startup/facts reporting, controller
routing, the Web exposure/authorization split, embedded assets, and focused real-`AddKoan()` Tenancy suites.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| Resolver extension contract | `src/Koan.Identity.Tenancy/Resolution/ITenantResolver.cs` | Put the application extension seam beside the bridge that consumes it; Tenancy core remains transport/Web-neutral. |
| Resolver input contract | `src/Koan.Identity.Tenancy/Resolution/TenantResolutionRequest.cs` | Keep HTTP adaptation transport-neutral without making core Tenancy own inbound Web policy. |
| Administration service | `src/Koan.Tenancy.Web/Services/TenantAdministrationService.cs` | Give supported registry and membership mutations one audit-owning chokepoint. |
| Operator request/projection contracts | `src/Koan.Tenancy.Web/Controllers/TenancyOperatorContracts.cs` | Keep the controller thin and public HTTP vocabulary inspectable without nested type clutter. |
| Core package companions | `src/Koan.Tenancy/README.md`, `src/Koan.Tenancy/TECHNICAL.md` | State the shortest isolation path, runtime plan, prerequisites, failures, and exact boundaries. |
| Web package companions | `src/Koan.Tenancy.Web/README.md`, `src/Koan.Tenancy.Web/TECHNICAL.md` | State the reference-mounted operator path, exposure/authority model, supported mutations, and exclusions. |

**Coalescence:** Closest complete pattern: the just-graduated Identity/Identity Web split—headless durable entities and
policy in core, controller projection in Web, business mutations behind one service. Specificity is pillar-owned for
ambient isolation, bridge-owned for authenticated inbound resolution, and projection-owned for HTTP/operator policy.
Disposition: keep/rebuild the isolation kernel; absorb dev fallback directly into runtime posture; move resolver
contracts to their consuming bridge; rebuild the Web service/controller around registry + membership; delete unused
dev seed/brand/state, resolver preflight, bootstrap ceremony, invites, unenforced status, jobs-backed registry erase,
operations feed, fake act-as, and redundant `[After]`. Core is the wrong owner for HTTP resolver availability; Web is
the wrong owner for product-data erasure; adapters are the wrong owner for tenant meaning.

**Ergonomics:** Human code continues to read exactly as business intent: reference, Entity, `Tenant.Use`, optional
`[HostScoped]`. IntelliSense loses lifecycle verbs that cannot keep their promise and retains the few concepts that
matter. A coding model can infer that Web administers registry seats without inventing invite delivery, distributed
claiming, erasure, or act-as state. Operators see one startup decision for posture and one for console exposure/grant;
reviewers inspect one segmentation contribution, one authenticated request chokepoint, and one administration service.

**Constraints satisfied:** Entity statics remain every data path; all HTTP remains controller-owned; configuration
uses typed Options and one `Koan:Tenancy` family path; stable identifiers are centralized; no placeholders or
compatibility shells remain; hot paths consume cached runtime/composition state; package companions and current public
truth will be updated; ADRs stay unchanged; only focused Tenancy/Identity bridge evidence runs before R11-07.

**Risks:** This intentionally removes attractive but incomplete pre-1.0 API/UI surface. Direct membership grants name
a known subject ID without validating an external identity system; the active-Identity guarantee remains specific to
the Identity Tenancy bridge. Supported administration writes are ordered and audited, not transactional or
append-only. Tenant-code uniqueness can be enforced by the supported administration path but direct Entity writes
remain possible; request resolution therefore retains ambiguity-as-denial. Full invitation, tenant-status enforcement,
data lifecycle/erasure, and true operator act-as need distinct future capability contracts and end-to-end evidence.

### Tenancy core and Web family implementation

Status: `keep with focused proof complete` for `Sylin.Koan.Tenancy` and `Sylin.Koan.Tenancy.Web`.

- Reduced core Tenancy to one headless isolation kernel: ambient scope, resolved posture, context carriage, hard
  segmentation, and the minimal `TenantRecord`/`Membership`/`TenantAuditEntry` control plane. Open Development now
  supplies the stable local `dev` scope directly; Closed posture still fails missing tenant context at the consuming
  pillar. Core no longer requires an HTTP resolver or manufactures unused seed owner, brand, signing-key, bootstrap,
  status, or invitation state.
- Moved `ITenantResolver` and `TenantResolutionRequest` to Identity Tenancy, the only functional bridge that consumes
  them. This keeps core transport-neutral and avoids a contracts project or inert activation mechanism for vocabulary
  that has one clear owner.
- Rebuilt Tenancy Web around one `TenantAdministrationService`. The supported surface creates, inspects, and renames
  registry tenants; deterministically grants/replaces and revokes known-subject memberships; rejects duplicate routing
  codes and reserved host roles; and audits completed mutations. The controller is a thin HTTP projection, roster
  reads are complete, and audit reads are explicitly bounded by caller/options policy.
- Removed registry-only suspend/reactivate/erase, operation-job, invitation, and fake act-as surfaces. The package no
  longer depends on Jobs or keyed leases, and its embedded UI exposes only the guarantees the service completes.
  Exposure gates remain separate from host operator authority and both decisions are reported at startup.
- Consolidated configuration under `Koan:Tenancy`, with bridge resolution at `Koan:Tenancy:Resolution` and console
  policy at `Koan:Tenancy:Console`. Identity's local-person seed no longer reaches into retired Tenancy configuration.
- Added package-owned README/TECHNICAL contracts for both survivors and aligned the current guide, reference card,
  module ledger, surface ledger, topology disposition, and product claim. Invitation, lifecycle/data erasure, verified
  domains, and server-side act-as remain explicitly outside the current product boundary; PMC-035 retains the future
  invitation evidence gate.
- Focused Release evidence is warning-clean: Tenancy passes 87/87, Tenancy Web passes 13/13, and Identity Tenancy's
  bridge evidence remains green inside Identity's 85/85 suite. PMC-031 is resolved by an isolated Local Storage profile
  in the intentionally composed Tenancy fixture; PMC-033 still owns framework-wide unused Storage activation.
- Release packs for both packages contain DLL/XML, package-owned README, canonical icon, symbols, and build-transitive
  activation metadata; dependency manifests contain only their evaluated functional requirements and current NuGet
  audit reports no known vulnerable direct or transitive package. Generated truth contains 102 packages: 7
  repair-required, 21 review-required, and 74 structurally ready across 25 claims. Both Tenancy packages have zero
  objective package-quality findings, and public documentation passes 230 current files / 40 navigation targets.

The resulting developer grammar is smaller and more honest: reference Tenancy and keep `AddKoan()` for local-first
Entity isolation; add Tenancy Web only when registry and seat administration is desired. Operators and reviewers can
trace scope, inbound authorization, and supported mutations through one chokepoint each.

### Classification family discovery

**Task:** Graduate `Sylin.Koan.Classification` around one complete field-at-rest guarantee, rebuilding its contribution,
key-scope, and failure boundaries while deleting searchable/masking/erasure-shaped surfaces the product does not finish.

**Application intent:** “This Entity property contains sensitive data; protect its stored value without adding crypto
code to my domain or changing how I save and read the Entity.”

**Public expression:** Reference `Sylin.Koan.Classification` beside a Data provider, keep the existing `AddKoan()`, and
decorate writable string properties with `[Pii]`, `[Phi]`, `[Pci]`, `[Secret]`, or `[Classified("category")]`:

```csharp
public sealed class Patient : Entity<Patient>
{
    [Pii] public string Email { get; set; } = "";
}
```

Development needs no configuration and uses an ephemeral local key provider. Outside Development, the default
ephemeral provider refuses startup; an application or future provider package must supply the isolated
`IClassificationKeyProvider` contract before `AddKoan()` completes composition. When Tenancy is active, the same
compiled segmentation scope automatically partitions active encryption keys; the developer does not implement a
tenant accessor.

**Guarantee/correction:** Every supported Entity write path persists an AES-256-GCM envelope on a clone while the
caller's instance remains plaintext; every supported Entity materialization reverses the envelope before returning it;
classified Entities are excluded from distributed Entity cache; active hard segmentation contributes to key scope.
Malformed envelopes, tamper, missing keys, unsupported property types, missing tenant scope, and ephemeral keys outside
Development fail loudly. The guarantee is storage-at-rest protection for writable string properties only. It does not
promise searchable ciphertext, tokenization, caller-specific masking, log/message/vector redaction, existing-row
backfill, key destruction/erasure certificates, raw provider bypass coverage, or production key custody.

**Complete intent surface:** The ordinary local path has no action beyond package reference, existing `AddKoan()`, and
the attribute. A non-Development deployment additionally supplies one `IClassificationKeyProvider` through standard
.NET DI or a future provider reference. Existing plaintext rows are tolerated on read but remain plaintext until the
application performs an explicit business migration/rewrite; no hidden scan or backfill occurs.

**Public concepts:** Classification attributes state business facts; `IClassificationKeyProvider` is the one explicit
production-custody extension seam; `ClassificationDataKey` and `ClassificationKeyUnavailableException` are its minimal
contract vocabulary; `ClassificationIntegrityException` is the corrective application-visible failure for invalid or
tampered stored envelopes. AES mechanics, envelope format, segmentation-key encoding, field-transform construction,
and provider-plan compilation remain internal.

**Docs read:** `docs/engineering/index.md` requires Entity statics, owned package docs, standard options/constants, and
focused validation; `docs/architecture/principles.md` requires business-to-code intent, host-owned compiled plans,
semantic honesty, local-first defaults, and isolated cross-module contracts; the root README establishes references +
`AddKoan()` + Entity decorations as the public grammar; ARCH-0098 preserves the accepted field-transform/clone/read-
reverse mechanism but also records unfinished searchable, masking, KMS, shred, and leak-guard phases that cannot be
presented as current product truth; R11 generated truth marks Classification repair-required and unassessed.

**Code read:** `ClassificationModule` currently registers ephemeral crypto then mutates two process-static registries
from `Start`; `ClassificationFieldTransform` resolves host services per operation and depends on a bespoke tenant
accessor; `EphemeralKeyProvider` proves AES key rotation but permits restart loss and exposes an incomplete shred verb;
`StorageFieldTransformRegistry`/`StorageFieldTransformPlan` duplicate contribution ownership outside DI and cache plans
process-wide; `RepositoryFacade` already provides the correct unavoidable clone/write and reverse/read chokepoints;
the focused Classification suite proves SQLite at-rest ciphertext, read/write coverage, caller plaintext, and cache
exclusion but does not prove production refusal, host isolation, automatic Tenancy key scoping, malformed-envelope
failure, or a package-only consumer.

**Scoped inventory:** AES sizes, envelope magic, and rotation threshold already exist as implementation constants;
there is no Classification options family and none is needed for the supported promise. Classification attributes and
property bags already exist in Data Abstractions, but their `Searchable` and masked-read language advertises absent
behavior. `IFieldTransform`, its contributor seam, and cache-inspection vocabulary are cross-module contracts currently
owned by functional Data Core. `IKeyProvider` is an intended external/provider seam currently owned by the functional
Classification assembly. Neither placement satisfies the contract-isolation mandate.

**Reusing:** The existing attributes/category facts, expression-compiled property bag, AES-GCM primitive, envelope
shape, count-aware ephemeral rotation, RepositoryFacade's exhaustive clone/reverse chokepoint, Core's compiled
`SegmentationPlan`, standard DI enumerable contributors, Data's host singleton/type memo pattern, cache exclusion,
module startup reporting, and the focused SQLite raw-at-rest fixture.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| Minimal key-custody contracts | `src/Koan.Classification.Contracts/` | Let applications and future KMS/provider modules implement custody without activating Classification; no inert metadata crutch. |
| Field-transform contributor/inspection contracts | `src/Koan.Data.Abstractions/Pipeline/` | They are consumed by Classification, Data Core, and Cache and therefore belong in the existing inert Data contract assembly. |
| Classification transform contributor | `src/Koan.Classification/Pipeline/ClassificationFieldTransformContributor.cs` | Give one DI-owned capability object responsibility for applicability, crypto dependencies, and compiled segmentation scope. |
| Stable key-scope encoder | `src/Koan.Classification/Infrastructure/ClassificationKeyScope.cs` | Convert Core segmentation bindings to one deterministic opaque provider scope without tenant-specific coupling or delimiter collisions. |
| Package companions | `src/Koan.Classification*/README.md`, `TECHNICAL.md` | Publish the local path, production responsibility, startup truth, exact guarantee, and explicit exclusions for both runtime and contracts packages. |

**Coalescence:** Closest pattern: Data's host-owned `DataSegmentationPlan` plus DI-owned read contributors. Rebuild the
field-transform path at the same specificity: inert contracts in Data Abstractions, one host-owned/type-memoized plan
in Data Core, and one thin Classification contributor. Delete `FieldTransformContributor`,
`StorageFieldTransformRegistry`, static `ClassifiedFieldRegistry` activation, per-operation `AppHost` service lookup,
and `IClassificationTenantAccessor`. Do not broaden Core into classification handling; it compiles generic round-trip
transforms only. Do not put key custody in Data Abstractions; it is classification-specific and deserves one inert
Classification Contracts package. Remove `Searchable`, masked-read, tokenization, and shred APIs until complete owners
and evidence exist.

**Ergonomics:** Human and coding-model code remains one business decoration; Tenancy scoping appears automatically
from the same compiled semantic constitution; local use has no key ceremony; production refusal names the single
explicit custody responsibility. IntelliSense contains only facts and guarantees that work now. Operators see the
selected key provider, scope mode, cipher, local/durable posture, and unsupported boundaries. Reviewers inspect one
host plan, one contributor, one transform, and one unavoidable Data facade rather than process-static registries and
ambient host lookup.

**Constraints satisfied:** Entity statics remain every data path; no HTTP is introduced; stable storage identifiers
stay centralized; no tunable options are invented; cross-module contracts are isolated; large data behavior remains
provider-bounded by the existing facade; package companions and current public truth will be updated; ADR history stays
unchanged; only Classification/Data seam/Cache-focused evidence runs before R11-07.

**Risks:** This intentionally narrows public attributes and crypto APIs before 1.0. Custom production key-provider
quality cannot be inferred from interface implementation, so Koan can prove only that the unsafe built-in provider is
rejected outside Development and report the selected concrete type. Legacy plaintext tolerance is migration-friendly
but not a backfill guarantee. Moving the generic transform plan from process-static state touches Data Core, Cache, and
adapter conformance test kits; focused affected suites must prove host isolation and preserve every write/read path.

### Classification family closure

**Disposition:** Pass `Sylin.Koan.Classification` as the functional capability and introduce/pass
`Sylin.Koan.Classification.Contracts` as its inert production-custody boundary. Both have terminal `keep`
dispositions. The contract package has no functional dependencies or module activation; a future vault/KMS provider can
consume it without an `Inert` reference annotation or accidental Classification activation.

**Architecture landed:** Data Abstractions now owns the neutral `IFieldTransform`, contributor, and inspector
contracts. Data Core compiles contributors once per host in deterministic order, rejects duplicate identities, memoizes
one immutable plan per Entity type, clones once and writes forward, then materializes in reverse order. Classification
contributes one DI-owned transform and receives Core's already-compiled `SegmentationPlan`; Cache asks only the neutral
inspector whether an Entity has transforms. Process-static transform/classification registries, per-operation
`AppHost.Current` lookup, the Classification-specific tenant accessor, and the unused adapter-surface helper are gone.

**Developer result:** Reference Classification, keep `AddKoan()`, and annotate a writable string property. Development
gets a complete in-memory key provider with no setup. Referencing Tenancy automatically changes key scope through the
shared segmentation constitution; no tenant configuration or Classification tenant API exists. Outside Development,
the ephemeral provider refuses startup and names the one responsibility: register `IClassificationKeyProvider` before
`AddKoan()`. Unsupported `Searchable`, masking, tokenization, and shred-shaped APIs were removed rather than preserved
as 1.0 ceremony.

**Safety and inspectability:** AES-256-GCM envelopes fail loudly on malformed reserved prefixes, authentication failure,
missing keys, invalid key sizes, unsupported classified property types, and missing hard segmentation. Existing
plaintext remains readable but is not silently backfilled. Classified Entity types are excluded from distributed Entity
cache. Human startup reporting, resolved composition, and the shared operator/agent facts envelope identify cipher,
concrete key provider, segmentation ownership, cache posture, and exclusions without exposing scopes or values.

**Focused evidence:** Classification passes 55/55, covering all supported Entity write/read paths against raw SQLite,
caller plaintext, cache exclusion, strict envelope integrity, key rotation/loss/disposal, production refusal/custom
provider acceptance, deterministic opaque scopes, automatic Tenancy partitioning, and exact startup facts. The shared
Data pipeline passes 17/17 including host isolation and forward/reverse composition; Data-axis explanation passes 9/9;
Cache topology passes 7/7. The Data-axis project still emits the already-recorded stale-reference `MSB9008` owned by
PMC-032; its focused facts pass. No full release certification ran.

**Artifact and public truth:** Both Release packages build and pack warning-clean with DLL/XML, package-owned README,
canonical icon, symbols, and build-transitive composition metadata. Their package icons are byte-identical to the
repository canonical icon and current direct/transitive NuGet audits report no known vulnerable package. Generated
truth contains 103 packages: 6 repair-required, 21 review-required, and 76 structurally ready across 26 claims. Both
Classification packages have zero objective quality findings; the public documentation truth gate passes 234 current
files / 40 navigation targets.

### Canon family discovery and architecture checkpoint

**Task:** Graduate the post-R10 Canon family by giving every discovered canonical Entity one compiled decision,
removing package and public surfaces that do not carry an independent V1 intent, and stating the exact storage and Web
failure boundary without rebuilding CustomerCanon or Canon's automatic contributor architecture.

**Application intent:** “Turn imperfect arrivals into one trusted Entity through automatically discovered business
rules and deterministic convergence; reject invalid arrivals with useful reasons, and expose the same compiled model
decision through HTTP only when I reference Canon Web.”

**Public expression:** The already-passed expression remains unchanged. A headless application references
`Sylin.Koan.Canon` and a Data provider, keeps its existing `AddKoan()`, and defines a canonical Entity. A Web
application references `Sylin.Koan.Canon.Web` instead and keeps the standard four-line host:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan().AsWebApi();
var app = builder.Build();
await app.RunAsync();
```

```csharp
public sealed class Customer : CanonEntity<Customer>
{
    [AggregationKey]
    public string Email { get; set; } = "";
}
```

An `ICanonPipelineContributor<Customer>` remains optional business policy. When present it is discovered
automatically; when absent the model still receives Canon's built-in aggregation and policy pipeline. There is no
direct Contracts-package reference, Canon registrar, runtime builder, application module, generated-admin setup, or
sample-specific controller in the supported expression.

**Guarantee/correction:** Every discovered `CanonEntity<T>` receives exactly one immutable host-owned model/pipeline
decision, including built-in aggregation and policy contributors even when no custom contributor exists. Runtime,
startup, facts, and Web consume that decision rather than rediscovering models. A duplicate HTTP slug rejects host
composition with both conflicting CLR types. Within a phase, the first failed or parked contributor terminates
immediately; a later contributor cannot overwrite or run after that terminal business decision.

Failed and parked business outcomes retain R10's proven non-persistence guarantee. Successful canonization returns
only after canonical persistence, every pending aggregation-index write, and required audit writes complete. The
default Data-backed implementation deliberately orders canonical state before indexes and audit so it never publishes
an index to a missing canonical Entity. Those writes are not one provider transaction: an index failure can leave an
unindexed canonical snapshot, and an audit failure can leave canonical state plus indexes. The original exception must
propagate with a corrective operation/checkpoint message; Canon does not claim rollback, atomic convergence, safe
blind retry, durable replay, or automatic recovery. A custom `ICanonPersistence` remains the explicit owner when an
application requires a stronger storage contract.

**Complete intent surface:** Package reference, existing `AddKoan()`, `CanonEntity<T>`, at least one
`[AggregationKey]`, ordinary Entity/Data runtime prerequisites, and optional discovered contributors are the complete
headless surface. Referencing Canon Web additionally mounts only model inspection and Canon-aware Entity routes;
ordinary host authentication/fallback authorization governs those business routes. Rebuild remains an injected
runtime/application operation that an application may place behind its own authorized workflow. Canon Web will no
longer manufacture a privileged admin/replay control plane.

**Public concepts:** `CanonEntity<T>` declares governed canonical state; `[AggregationKey]` declares convergence
identity; `[AggregationPolicy]` and `[Canon(audit: true)]` declare real conflict/audit decisions;
`ICanonPipelineContributor<T>`, phase/status/event/context, and operation options/results carry application business
rules and outcomes; `ICanonRuntime`, `ICanonPersistence`, and `ICanonAuditSink` remain the narrow standard-DI runtime,
complete-storage, and audit customization seams. These concepts require the active Canon capability and therefore
belong in functional Canon. A read-only Canon composition plan is a family/module contract consumed by Canon Web and
facts, not a second application registration surface.

**Already completed by R10-11:** Preserve the automatic generated-registry contributor discovery, functional/Web
activation split, one `CanonModule`, deterministic phase/order/type ordering, four-line CustomerCanon host, generated
Canon Entity controller, successful customer convergence, 422 failure projection, and failed/parked termination before
canonical/index persistence. CustomerCanon's model, policy, contributors, and golden test are evidence to retain, not
an architecture to rebuild.

**New R11 corrections:** Current source leaves seven distinct post-R10 gaps: contributor-free models never enter
`CanonPipelineBuilder<T>` and therefore bypass default aggregation/policy; Web rediscovers models into a second
catalog; duplicate slugs overwrite silently; Contracts activates functional Data Core and its `CanonEntity<T>` uses
ambient `AppHost`; the optimization subsystem is disconnected from execution; generated admin/replay routes are
unauthenticated; and successful commits are ordered but non-transactional and untested under write failure. The
within-phase last-event-wins rule and the legacy process-global `FlowPillarManifest` are additional current-code
ownership defects. None is a pre-R10 sample-registration or failure-persistence defect.

**Docs read:** `CLAUDE.md` establishes references + `AddKoan()` + Entity as the golden grammar and requires genuine
contract isolation; `docs/engineering/index.md` requires Entity-first data, controller-only HTTP, standard
constants/options, package companions, and focused proof; `docs/architecture/principles.md` requires one host-owned
compiled authority and corrective semantic honesty; the root README states the four-line host and unified runtime
facts contract; `docs/toc.yml` shows Canon is not currently in the public navigation set; `samples/CATALOG.md` defers
sample authority to the graduated portfolio; the current Canon reference/guide and three package companions describe
the R10 result but overclaim contributor-free defaults and an inert Contracts boundary, and delegate generated-admin
security to each application; R10-11 is passed evidence and explicitly forbids returning to the former registrar,
controller, or Web-owned activation architecture.

**Code read:** `CanonPipelineDiscovery` groups only discovered contributor bindings, so models without contributors
never receive a descriptor; `CanonPipelineBuilder<T>.Build` already supplies the correct built-in aggregation/policy
steps once invoked; `CanonRuntime` owns the correct R10 terminal-outcome gate but lets later contributors overwrite an
earlier terminal event and persists canonical → indexes → audit without an atomic guarantee; `CanonWebModule` performs
a second `ICanonModel` registry walk, derives routes independently, and registers a mutable-looking duplicate catalog;
`CanonModelCatalog` silently replaces a prior slug; `CanonAdminController` exposes process records and reflective
rebuild with no authorization; `CanonEntity<T>.Canonize()` proves Contracts is functional by resolving
`ICanonRuntime` from ambient `AppHost`; `FlowPillarManifest` is Canon's only activation call into its legacy static
catalog and still claims the retired Flow name plus Orchestration namespaces; Classification's module/composition fact
is the closest graduated startup/facts pattern because one owner reports one exact guarantee and its exclusions.

**Scoped inventory:** Existing stable Web route constants, aggregation context keys, operation options, metadata,
attributes, default contributors, persistence/audit seams, generic controllers, standard DI, generated registry, and
Core composition facts are reusable. Canon has no typed host-options family that the supported promise needs. The
only Canon optimization options are disconnected and should be deleted. The Web response/request records are
controller-local. No inline endpoint exists. No source package references Canon Contracts independently of functional
Canon/Web; its only direct project consumers are those two packages and Canon's own test projects. No source consumer
outside Canon tests uses the runtime builder/configuration/observer/replay surfaces, `CanonValueObject<T>`, or the
optimization subsystem.

**Reusing:** R10's generated discovery marker, `KoanRegistry`, `CanonPipelineBuilder<T>` built-in construction law,
aggregation metadata cache, contributors, runtime context, `ICanonPersistence`, `ICanonAuditSink`, Entity/Data
chokepoints, Web generic-controller composition, standard ASP.NET fallback authorization, Core
`KoanCompositionBuilder`, Classification's startup/facts proof shape, and the CustomerCanon cumulative host test.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| immutable Canon composition/model plan | `src/Koan.Canon/Composition/` | Compile all discovered Canon models, built-ins, and custom contributors once at the functional owner; runtime, Web, startup, and facts consume it. |
| Canon composition compiler | `src/Koan.Canon/Composition/CanonCompositionCompiler.cs` | Keep generated-registry reflection and validation at one boot-time owner rather than runtime or Web. |
| diagnostics/constants additions | `src/Koan.Canon/Infrastructure/Constants.cs` | Centralize stable capability, correction, and composition-fact identifiers; absorb scattered internal context identifiers that survive. |
| Web projection plan/catalog rebuild | `src/Koan.Canon.Web/Catalog/` | Derive routes from the Canon plan once, reject slug ambiguity, and register controllers without rediscovering models. |
| composition/commit focused specs | existing Canon unit and integration projects | Prove contributor-free defaults, host isolation, first-terminal-wins, exact partial-write ordering, and corrective exceptions without a broad release run. |
| Web composition/security specs | Canon integration project plus CustomerCanon golden test | Prove one shared model decision, duplicate-slug boot rejection, absence of generated admin/replay routes, and preservation of the R10 HTTP journey. |
| package/current-doc companions | surviving Canon and Canon Web project roots plus current Canon reference/guide/product truth | State the two-package reference intent, smallest result, exact commit/security limits, and retired surfaces. |

**Coalescence:** Closest pattern: Classification's active functional package plus one immutable composition/facts
authority, combined with the accepted Identity/Tenancy functional-Web split where Web projects an already-owned
domain service and secures or omits privileged operations. Specificity is Canon-family composition in functional
Canon, HTTP route identity in Canon Web, generic facts law in Core, and persistence mechanics in the selected Data or
custom Canon persistence owner.

Disposition proposal: `keep` `Sylin.Koan.Canon`; `merge` `Sylin.Koan.Canon.Contracts` into it; `keep`
`Sylin.Koan.Canon.Web`. Absorb model vocabulary, contributor/runtime contracts, and metadata into functional Canon
because every useful consumer requires Canon activation and the current package cannot be inert. Rebuild the existing
runtime configuration into the one model/composition plan and make Web a projection of it. Retire the disconnected
optimization folder and isolated tests; generated `CanonAdminController`, process replay records/capacity, mutable
observer registration, public manual `AddCanonRuntime`/builder/configuration machinery, unproved
`CanonValueObject<T>` auto-CRUD, and `FlowPillarManifest`. Keep runtime `RebuildViews` as a headless application
operation but delete its generated unauthenticated HTTP route. No compatibility package or alias survives.

Functional Canon is the one correct owner because it defines canonical model eligibility, default pipeline meaning,
terminal outcomes, and commit order. Core is too wide to know Canon phases or aggregation. Web is too narrow to
discover/compile domain models. Contracts is not an honest narrower owner because its useful types inherit functional
Entity behavior, resolve the runtime, and have no independent module consumer. An adapter is too narrow to coordinate
canonical state, indexes, and audit semantics.

**Ergonomics:** Human and coding-model code keeps the R10 business sentence and loses a direct package choice plus
advanced setup branches. IntelliSense begins with `CanonEntity<T>`, aggregation attributes, optional contributors,
and `Canonize`; it no longer advertises a manual runtime builder, disconnected optimization controls, process replay,
or value-object CRUD that the product does not complete. Operators see the discovered models, built-in/custom pipeline
posture, Data-backed non-atomic commit boundary, and exclusions from one fact source. Reviewers inspect one compiler,
one immutable plan, one runtime chokepoint, and one Web projection instead of two discovery catalogs and a global
pillar mutation.

**Constraints satisfied:**

- Entity statics and `CanonEntity<T>` remain the application data/semantic language; CustomerCanon is not rebuilt.
- All HTTP remains controller-owned; retiring the admin controller removes routes rather than replacing them inline.
- Stable identifiers move to project-scoped constants; no new host option or magic activation metadata is invented.
- Standard DI owns persistence/audit overrides; `AddKoan()` remains the only framework bootstrap expression.
- Structural discovery and pipeline construction run once per host; runtime operations consume an immutable plan.
- The default write boundary is documented and tested without claiming unsupported provider transactions or retry.
- Current companions, product/package truth, and R11-02 dispositions will change with implementation; ADRs remain
  dated history.
- Focused Canon owner/Web/sample/build/pack/documentation proof only; no Tenancy/Classification rerun or R11-07
  release ratchet without an affected dependency.

**Risks:** Merging Contracts and deleting advanced public surfaces is intentionally breaking before 1.0 and changes
the package count/product claim. A read-only plan shared from functional Canon to Canon Web must remain a family
projection contract rather than becoming application registration ceremony. The default persistence path cannot
provide cross-Entity atomicity on every Data provider; implementation must preserve truthful ordered failure and may
not invent rollback. Removing `CanonValueObject<T>` is justified by current source—its comment promises canonization,
runtime cannot canonize it, Web exposes only generic CRUD, and no sample/source consumer exists—but it is included in
this checkpoint because it materially narrows the public promise. Stage-only/parked behavior and headless rebuild are
retained; durable sweep/recovery, distributed locking, delivery, and transaction capability remain outside V1 unless
separately designed and evidenced.

**Architecture checkpoint:** No production edit follows this record until the maintainer accepts or adjusts the three
terminal dispositions and the proposed promise reductions: Contracts merge; retirement of manual builder/replay/
observer/optimization/value-object/admin surfaces; and an explicitly ordered, fail-loud but non-atomic default commit
guarantee. R10-11's automatic contributor architecture and CustomerCanon result are not under reconsideration.

### Canon implementation closure (2026-07-18)

The maintainer accepted the checkpoint without adjustment. The implementation followed the recorded topology and did
not reopen R10-11: CustomerCanon remains the same four-line host, application contributors remain automatically
discovered, functional/Web ownership remains split, and failed/parked pipeline outcomes remain non-canonical.

**Implemented R11 corrections:**

- `CanonCompositionCompiler` now produces one immutable host plan for every discovered `CanonEntity<T>`, including
  contributor-free models. Runtime registration, pipeline metadata, startup/facts, and Canon Web consume that plan;
  Web no longer performs an independent model discovery pass.
- Every planned model receives built-in aggregation and policy contributors. Custom contributors remain ordered by
  phase, `Order`, and CLR name. The first failed or parked contributor stops its phase and the operation, so a later
  contributor cannot overwrite a terminal result.
- The default commit now exposes exact fail-loud checkpoints in the existing order: canonical Entity, aggregation
  indexes, audit. Provider exceptions remain inner exceptions. Canon attempts no later checkpoint after failure and
  makes no rollback, atomicity, blind-retry, or recovery claim; index failure explicitly admits a durable prefix.
- Canon Web projects routes from the shared plan, rejects duplicate slugs with both CLR type names, and retains only
  Canon-aware Entity routes plus `/api/canon/models`. Generated admin/replay/rebuild and value-object routes are gone;
  headless `ICanonRuntime.RebuildViews<T>` remains available to application code.
- `Sylin.Koan.Canon.Contracts` was merged into `Sylin.Koan.Canon` and removed from the solution, project references,
  sample lockfile, generated package truth, and verified product claim. There is no compatibility package.
- Public manual runtime builder/configuration, observer/replay records and capacity, disconnected optimization types,
  `CanonValueObject<T>`, and `FlowPillarManifest` were retired. `ICanonRuntime`, `ICanonPersistence`,
  `ICanonAuditSink`, contributors, options, results, and read-only pipeline metadata remain the earned public surface.

**Focused evidence:**

- Canon unit suite: 35/35, including contributor-free default composition, standard-DI persistence replacement,
  first-terminal-wins, and canonical/index/audit checkpoint failure behavior.
- Canon integration suite: 7/7, including exact Web projection of the host plan, duplicate-slug rejection, and absence
  of the admin controller.
- CustomerCanon real-host golden path: 1/1, including same-id convergence, invalid non-persistence, admin-route 404,
  and composition facts that disclose the ordered non-atomic commit.
- `Sylin.Koan.Canon` and `Sylin.Koan.Canon.Web` built and packed in Release with package-owned README, canonical icon,
  XML documentation, build-transitive props, symbol package, and exact evaluated dependencies. Neither nupkg references
  the retired Contracts identity. Current direct/transitive vulnerability audit reports no known vulnerable packages.
- Generated package quality and product surface were regenerated: 102 evaluated packages, 6 repair-required, 18
  review-required, 78 structurally ready, and 26 claims. Both Canon survivors are structurally ready with no findings;
  the verified Canon claim contains exactly those two packages.

R11-02 now records `keep` Canon, implemented `merge` Contracts, and `keep` Canon Web. Full solution/release
certification, publication, tagging, and remote mutation remain intentionally deferred to R11-07.

## RabbitMQ Communication provider discovery and disposition proposal (architecture checkpoint)

**Task:** Graduate `Sylin.Koan.Communication.Connector.RabbitMq` from temporary `assess` without reopening the passed
R07-10 provider-election/transport rebuild; retain its earned external-reach intent, remove only current unearned
branches/surfaces, and align package/product truth with focused real-provider evidence.

**Application intent:** An application adds one RabbitMQ connector reference and keeps its Entity Transport code;
Koan moves that snapshot onto the application mesh with broker-confirmed acceptance, authenticated context carriage,
typed receiver-group fan-out, and no silent reduction back to process-local reach.

**Public expression:**

```powershell
dotnet add package Sylin.Koan.Communication.Connector.RabbitMq
```

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddKoan();

public sealed class ImportOrder : IReceiveEntity<Order>
{
    public Task Receive(Order order, CancellationToken ct) => Task.CompletedTask;
}

await order.Transport.Send(ct);
```

The direct reference is provider intent. With Koan orchestration, no Rabbit-specific registration or endpoint setting
is required. An existing broker uses standard .NET configuration:

```json
{
  "ConnectionStrings": {
    "RabbitMq": "amqp://app:secret@rabbitmq:5672"
  }
}
```

Publisher and receiver participants must share the application mesh identity, compatible CLR contract identities,
declared business channel, and trust material. RabbitMQ must be reachable, or Koan orchestration must be allowed to
provision it. No provider pin is part of the common direct-reference path.

**Guarantee/correction:** Awaiting `Transport.Send` means RabbitMQ confirmed a mandatory persistent publication to
Koan's durable mesh exchange and did not report a missing receiver-group route. Each stable receiver group owns a
durable competing queue; distinct groups fan out independent host-deserialized Entity copies. The receipt reports
`rabbitmq`, `durably-acknowledged`, and `SettlementObservable=false`; it does not imply handler completion. An elected
connector that cannot start, authenticate, route, or confirm fails with a typed/corrective provider boundary and never
falls back locally. Invalid or failed inbound envelopes are rejected without requeue because retry/dead-letter policy
is explicitly absent.

**Complete intent surface:** The complete normal action is the direct package reference, ordinary `AddKoan()`, existing
business receiver, and existing `Entity.Transport.Send`. A standard connection string is needed only for an existing
broker when discovery/orchestration cannot supply it. A named Communication channel/provider pin remains a deliberate
Core Communication policy override, not Rabbit-specific setup. `MeshTrustKey`, provisioning credentials, prefetch,
and publication timeout are optional operator decisions under `Koan:Communication:RabbitMq`; there is no queue,
exchange, routing-key, handler-registration, or arbitrary-message API.

**Public concepts:**

- `Sylin.Koan.Communication.Connector.RabbitMq` — the one application-visible decision to extend Transport and
  framework-owned routes beyond the process.
- `RabbitMqCommunicationOptions` — earned operator controls for mesh trust, provisioned/discovered endpoint
  credentials, consumer prefetch, and confirmed-publication timeout. Its duplicate `ConnectionString` property is not
  earned once standard `ConnectionStrings:RabbitMq` is the canonical explicit endpoint.
- `RabbitMqModule` — the generated functional activation owner required by the package reference.
- `RabbitMqDiscoveryAdapter` and `RabbitMqOrchestrationEvaluator` — package mechanics, not application contracts;
  current repository consumers do not instantiate them outside the connector's friend test assembly.
- Entity Transport, receivers, acceptance/settlement, business channels, adapter contracts, host wire, and provider
  election remain owned by `Sylin.Koan.Communication`; RabbitMQ adds no second grammar.

**Docs read:**

- `CLAUDE.md` — requires reference-as-intent, Entity Communication vocabulary, one host composition, adapter-owned
  mechanics, and focused evidence; directly governing.
- `docs/engineering/index.md` — requires typed options, centralized constants, package companions, and focused package
  validation; directly governing.
- `docs/architecture/principles.md` — places semantic route/election policy in Communication and backend realization
  in the adapter; directly governing.
- `docs/toc.yml` — already links the current Communication reference; no new navigation branch is needed.
- root `README.md` — teaches unchanged `Entity.Events`/`Entity.Transport` grammar and connector-transparent reach.
- `samples/CATALOG.md` — retires the historical sample catalog; no stale catalog entry is an authority for this slice.
- `R07-10-communication-provider-election.md` — proves the provider boundary, real RabbitMQ topology, authenticated
  carriage, fail-closed reach, and explicit non-claims already passed; it must not be rebuilt.
- `src/Koan.Communication/{README,TECHNICAL}.md` and `docs/reference/communication/index.md` — state the current pillar
  contract, named channels, internal routes, settlement distinction, and Rabbit limits; relevant current truth.
- `src/Connectors/Communication/RabbitMq/{README,TECHNICAL}.md` — accurately describes most mechanics and limits but
  lacks the exact NuGet title/recognized meaningful-use heading and still teaches a Koan-specific endpoint branch.
- generated package/product references — classify RabbitMQ correctly as a provider under the verified Communication
  claim; only two README findings remain, while claim evidence still points to older broad initiative pages.

**Code read:**

- `RabbitMqModule.cs` — one module registers options, discovery, orchestration, adapter, and health; correct owner,
  though its fixed `default` claim text understates named-channel candidacy.
- `RabbitMqCommunicationAdapter.cs` — one host-lifetime connection plus publisher/consumer channels implements
  confirmed mandatory publication, topology, HMAC verification, manual settlement, and clean shutdown; directly
  relevant and retained.
- `RabbitMqCommunicationOptions.cs` and `Infrastructure/Constants.cs` — typed tunables and most identifiers already
  exist; one duplicate endpoint property, legacy keys, a legacy Koan environment alias, and scattered stable
  image/port/topology values remain.
- `RabbitMqDiscoveryAdapter.cs` — startup-only standard discovery with real AMQP health and credential normalization;
  keep the mechanism, internalize the type, and read one standard connection string plus `RABBITMQ_URL`/Aspire.
- `RabbitMqOrchestrationEvaluator.cs` and `RabbitMqServiceDescriptor.cs` — direct-reference provisioning and service
  metadata share one physical lifecycle; keep in this package and centralize their broker constants.
- `RabbitMqHealthContributor.cs` — elected-only criticality projects the adapter's actual state; retain unchanged.
- `CommunicationAdapterContracts.cs` and `CommunicationRouter.cs` — Communication already owns the immutable adapter
  declaration, per-route election, host wire/bindings, context trust gate, and no-fallback policy; RabbitMQ must not
  duplicate them.
- `RabbitMqTransportSpec.cs`, `RabbitMqTopologySpec.cs`, and `ProviderElectionSpec.cs` — nine Rabbit-specific cells plus
  provider-neutral election cells prove direct intent, named channels, group/node fan-out, authenticated Tenancy
  context, no-route correction, orchestration, health posture, and topology identity.
- Mongo's graduated discovery/package pattern — closest sibling: discovery mechanics are internal, standard connection
  strings are the first explicit endpoint, package docs state one meaningful result and operational limits.

**Reusing:** `Entity.Transport.Send`, `IReceiveEntity<T>`, Communication's immutable provider catalog/router, host-owned
wire codec and ingress, context trust plan, `ServiceDiscoveryAdapterBase`, `BaseOrchestrationEvaluator`,
`RabbitMqCommunicationOptions`, existing project constants, RabbitMQ Client 7, health/facts projections, the nine
RabbitMQ cells, the provider-neutral election suite, and the generated quality/product compilers.

**Creating new:** No new file, service, abstraction, option family, endpoint, or application concept is proposed.

| New code | Location | Justification |
|---|---|---|
| broker image/tag, AMQP/management ports, orchestration health command/timeouts, and topology/wire generation constants | existing `src/Connectors/Communication/RabbitMq/Infrastructure/Constants.cs` | Remove stable broker/protocol literals from module, service descriptor, orchestration, and adapter code without adding a new owner. |

Existing code changes are deliberately narrow: remove `RabbitMqCommunicationOptions.ConnectionString`; remove
`Koan:Messaging:*`, `Koan:Communication:RabbitMq:ConnectionString`, and `Koan_RABBITMQ_URL` endpoint branches; use
`ConnectionStrings:RabbitMq` as the one explicit endpoint plus standard `RABBITMQ_URL`, Aspire, or orchestration;
internalize discovery/orchestration mechanics; make module reporting lane-accurate; update existing tests to the
standard endpoint; and graduate current package/product prose and evidence.

**Coalescence:** Closest pattern: the graduated Mongo provider combines a pillar-owned provider contract with one
mechanical connector package, internal discovery, standard connection strings, selection-aware health, and exact
package docs. RabbitMQ's current decision owner is `CommunicationRouter`; its consumers are Entity Transport plus
Jobs competing signals and Cache node broadcasts; adapter state is one host-lifetime connection/channel set; discovery
cost is startup-only, while publication uses one bounded serialized confirm gate. No sibling repeats RabbitMQ
topology/HMAC mechanics. Specificity is therefore adapter. Disposition: `keep` the package; absorb nothing into Core or
Communication; rebuild no R07 mechanism; delete only obsolete configuration/public branches. The one target owner is
the RabbitMQ connector because discovery, orchestration, health, options, topology, and AMQP lifetime are one physical
backend decision. Communication is too wide—it must preserve a dependency-light local floor and provider-neutral
semantics. A narrower package is wrong because none of these mechanics has independent reference intent or version
value.

**Ergonomics:** Human and coding-model code remains one package reference plus `Transport.Send`; explicit endpoints
become the standard .NET connection-string concept instead of a parallel Koan property and three legacy aliases.
IntelliSense retains only options that change trust or flow-control guarantees. Discovery/evaluator implementation
types disappear from application completion lists. Reviewers retain one 398-line adapter chokepoint with one constants
owner and nine direct behavior cells; no new cognitive branch is introduced.

**Constraints satisfied:**

- Entity-first: the application grammar remains `Entity.Transport.Send`; no repository/data surface is involved.
- Controllers-only HTTP: this provider exposes no HTTP route or inline endpoint.
- Constants/options: stable broker/protocol values move to the existing constants owner; tunables remain typed options.
- Shared contracts: provider-neutral adapter/wire/election types remain in Communication; RabbitMQ mechanics remain in
  the connector.
- Structural composition: reference intent and route election still compile once per host; hot publication does not
  rediscover or renegotiate.
- Data/streaming guardrails are not applicable; Communication preserves lazy source enumeration and bounded
  acceptance at its existing pillar boundary.
- Docs: package companions, current Communication reference, product claim/evidence, R11-02, generated truth, and
  progress/handoff will be aligned; dated ADR/R07 evidence remains historical.
- Focused proof only: RabbitMQ connector/election/build/pack/audit/docs evidence; no full R11-07 ratchet.

**Risks:** Removing the public options endpoint and legacy environment/configuration aliases is intentionally breaking
before 1.0. Repository search finds no current source/sample consumer; the real connector tests are the only direct
project consumer and will move to `ConnectionStrings:RabbitMq`. External users of the deprecated Messaging keys must
adopt the documented standard connection string. RabbitMQ container evidence depends on an available Docker-compatible
runtime; if unavailable, the result must be reported as an environmental block rather than replaced by mocks. The
HMAC protects integrity/provenance, not confidentiality; broker TLS/network/vhost controls remain operator-owned.

**Architecture checkpoint:** No RabbitMQ production edit follows this record until the maintainer accepts or adjusts
the `keep` disposition and the narrow promise reduction: one standard explicit endpoint; removal of the public
`ConnectionString` option and legacy aliases; internal discovery/orchestration mechanics; no change to R07 transport,
topology, acceptance, context, health, internal-route, or failure semantics.

## RabbitMQ Communication provider implementation closure

The maintainer accepted the checkpoint. R11-05 implements the terminal `keep` disposition without reopening R07-10
or changing Communication's application grammar:

- `ConnectionStrings:RabbitMq` is the single explicit endpoint. Standard `RABBITMQ_URL`, Aspire discovery, and Koan
  orchestration remain. The duplicate options endpoint, `Koan:Communication:RabbitMq:ConnectionString`, both
  `Koan:Messaging:*` legacy keys, and `Koan_RABBITMQ_URL` were removed.
- `RabbitMqCommunicationOptions` now exposes only earned operator decisions: discovered/provisioned credentials,
  explicit mesh trust, bounded prefetch, and confirmed-publication timeout.
- Discovery and orchestration evaluators are internal connector mechanics. Broker image/tag, ports, recovery and
  health timing, health command, environment identifiers, and wire/topology generations have one existing constants
  owner. Module reporting names candidate lanes rather than falsely fixing them to `default`.
- The passed direct-reference election, named channels, confirmed mandatory persistent publication, durable group
  queues, ephemeral node queues, manual acknowledgement, authenticated context ingress, selection-aware health,
  internal framework routes, no remote settlement, and fail-closed reach remain unchanged.
- Package and current Communication docs teach one package reference, unchanged `Entity.Transport.Send`, the standard
  .NET connection string only when an existing broker requires it, the exact acceptance boundary, and deliberate
  non-claims. Historical ADR/R07 pages remain historical evidence rather than current configuration authority.

**Focused evidence:**

- Release connector build: zero warnings and zero errors.
- Real RabbitMQ connector suite: 9/9, including direct and named-channel election, group/node fan-out, authenticated
  Tenancy context, no-route correction, orchestration, participation health, and topology identity.
- Provider-neutral `ProviderElectionSpec`: 8/8. Both test projects retain the already recorded PMC-032 stale
  `Koan.Core.Adapters` project-reference warning; test discovery and execution are green.
- Release pack contains the package-owned README, canonical icon, one net10.0 DLL/XML pair, build-transitive props,
  and symbol package. Its exact dependencies are Communication, Core, Microsoft Extensions configuration/DI/logging/
  options, and RabbitMQ Client; no new contract or functional dependency was introduced.
- The current direct/transitive vulnerability audit reports no known vulnerable package.
- Generated truth is 102 evaluated packages: 6 repair-required, 17 review-required, 79 structurally ready, and 26
  claims. RabbitMQ is structurally ready with no findings, and the verified Communication claim points to current
  Communication/RabbitMQ docs plus the focused owner/provider suites.

R11-02 now records the implemented `keep` disposition. No full solution/release ratchet, publication, remote mutation,
or unrelated family suite ran; those boundaries remain R11-07 work.

## Acceptance

1. every active package receives a terminal R11-02 disposition before prose graduation;
2. no contracts package depends on a functional engine or projection solely for borrowed vocabulary;
3. every survivor has package-specific identity, install/reference expression, meaningful result, and boundaries;
4. focused behavior and artifact proof is proportional to its role;
5. generated quality and product-surface truth agree after every family slice;
6. the full release ratchet remains a single R11-07 boundary.
