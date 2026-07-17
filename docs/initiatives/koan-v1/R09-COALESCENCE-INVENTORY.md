---
type: ARCHITECTURE
domain: framework
title: "R09 Coalescence and Decision Inventory"
audience: [architects, maintainers, ai-agents]
status: current
last_updated: 2026-07-16
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-16
  status: accepted
  scope: complete R09-01 application grammar, owner/lifetime/dependency inventory, target architecture, red proofs, and deletion order
---

# R09 coalescence and decision inventory

This is the durable architecture inventory for R09. It records findings and dispositions, not live
work status. [`PROGRESS.md`](PROGRESS.md) remains the sole status ledger.

## Assessment question

For every logical block:

> What is the narrowest owner that can make this decision once for every valid consumer?

The assessment does not assume the closest existing pattern should survive. Each pattern is classified
as `keep`, `absorb`, `rebuild`, `delete`, or `open`, with the highest justified specificity level.

## Application-language baseline

The architecture is judged from the application inward. The benchmark sentence is:

> Use Koan, define a Todo, and expose Todos through HTTP.

The desired conceptual expression is:

```csharp
builder.Services.AddKoan();
public sealed class Todo : Entity<Todo> { }
public sealed class TodoController : EntityController<Todo> { }
```

Those three concepts map to three business decisions. Provider election, module activation, DI,
registries, contribution compilation, facts, and route construction are consequences and do not
belong in normal application language.

The current supported action surface is slightly larger and must be stated honestly:

| Intent | Current complete action surface | Current guarantee | Delight gap or justified concept |
|---|---|---|---|
| Entity-backed application | Web SDK; direct `Sylin.Koan.App` and a Data connector; `AddKoan()`; `Todo : Entity<Todo>` | Entity verbs execute through the elected adapter and the Data facade's identity, lifecycle, guard, and read-scope behavior | Durability is connector-qualified, not implied by `Entity`; a bundle/connector reference is legitimate intent but must be visible in plan evidence |
| Controller REST exposure | Koan Web through the app bundle; concrete `EntityController<Todo>`; controller-level `[Route(...)]` today | Governed CRUD/query endpoints at the declared route | The route is currently mandatory because the base controller has action routes but no controller route; infer a conventional route for the common path and retain `[Route]` as a deliberate URL override |
| Terse REST exposure | Web Extensions reference plus `[RestEntity]` on the Entity | A generated concrete controller and conventional route | It proves route inference is viable, but it is a second grammar and is not the documented controller-first golden path |
| FirstUse result | Source checkout; .NET 10; Web SDK; app, SQLite, and MCP references; `AddKoan()`; `[DataAdapter("sqlite")]`, `[McpEntity]`, `[Access]`, Entity, route, controller; writable `.koan` path | Persisted/queryable approvals plus governed REST, MCP exposure, and facts | This is an honest cumulative proof, not evidence that every smaller API app needs MCP or an adapter attribute |
| Entity Cache | Cache reference; `[Cacheable]`; ordinary Entity verbs; optional `.Cache` facet | Intended Memory-backed read-through plus mutation coherence | Tenant-safe cache hits and bulk/tag eviction are not currently proved |
| Generic Cache | Cache reference; `Cache.WithJson/WithString/WithRecord(key)` and a verb; optional `Cache.BeginScope` | Typed local/layered cache behavior and coherence | `ScopeId`/`Region` are metadata today, not key partitioning; no tenant-isolation guarantee |
| Storage | Storage and provider references; at least one profile/container configuration; optional `[StorageBinding]`; `StorageEntity<T>` verbs | Provider/profile-qualified object storage behavior | There is no built-in zero-configuration provider/profile floor today |
| Tenancy | Tenancy reference; `AddKoan()`; normal Entity code; production ingress also needs Identity.Tenancy or `ITenantResolver`; explicit host/test work may use `Tenant.Use(id)` | Proven Data row isolation, host exemption, point Entity-cache identity/eviction, Storage key prefix, and trusted context restoration on covered paths | Generic Cache, tag/type flush, Data batch/direct escape paths, and stronger Communication isolation levels remain unsupported or unproved |

This table is an architecture input. A hidden reference, route, configuration value, context setup, or
runtime prerequisite counts as application work even when it is absent from the C# snippet. Conversely,
a route override or isolation posture is welcome when it represents a real application choice.

## Current project and activation graph

The relevant project-reference edges are:

```text
Koan.Core -> Koan.Orchestration.Abstractions
Koan.Data.Abstractions -> Koan.Core
Koan.Cache.Abstractions -> (none)
Koan.Data.Core -> Data.Abstractions, Core, Core.Adapters, Cache.Abstractions
Koan.Communication -> Core, Data.Abstractions, Data.Core
Koan.Cache -> Cache.Abstractions, Core, Data.Abstractions, Data.Core, Communication
Koan.Jobs -> Core, Communication, Data.Abstractions, Data.Core
Koan.Storage -> Core, Data.Abstractions, Data.Core
Koan.Tenancy -> Core, Data.Abstractions, Data.Core
Koan.ZenGarden -> Core, ZenGarden.Core
Mongo connector -> Data.Abstractions, Data.Core, Core, Core.Adapters,
                   Orchestration.Abstractions, ZenGarden.Core
RabbitMQ Communication connector -> Communication, Core
```

`Sylin.Koan` is a dependency bundle for Core, Communication, Data, and JSON. `Sylin.Koan.App` adds
Koan Web. The build records exact direct application references, but `AppBootstrapper` currently loads
the full reference closure and executes every discovered initializer. Therefore type/assembly presence
is still activation for module registration even where provider election later distinguishes direct
intent. A capability must not reference an optional pillar implementation merely to obtain a target
contract.

The target graph is:

```text
application/build intent
        |
        v
Core immutable discovery descriptors + direct-reference evidence
        |
        v
host activation catalog ------------------------------+
        |                                              |
        v                                              v
capability-family models                    active pillar target compilers
  (first: segmentation)                     (Data/Cache/Communication/
        |                                    Jobs/Storage/...)
        +----------------------+-----------------------+
                               v
                    immutable host execution plans
                               |
                               v
                    direct operations + projections
```

The graph deliberately separates an inert contract edge from an activating implementation edge.
Where a real third-party typed target is needed, it belongs in an existing inert abstractions project
or a justified pillar-specific contracts project. It does not belong in Core and does not justify a
speculative contracts package before an actual consumer exists.

## Initial owner map

| Current mechanism | Current owner and strength | Coalescence opportunity | Initial disposition |
|---|---|---|---|
| `KoanRegistry`, generated discovery, direct-reference manifest | Core discovers modules/types and distinguishes direct from transitive intent | Feed one semantic catalog and activation compiler without making assembly presence intent | **Keep/absorb** discovery facts; replace downstream reinterpretation |
| `IKoanCompositionContributor` + `KoanCompositionBuilder` | Core provides a common facts/lock projection; contributors are fail-soft and last-write-wins | Make it a projection of canonical plans rather than a runtime authority | **Rebuild/absorb** into semantic-model projection |
| `Capability`/`CapabilitySet` and conformance gates | Core has typed tokens and honest capability checks | Reuse stable identities/evidence while separating requirements from provider realization | **Keep/refine** |
| `DataAxisExpander`, `Axis`, managed fields, AODB | Data has an accumulative typed DSL, smart defaults, collision checks, and isolation preflight | Reuse typed-builder lessons; move generic mechanics to host-owned compiler and stop Data from being an accidental cross-pillar owner | **Rebuild by specificity** |
| Process-static Data registries | Fast memoized lookup after boot | Convert mutable cross-host registration into generated discovery or immutable host plans | **Replace/delete** where host-owned semantics apply |
| `IKoanContextCarrier` + registry | Core has opaque, versioned, trust-qualified capture/restore consumed by Jobs and Communication | Add stable semantic identities/evidence; retain context-specific trust semantics | **Keep/absorb** into catalog; do not call it routing isolation |
| `IDiscoveryCandidateContributor` + service-discovery coordinator | Typed optional candidates, shared priority slots, adapter health probing | First proof for generic contribution lifecycle | **Migrate** to typed compiler; retain discovery policy |
| ZenGarden adapter bindings | Adapter declares inert compatibility; engine activates contribution | Canonical optional-layer archetype | **Keep semantics; rebuild mechanics** |
| Data adapter resolver | Data owns source/entity/provider selection and capability satisfaction | Share candidate mechanics with Communication only where identical | **Open pending decision inventory** |
| `CommunicationRouter` and adapter descriptors | One immutable route/binding plan per host with hard capabilities and deterministic election | Candidate identity, direct intent, qualification, priority, tie-break, and reason may share a kernel with Data | **Keep typed policy; assess shared mechanics** |
| Layered Redis Cache Communication candidate | Declares zero active lanes until Redis owns L2/coherence | Second optional-layer proof independent of ZenGarden | **Keep as corroborating evidence** |
| `EntityCachePlan` | Cache centralizes policy, key template, safety exclusion, and explicit eviction | Make Cache the owner of all cache partition semantics rather than consume Data registries indirectly | **Keep owner; rebuild inputs** |
| Generic Cache APIs | Provider stores raw cache keys; no universal tenant partition | Add typed Cache contribution language and hard Tenancy coverage | **Gap — build through Cache owner** |
| `ScopedStorageService`/key scoping | Storage centralizes public operations but consumes Data axis information | Give Storage its own typed segmentation target using the same semantic dimension | **Rebuild inputs; keep chokepoint** |
| Communication context envelope/ingress | Captures opaque axes and restores only at sufficient ingress trust | Compile logical isolation separately from route/physical/confidential guarantees | **Keep mechanism; strengthen plan/evidence** |
| Runtime fact store and startup/MCP/Web projections | One safe fact envelope already serves several consumers | Project canonical decision identities and rejected candidates; do not add a second scanner | **Keep/extend projection** |
| Lockfile/composition snapshot | Build/runtime intent and some resolved facts are serialized | Define declared-vs-resolved contract and semantic diff without making two authorities | **Open boundary decision** |

## Exact decision, state, and hot-path inventory

| Source/type | Decision currently owned | State/lifetime and cost | Target disposition |
|---|---|---|---|
| `Koan.Core.Hosting.Registry.KoanRegistry` | Which initializer, registrar, background service, discovery adapter, and discoverable implementer types exist | Mutable process-static dictionaries populated by generated module initializers and runtime fallback | **Keep only immutable generated discovery facts**; host activation and plans consume descriptors without mutating process semantic state |
| `RegistrySourceGenerator`, `RegistryManifestLoader`, `Sylin.Koan.Core.targets` | In-tree generated discovery, runtime fallback discovery, direct/module manifests, and trimming roots | **R09-01 baseline:** the generator was injected broadly by repo build props but was a separate package not automatically proved for downstream package consumers; fallback reflected loaded assemblies; the build target rooted every Koan assembly with `preserve="all"` | **Keep/rebuild generated substrate** and package analyzer delivery; emit sorted immutable descriptors/factories; retain fallback only as explicit degraded mode until staged-package/AOT proof, then delete reflection-deep rooting and the Data.AI generator special case |
| `AssemblyCache`, `KoanPillarCatalog`, `ProvenanceRegistry`, `KoanStartupTimeline` | Loaded assemblies, pillar declarations, public provenance, and startup stages | Mutable process-global state; multiple hosts can observe each other's composition/timing | **Rebuild host-owned**; retain a process cache only for immutable reflection/build facts with no host meaning |
| `KoanEnv` | Environment, app identity, orchestration/session/container posture, and global provenance bridge | Process-sticky first-host-wins state | **Split/rebuild** truly process facts from host `ApplicationIdentitySnapshot`/environment; terse static reads resolve current AppHost, compiled consumers capture the host snapshot |
| `AppBootstrapper` | Assembly closure loading, manifest ingestion, initializer construction/order, boot failure policy, and bootstrap snapshots | Runs per `IServiceCollection` but reads/writes global catalogs and instantiates every discovered initializer | **Rebuild as host composition orchestrator**; activation filtering must happen before initializer/contributor instantiation |
| `AddKoan(Action)` + `KoanCompositionScope` | Captures application declarations | Calls complete `AddKoan()` before the callback; declarations therefore cannot be frozen during the first call | **Keep public grammar; rebuild backing state** as a host declaration builder frozen only after the callback/provider composition boundary |
| `KoanModule`, `KoanModuleHost`, initializer/registrar bridge | Module identity, DI registration, start, and reporting | One module may be parameterlessly constructed by bootstrap, constructed again through DI for start, and constructed again by AppRuntime for report | **Keep lifecycle/authoring idea; rebuild from generated static metadata** so one filtered active host instance registers/starts once while the constitution projects activation; keep typed contribution as a separate earned phase and delete the legacy initializer/registrar bridge after migration |
| `KoanCompositionSnapshot.Build` + `KoanCompositionBuilder` | Re-discovers reporting contributors and reconstructs elections/capabilities | After provider build, scans loaded assemblies, parameterlessly instantiates contributors, swallows failures, and applies mutable last-write-wins dictionaries | **Delete as authority**; retain compatibility projection only until runtime facts/lockfile read the canonical model |
| `AppRuntime` | Re-enumerates registrars, mutates provenance, builds composition snapshot, and records facts | Host service with duplicate discovery/instantiation over process-global registries | **Rebuild as renderer** of one frozen host model; no independent decisions |
| `Capability` / `CapabilitySet` | Stable provider capability identity and detail declaration | `CapabilitySet` is mutable, unsorted, non-thread-safe, and duplicate detail writes silently replace; several consumers rebuild sets repeatedly | **Keep `Capability` and fail-loud requirement semantics**; compile declaration builders into immutable, ordered provider views owned by plans |
| `KoanContextCarrierRegistry` | Carrier identities, ordering, trust preflight, capture, restore, and fingerprint | Host singleton; validates/finalizes once; operations execute direct ordered carriers | **Keep/absorb** as the strongest existing model; project its descriptors into the semantic model without conflating context with routing isolation |
| `DataAxisExpander` | Discovers axes, expands their DSL, validates some collisions, and fills Data registries | Activator-creates every discovered axis; writes process-static field-owner ledger and registries at boot | **Rebuild** as a Data-owned typed target through the generic compiler; delete Activator/static registration path |
| `ManagedFieldRegistry` and sibling Data registries | Persisted fields, read filters, routes, operation overrides, cache/storage scope particles | Mutable process-static authority consumed by Data and indirectly by Cache/Storage | **Replace** with host-owned Data plan plus explicit Cache/Storage inputs; delete cross-pillar reads |
| `StorageWritePlan` / contributor registries | Per-entity write transforms and field contributions | Compile-once precedent, but static contributor registry and static type memo | **Keep fold semantics; move ownership/cache to host** |
| `ReadScopeFold` | Composes read predicates and required capabilities | Invokes contributor callbacks per read and consults static registries | **Compile structural chain once**; bind ambient dimension values through precompiled accessors per operation |
| `AdapterResolver`, `FactoryResolver`, `DataService` | Entity/source/provider decision and repository construction | Enumerates instantiated `IDataAdapterFactory` objects during repository resolution; `DataService` later caches by entity/key/adapter/source | **Keep Data policy and runtime route inputs**; absorb generic identity/activation/order/evidence; compile static portion per host/entity/source shape |
| `RepositoryFacade` | One governed Data read/write/batch surface and capability enforcement | Repository instance cached by `DataService`; several correct isolation chokepoints, with known batch/direct escape gaps | **Keep and close** as Data execution chokepoint consuming immutable plans |
| `EntityCachePlan` | Entity cache policy, identity template, exclusions, mutation coherence | Host-owned plan precedent, but derives segmentation through Data registries | **Keep/expand** as Cache owner; consume compiled dimensions directly |
| `ScopedEntityCacheKey` | Entity cache key material | Direct operation helper; currently Data-scope-derived | **Absorb** into Cache identity renderer |
| `CacheClient.ApplyScope`, `CacheScopeAccessor`, stores | Generic cache scope metadata and runtime operations | Every operation sets metadata, but Memory/SQLite/Redis address by raw key; tag/type eviction is globally addressed | **Rebuild identity at Cache chokepoint** or delete misleading scope API; compile key/tag/coherence segmentation once |
| `StorageKeyScoper` / `ScopedStorageService` | Storage key rendering and guard enforcement | Correct public chokepoint, but reads Data registry and enumerates `IStorageGuard` from DI on every operation | **Keep service/renderer; rebuild inputs** as Storage-owned immutable plan and direct guard chain |
| `CommunicationRouter` | Per-host lane/channel provider election, route table, target bindings, selected adapter hosts | Host singleton; compiles immutable dictionaries once; publish hot path is lookup + serialization + adapter call | **Keep typed policy/bindings**; absorb generic candidate mechanics and project plan decisions directly |
| `CommunicationIngress` + wire envelope | Validates envelope and restores context under declared ingress trust | Per delivery, direct binding lookup and precompiled carrier registry | **Keep**; report logical restoration separately from route, physical, confidentiality, and authorization guarantees |
| `ServiceDiscoveryCoordinator` / `IDiscoveryCandidateContributor` | Candidate precedence, normalization, optional contributor fallthrough | Contributors materialized once but invoked sequentially on every discovery request | **Keep policy shape; compile structural contributions once** and leave health probing/runtime discovery where values are truly dynamic |
| Jobs `JobTypeBinding` | Job serializer/handler invocation binding | Reflection/delegates bound once per job type | **Keep/absorb** into job plan |
| Jobs ledger selection | Memory versus Data ledger | First resolution enumerates Data factories and guesses durability from type names; forced DataStore can silently use memory | **Rebuild** against compiled typed Data durability evidence; explicit durable intent fails correctively when unmet |
| `KoanRuntimeFactStore` | Latest safe runtime fact state and deterministic envelope | Host singleton; Web and MCP project the same store | **Keep**; seed it from canonical decisions and reserve runtime `Record` for observations, not reconstruction |
| `KoanLockfile` / comparer | Deterministic build/direct-reference intent and some resolved twin fields | Comparer intentionally tolerates runtime sections missing on one side; that is not a full semantic comparison | **Keep declared build artifact**; serialize/diff resolved canonical model separately and never describe the tolerant lock comparison as semantic diff |

### Current correctness gaps that affect the target

- Generic Cache `ScopeId`/`Region` values do not alter physical identity despite documentation implying
  partitioning. No `BeginScope` behavior matrix currently proves otherwise.
- Entity cache tenant suffixes protect point identities, but default type tags are unsegmented; type/tag
  flush can cross tenant boundaries.
- Tenant point reads currently lower to a scoped Data query and bypass `CachedRepository.Query`. The
  no-leak test is safe, but it does not prove a tenant-qualified cache hit.
- `RepositoryFacade.BatchFacade.Delete(id)`, arbitrary `ExecuteAsync`, and `Data.Direct` do not all pass
  through the same isolation composition. They must be closed or explicitly rejected under a hard
  segmentation requirement.
- Jobs documentation is stronger than its type-name durability heuristic and silent forced-mode fallback.
- Jobs options are not bound/validated from an application configuration section today, and the public
  `JobPersistence.Provider` value is captured but not executed.
- Communication uses `Descriptor.IsBuiltIn` in places as a proxy for target-count/settlement evidence
  already represented by more precise descriptor/acceptance fields. External observable adapters can be
  underreported.
- Current `Send` is not delivery-idempotent: every deliberate call receives a new operation identity and
  may produce another delivery. If “idempotent” is intended to mean that sending a snapshot does not
  mutate/persist the Entity, document that narrower property; deduplicated or exactly-once delivery needs
  an explicit identity/settlement contract and is not currently supported.
- Mongo/ZenGarden discovery paths can log raw resolved URLs or connection strings. Canonical facts and
  errors must use redacted/de-identified evidence, and boot/runtime logs need the same rule.
- Entity facts are currently observation-driven, which can leave the agent Entity catalog empty until a
  runtime path happens to touch a type. Generated Entity discovery must feed the canonical model.
- Runtime fact replacement currently keys structural entries too coarsely (`Code:Subject`), so distinct
  candidate decisions can collapse. Canonical decision IDs must remain distinct while identical repeated
  observations avoid unnecessary full-envelope churn.
- Core module documentation describes an attribute/discovery path that current pillar registration does
  not use. Rewrite it with the descriptor constitution rather than preserving the stale mechanism.
- `Koan.Communication` references `Koan.Data.Core` only for neutral Entity cardinality selection. Move that
  helper to `Koan.Data.Abstractions` and remove the implementation dependency.

## Specificity matrix

| Concern | Framework law | Pillar policy that must remain typed | Adapter responsibility |
|---|---|---|---|
| Contributions | activation, ordering, collision, evidence, freezing | target vocabulary and fold/conflict semantics | none |
| Provider selection | identity, qualification pipeline, stable tie-break, reason | Data source/query/transaction requirements; Communication reach/copy/group/settlement requirements | capabilities, priority metadata, readiness, mechanics |
| Context | identity, provenance ordering, capture/restore orchestration | axis meaning, applicability, trust minimum, ingress/egress chokepoints | establish truthful ingress provenance |
| Segmentation | stable dimension identity and coverage evidence | Data/Cache/Communication/Jobs/Storage realization and failure posture | physical encoding and supported levels |
| Explanation | stable decision/problem identity and safe projection contract | pillar-specific correction and guarantee vocabulary | redacted provider detail and health |

## Data and Communication election comparison

The two pillars share mechanics but not one selection policy:

| Dimension | Data today | Communication today | Kernel boundary |
|---|---|---|---|
| Decision shape | Entity/key plus source and ambient route inputs | Static lane/channel | Compiler supports typed plan keys; pillar owns which inputs are structural versus ambient |
| Compilation point | Repository resolution; static result later memoized by `DataService` | `CommunicationRouter` construction once per host | Host plan cache/freezing is shared; Data retains legitimate lazy structural compilation |
| Intent precedence | Explicit context source, database-axis route, context adapter, Entity attribute, configured Default, then any highest-priority registered factory | Explicit provider, then direct-reference candidates, else built-in/layered floor | Shared evidence model; pillar owns precedence stages |
| Candidate activation | Every DI-registered/type-present factory participates | External adapter is inert unless direct, explicitly pinned, built-in, or layered | Generic activation filter must be available; Data must adopt semantic intent evidence |
| Instantiation | `IEnumerable<IDataAdapterFactory>` materializes factories before choice | `IEnumerable<ICommunicationAdapter>` materializes adapters before choice | Descriptors qualify/filter before runtime implementation construction |
| Eligibility | Default configuration checks `CanHandle`; explicit strings often validate later; operation capabilities are mostly facade-time | Lane capabilities are hard-filtered before selection | Generic qualification result; typed pillar requirements and corrective text |
| Ranking | `[ProviderPriority]`, then factory type name | delivery assurance, `[ProviderPriority]`, then stable provider ID | Shared deterministic selector accepts a pillar-owned ordered score/comparer; no fixed universal rank |
| Duplicates | Provider identity collisions are not rejected; later construction may use `FirstOrDefault` | Duplicate provider IDs fail boot | Core owns stable identity collision law |
| Failure posture | Some explicit/default paths fail well; `FactoryResolver.Resolve(desired)` can fall back to highest ranked and explicit adapter strings may fail late | Direct/explicit intent never silently weakens; no eligible route fails correctively | Generic problem/decision receipt; pillar owns whether absence means fallback or rejection |
| Hot path | Repository lookup then cached facade; ambient Data route can vary by operation | Immutable dictionary lookup | No contributor enumeration, reflection, or negotiation on either hot path |

A shared `SelectProvider()` with fixed rules would erase valid differences. The reusable unit is a
deterministic decision compiler: activated descriptors, stable identities, qualification outcomes,
pillar-supplied ordering, collision/dependency validation, and a canonical decision receipt. Data and
Communication supply different typed targets and policies.

## Prior-art assessment

This assessment uses prior art to pressure-test mechanics, not to turn Koan into a competitor or copy
another framework's application grammar.

| Prior art | Useful lesson | Koan decision |
|---|---|---|
| [.NET Options configuration](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.options.iconfigureoptions-1) | Multiple typed configurators run before post-configurators and compose naturally through DI | **Adapt** the typed staged-contribution idea. **Decline** mutable options and registration order as semantic authority: Koan needs explicit identities, activation, deterministic dependency ordering, collisions, frozen plans, and reasons |
| [ASP.NET Core application-model conventions](https://learn.microsoft.com/en-us/aspnet/core/mvc/controllers/application-model) | An application model can be discovered and modified once at startup; conventions need not run per request | **Adopt** as the model for conventional `EntityController<T>` route inference and Web projection. Keep the framework-wide semantic model independent of MVC |
| [Roslyn incremental generators](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.iincrementalgenerator) | Build pipelines favor immutable inputs/outputs and cannot rely on generator-instance state | **Adopt** immutable generated descriptor tables and incremental discovery. Host configuration/provider health remains runtime input; do not turn build output into the execution plan |
| [ABP modular architecture](https://abp.io/docs/latest/framework/architecture) and [pre-configuration](https://abp.io/docs/latest/framework/fundamentals/options) | Explicit modules, dependency-ordered phases, replacement, and composable infrastructure are proven .NET patterns | **Complement** ABP rather than emulate its module-authoring surface. Koan's application grammar remains Entity/intent-centric; borrow explicit dependency stages and deterministic overrides internally, without asking normal apps to write module lifecycle code |
| [ABP multi-tenancy](https://abp.io/docs/latest/framework/architecture/multi-tenancy/) | Tenant resolution is ordered, current-tenant scope is ambient, and Data/Cache/BLOB/authorization must behave coherently; security-sensitive resolver precedence matters | **Adopt the cross-pillar expectation and ordered trusted ingress. Extend it** with compiled coverage, provider-qualified realization, fail-closed unsupported paths, and separate logical/routing/physical/confidentiality evidence |

ABP demonstrates strategic market compatibility: a team may use its modular/business systems and still
benefit from Koan's compact Entity grammar and compiled, agent-readable infrastructure semantics at a
bounded integration edge. Koan's differentiator is not having more modules; it is turning a small
amount of application intent into one explainable, provider-qualified plan.

## Current Tenancy coverage truth

| Active path | Current proven behavior | What it does not prove | R09 requirement |
|---|---|---|---|
| Data records | `TenantAxis` stamps and filters an equality-managed field; adapter capability gates row isolation | every future Data mode/provider | Preserve no-leak and expose compiled realization |
| Entity cache | Data-managed equality fields extend Entity cache keys and eviction | generic Cache API partitioning | Move ownership to Cache plan without regression |
| Generic Cache | no automatic universal tenant partition | tenant-safe read/write/evict/tag/coherence | Add hard Cache contribution or report unsupported/fail closed |
| Storage | scoped service and Data-derived particles isolate applicable blob keys | all Storage profiles/physical placement levels | Give Storage a typed contribution and coverage proof |
| Jobs | opaque tenant carrier restores before work-item handling under host-trusted provenance | external ledger trust beyond proved boundary | Preserve and report exact trust/coverage |
| Local Communication | host-trusted capture/restore invokes handlers under the sender's tenant | tenant-specific routes or physical separation | Report logical isolation only |
| RabbitMQ Communication | signed/authenticated envelope restores tenant before typed handler | per-tenant queue/binding, confidentiality, authorization | Separate logical, route, physical, and confidential guarantees |
| Web/Identity ingress | tenant resolution and membership policy exist in their owning modules | automatic parity for every projection | Inventory canonical resolution/authority contribution boundary |

## Semantic-honesty gaps already confirmed

1. Explicit Mongo `zen-garden://...` intent currently falls back to autonomous discovery when the
   engine is absent or unresolved. Optional automatic contribution may fall through; explicit intent
   should fail correctively.
2. Generic Cache operations are not automatically tenant-partitioned.
3. Communication context integrity currently proves handler execution under the carried tenant, not
   tenant-specific routing, physical isolation, confidentiality, or authorization.
4. Runtime facts record selected outcomes more reliably than complete rejected-candidate and
   cross-pillar guarantee coverage.
5. `KoanCompositionBuilder` is last-write-wins and fail-soft, suitable for reporting but unsafe as the
   single runtime decision authority.
6. Several mutable static registries preserve expensive work but cross host ownership boundaries.

These are architecture inputs, not public support claims. Each receives a red test and exact owner
before implementation.

## Candidate target model

```text
Core semantic catalog
  - stable identities
  - direct activation evidence
  - bounded-context ownership hooks
  - contributor descriptors

Contribution compiler<TTarget>
  - filters inactive declarations before instantiation
  - validates dependencies and collisions
  - deterministically invokes typed contributors
  - records decisions
  - freezes the target

Pillar plan compiler
  - interprets typed requirements
  - selects provider through typed policy
  - validates hard guarantees
  - emits direct immutable execution plan

Projection services
  - facts / startup / health / MCP / HTTP
  - explain / doctor / semantic diff
  - never recompute the plan
```

## R09-01 target decisions

### Core contracts and placement

The completed saga uses these responsibilities; exact sequencing and names may change only with
evidence recorded on an implementation card:

| Contract | Placement | Responsibility |
|---|---|---|
| `IContributeTo<in TTarget>` | `Koan.Core.Semantics.Contributions` | Module-author contract with one `Contribute(TTarget target)` method; absent from normal application guidance |
| `KoanContributionAttribute` | `Koan.Core.Semantics.Contributions` | Stable contribution ID, owning capability/module ID, and stable before/after dependency IDs; target is inferred from implemented generic contracts |
| `SemanticContributionDescriptor` | `Koan.Core.Semantics` and generated registry output | Immutable type/factory metadata, owner, target, dependencies, and source evidence; inspecting it does not instantiate the contributor |
| `SemanticActivationCatalog` | `Koan.Core.Semantics`, host singleton | Direct reference, bundle/dependency intent, explicit declaration, target activation, and dormant/rejected evidence |
| `SemanticContributionCompiler` | `Koan.Core.Semantics.Contributions`, host singleton/internal | Filter before construction, validate IDs/dependencies/cycles, stable topological order, instantiate active contributors, apply them to the typed target, and emit decisions/problems |
| `SemanticApplicationModel` | `Koan.Core.Semantics`, host singleton/public read-only projection | Canonical declared/active/selected/satisfied model and stable decision identities; not the mutable runtime plan itself |
| `SemanticDecision`, `SemanticProblem`, `SemanticEvidence` | `Koan.Core.Semantics` | One safe reason/correction vocabulary projected to facts, health, startup, errors, lock/diff, Web, and MCP |
| `SemanticPlanCache` | `Koan.Core.Semantics`, internal host singleton | `Lazy`/single-flight immutable plans keyed only by structural shape; never by ambient tenant/region/user values |

R09-02 exploration supplied that sequencing evidence. Module registration is pre-provider, typed
contribution may be post-provider, and a structural cache belongs to its runtime plan owner. Therefore
the first production slice implements only the host constitution and module-activation subset above.
ZenGarden earns the public typed-contribution surface; a legitimate dynamic Data plan earns the cache.
This narrows timing, not the accepted saga ownership model.

The source generator emits immutable descriptors into the existing generated-discovery substrate. The
current `KoanRegistry` may remain a process cache for those immutable descriptors during migration, but
it is not an activation or plan owner. The Data.AI-specific generator special case and whole-assembly
reflection rooting are deletion targets once generic descriptor generation proves package-consumer and
NativeAOT coverage.

Ordering reuses the tested deterministic topological-sort law in `RegistrarOrdering`, generalized over
stable descriptor IDs. It does not retain registrar-specific `Type` reflection as the new kernel.
Duplicate IDs, missing hard dependencies, self-edges, and cycles fail before any contribution effect.
Unconstrained nodes use ordinal stable ID as their tie-break.

### R09-02 implementation update

The generalization is now one Core-owned `StableTopologicalOrder` implementation consumed by both
legacy registrar ordering and semantic activation; the two lifecycle paths no longer carry copies of
Kahn's algorithm. `Sylin.Koan.Core` also owns the registry generator as a transitive build tool, so a
consumer's one package decision supplies descriptor generation and activation-manifest tooling together.
The separate generator project remains a build boundary but is no longer an independently packed
product. A concrete `KoanModule` is enough: its identity derives from standard `PackageId`/`AssemblyName`
metadata, and the runtime binds that single generated identity before registration.

### Capability-family coalescence

Tenancy will not directly reference and implement one builder from every pillar. That literal overload
shape would recreate N distributed integration points, require inert contracts for every pillar, and
force Tenancy to change whenever a new segmented pillar appears.

Instead, cross-pillar semantics compile first into typed capability-family models. The first family is
segmentation:

```text
Tenant capability
  -> contributes dimension `tenant` once
  -> immutable SegmentationPlan
  -> every active segmentable pillar compiles a typed realization
  -> one coverage matrix proves all applicable operations
```

Core owns generic dimension identity, current-value accessor/carrier evidence, sensitivity, hard/soft
posture, applicability, and coverage status because these meanings are identical across pillars. Core
does not own tenant resolution, Data fields, cache keys, queue topology, job loading, or storage paths.
Tenancy owns the `tenant` dimension and correction language. Each pillar owns its realization:

- Data: field/container/database route plus every governed read/write/batch/direct path;
- Cache: key, tag, region, eviction, and coherence identities;
- Communication: logical context, route/binding, physical topology, and confidentiality levels;
- Jobs: capture/restore boundary and ledger visibility;
- Storage: key/container/physical placement; and
- Web/MCP: trusted resolution and authorization at ingress.

This is the default coalescence rule for a capability whose meaning is truly shared. A capability with
pillar-specific meaning still implements `IContributeTo<TPillarTarget>` through an inert pillar
contract. Existing `Data.Abstractions` and `Cache.Abstractions` can host earned public targets. A new
`Storage.Abstractions` or `Communication.Abstractions` project is created only when a real direct typed
consumer cannot be expressed through a capability-family model; it is not required for Tenancy.

### Pillar plan ownership

| Pillar | Target/plan owner | First coalescence action |
|---|---|---|
| Data | internal Data semantic builder and immutable per-host/entity/source plan in Data.Core; externally earned target contracts in Data.Abstractions | absorb axes/write/read/route registries; keep RepositoryFacade; adopt direct-intent/capability evidence; close batch/direct gaps |
| Cache | `EntityCachePlan` expanded into one Cache identity/coherence plan; generic Cache uses the same segmentation renderer | make all keys/tags/evictions/coherence consume compiled dimensions; delete Data registry reads and metadata-only scoping claims |
| Communication | `CommunicationRouter` route/binding plan | preserve typed lane requirements and immutable hot path; replace eager adapter objects with inert descriptors/factories; consume shared decision receipts; move neutral Entity cardinality out of Data.Core |
| Jobs | per-job-type binding + ledger plan | preserve compiled handler/context binding; elect ledger from typed durability evidence and fail closed for forced durable intent |
| Storage | Storage-owned profile/key/guard plan consumed by `ScopedStorageService` | compile guard/key rendering once; remove per-operation DI enumeration and Data registry ownership |
| Discovery/ZenGarden | discovery target remains in Orchestration.Abstractions; ZenGarden implements a Core contribution contract against it | compile structural candidate contributors once; keep runtime health probing; replace engine-specific Mongo binding with a neutral discovery offering contract when migrated |

### Host compilation lifecycle

1. Build/source-generation supplies immutable module, contribution, target, and direct-reference facts.
2. `AddKoan()` registers a host declaration builder; `AddKoan(Action)` adds application declarations to
   that same builder. Nothing freezes before the callback completes.
3. The host activation catalog expands explicit bundle/module dependencies and classifies every
   descriptor as dormant, active, or rejected without constructing its implementation.
4. Each target compiler validates stable IDs and dependency order, then constructs only active
   contributors through the host provider and applies them to an ephemeral typed builder.
5. Capability-family models freeze first; pillar compilers consume those models plus configuration and
   inert provider descriptors, qualify/elect realizations, and freeze direct execution plans.
6. A failed compile discards the ephemeral builder and starts no adapter. Selected adapters start only
   after the complete model is valid.
7. Known shapes compile during host startup. Legitimately dynamic Entity/operation shapes use one
   host-owned single-flight cache entry on first use.
8. Runtime operations bind ambient values through precompiled accessors. They do not enumerate DI,
   reflect, discover contributors, negotiate providers, mutate plans, or use ambient values as plan keys.
9. Facts, startup output, errors, health, MCP/HTTP, lockfile, and semantic diff project the same decision
   IDs. Dynamic health/telemetry remains observational and cannot rewrite the plan silently.

The build detail is now resolved centrally: dependency-only bundles such as `Sylin.Koan.App` publish their
ordinary dependency graph as generated build evidence. Assemblies without a concrete module contribute no
functionality, so contracts need no special activation posture. The first production card must prove source
`ProjectReference`, staged `PackageReference`, bundle, direct connector, transitive contract, and
single-file/AOT-shaped fixtures before module filtering replaces the current closure behavior.

### Alternatives deliberately declined

| Alternative | Why it is not the target |
|---|---|
| Make Tenancy implement Data, Cache, Communication, Jobs, and Storage builders directly | It makes one capability own an N-pillar dependency/update fanout and requires inert public contracts even where the shared meaning is simply segmentation. Use one family dimension plus pillar-owned realization; retain direct typed contribution for genuinely pillar-specific meaning |
| Put every pillar target in Core or one universal contracts bag | It centralizes names, not meaning, and turns Core into a switchboard. Targets remain at their bounded-context owner |
| Create `.Abstractions` for every pillar immediately | A package boundary is justified when it prevents a real activation edge for a real consumer. The family model removes the immediate Tenancy need; speculative projects add public compatibility surface without use evidence |
| Generalize `KoanCompositionBuilder` | Its fail-soft discovery and last-write-wins mutation are reporting behavior, not safe compilation. Build the model from immutable descriptors and typed builders, then project compatibility output temporarily |
| Use `IConfigureOptions<T>`/DI registration order as the engine | It composes conveniently but does not supply semantic activation, stable identity, hard dependency/collision validation, pre-instantiation filtering, canonical reasons, or frozen-plan ownership |
| One fixed provider selector for every pillar | Data and Communication have different target keys, precedence, capability stages, ranking, and failure posture. Share the deterministic decision substrate and keep typed policy |
| Treat loaded assemblies or transitive types as active intent | Current closure behavior creates accidental activation. Use direct/bundle/dependency activation evidence; compatibility presence remains inert |
| Compile one plan per tenant/user/region value | Ambient cardinality is unbounded and values are sensitive. Compile structural accessors once and bind values during execution |

## Red-proof matrix

Existing green tests are evidence for behavior to preserve; red cells name behavior that must fail
before implementation. Test-project paths may be coalesced, but each semantic cell remains visible.

| Area | Existing evidence to preserve | First red proof |
|---|---|---|
| Kernel activation | `KoanApplicationReferenceManifestSpec`, bootstrap manifest/fail-loud specs, `RegistrarOrderingTests` | Inactive owner or inactive target does not invoke the contributor factory/constructor; direct source/package and bundle exports activate; transitive contracts and side-loaded compatibility assemblies remain dormant; missing manifest is corrective on the supported path |
| Generated consumer substrate | embedded-module-manifest and registry fallback integration specs | A staged NuGet consumer receives the generator/build assets automatically and discovers modules, Entities, and contributions without `RegistryManifestLoader`; single-file, trim, and NativeAOT-shaped fixtures preserve the same identities |
| Kernel determinism | registrar ordering cycle/self/invalid-target tests | Duplicate stable IDs, missing hard dependency, self-edge, and cycle fail before effects; input/DI registration order produces byte-equivalent plan and facts |
| Host ownership | `KoanContextHostOwnershipSpec`, runtime-fact host-session tests, AppHost scope tests | Two simultaneous hosts with different environment/configuration compile different declarations/providers without shared activation, `KoanEnv`, plans, pillars, provenance, timing, modules, facts, or memo entries |
| Module constitution | `KoanModuleTests` lifecycle bridge | One active module host instance performs registration/start once while the constitution supplies activation projection; dormant module constructor is never called; removal restores baseline |
| Execution economy | context carrier allocation/direct-path specs and Communication hot-path structure | Repeated known operation performs zero contributor discovery, target mutation, provider election, DI enumeration, or reflection; one single-flight compile per structural key |
| Facts/projections | `KoanRuntimeFactsSpec`, `WellKnownFactsSpec`, `RuntimeFactsResourceSpec` | Successful startup/Web/MCP surfaces serialize the same active/inactive decisions and non-fatal candidate rejections; a boot-blocking semantic rejection emits its bounded problem before public projection. Entity catalog is complete before first Entity operation; plan-frozen, provider-started, collection-complete, and readiness states remain distinct |
| ZenGarden optional layer | `DiscoveryCandidateContributorTests`, automatic Mongo/ZenGarden inert/active/fallthrough cells | Dormant ZenGarden contributor is not constructed; equal-priority candidates order stably/collide correctively; removing engine restores autonomous baseline |
| Explicit discovery intent | current Mongo spec that incorrectly expects unresolved `zen-garden://` fallback | Explicit unresolved/unready ZenGarden selection fails with one corrective problem and never silently uses localhost; only automatic discovery falls through |
| Data provider plan | Data composition/default/priority specs, Axis and pipeline suites | Direct intent participates, duplicate provider identities reject, selected/rejected reasons are canonical, and explicit source/adapter never weakens to an unrelated provider |
| Tenancy/Data | `AssertNoTenantLeakSpec`, managed-field/read-scope/AODB specs | Tenant A cannot read/mutate/delete Tenant B through point, query, batch, `ExecuteAsync`, or Direct surfaces; unsupported raw escape fails closed or is explicitly declared outside the guarantee |
| Tenancy/Entity Cache | cache composition/eviction tests, `CacheEvictKeyConvergenceSpec` | A tenant-qualified Get produces a real cache hit; point eviction and type/tag flush affect only that tenant unless an explicit host-wide operation is chosen |
| Tenancy/generic Cache | Cache topology/cross-engine suites | Every Get/Set/GetOrAdd/remove/tag/bulk/coherence operation derives tenant-qualified identity across Memory, SQLite, and Redis; missing faithful provider realization rejects boot/plan |
| Tenancy/Storage | `StorageTenantIsolationSpec` and scoped service tests | Every applicable verb uses the same tenant plan, multi-host plans do not leak, and unsupported profile/physical requirement fails correctively before I/O |
| Tenancy/Communication | local context/flow tests and RabbitMQ transport/topology specs | Tenant A send/receive and raise/on never dispatch to Tenant B; facts distinguish authenticated logical context from route, physical, confidentiality, and authorization levels; an ineligible provider is rejected |
| Communication plan | provider election, channel routing, transport/event composition/lifetime specs | Duplicate IDs, two direct candidates, unmet hard lane capabilities, and layered candidate construction are deterministic/corrective; external settlement/target observability uses declared evidence, not `IsBuiltIn` |
| Jobs plan | `DurableCarrierSpec`, ledger exemption/distributed/wake/persistence specs | Forced `DataStore` with no durability capability fails; Auto may choose local; configuration binds and validates; context restores before load/handler/settle under the selected plan |
| Security/evidence | facts redaction/bounds specs | Discovery endpoints, credentials, connection strings, tenant values, and high-cardinality data never enter facts, problems, boot logs, or general semantic diff |
| Golden application language | FirstUse/GoldenJourney and Web controller specs | `AddKoan` + Entity + bare `EntityController<T>` exposes a deterministic conventional route; explicit `[Route]` overrides without double exposure; public errors name business intent/correction, not compiler/provider registry vocabulary |

## Dependency-ordered replacement and deletion sequence

Each step leaves one authority for the behavior it migrates. A compatibility renderer may exist only
while it reads the new model; no compatibility writer may make an independent decision.

1. **Freeze the evidence substrate.** Add the red kernel/activation/multi-host fixtures, measure current
   constructor/reflection/DI-enumeration counts, and add bundle activation-export fixtures. No runtime
   behavior changes in this test-only opening move.
2. **Establish one host-constitution vertical.** Generate immutable module descriptors and explicit
   bundle exports; build the host composition session, activation compiler, decision/problem types, one
   constitution, and one retained module runtime. Communication is filtered before construction and uses
   one host instance for registration/start; the constitution projects useful declared/active/inactive
   facts. This is the first production child; it is not a free-standing interface extraction.
3. **Prove optional typed contribution with discovery.** Let ZenGarden introduce the minimal generic
   contribution contract and compiler against a real typed target; preserve dynamic health probing,
   reverse the explicit-URI fallback test, redact evidence, and delete per-request structural contributor
   enumeration and duplicate-ID last-write behavior.
4. **Expand the Core host model only with real pillar plans.** Make startup/facts/resolved projections
   render the constitution and compiled plans; add the structural plan cache with the first legitimate
   dynamic Data shape. Delete migrated AppDomain re-discovery, multiple constructions, composition-
   snapshot authority, and parameterless contributor construction as their consumers move.
5. **Coalesce candidate mechanics without universal policy.** Move stable identity, activation,
   qualification outcome, ordering/collision, and decision receipt into Core; migrate Communication
   first, then Data. Keep their typed rank/precedence/failure policies. Delete `FactoryResolver` and
   router-local generic mechanics only after parity tests pass.
6. **Compile the segmentation family and Data vertical.** Tenancy contributes one `tenant` dimension;
   Data consumes it through host-owned Entity/source plans; RepositoryFacade closes batch/direct gaps.
   Delete `TenantAxis` as cross-pillar authority and process-static Data plan registries as each consumer
   moves.
7. **Move Cache and Storage to their own chokepoints.** Prove real tenant cache hits and segmented
   key/tag/eviction/coherence across engines; compile Storage guard/key plans. Delete Data-registry reads,
   metadata-only scope claims, and per-operation Storage guard enumeration.
8. **Move Communication and Jobs coverage.** Add required guarantee levels to Communication plans,
   preserve local floor/Rabbit topology truth, compile Jobs ledger durability and configuration, and
   delete type-name durability and built-in-proxy assumptions.
9. **Make every remaining projection a renderer.** Composition/lockfile, startup, health, errors,
   Web, MCP, and semantic diff read canonical decision IDs. Delete `IKoanCompositionContributor` and
   `KoanCompositionBuilder` after the last projection no longer recomputes.
10. **Finish package/AOT and extension proof.** Migrate downstream assembly-cache/registry consumers,
    remove runtime scans from the supported path, replace remaining process catalogs with immutable
    generated facts/host snapshots, remove generator special cases/excess reflection rooting, and publish
    a third-party contributor conformance kit.
11. **Close the application-language loop.** Apply Web startup convention for the bare controller path,
    run FirstUse/GoldenJourney clean-room source and staged-package proofs, remove superseded public
    grammar, reconcile documentation claims, and hand the frozen baseline back to R08.

The first production child is step 2. Its meaningful result is that one AddKoan host can report a
deterministic, host-isolated declared/active/inactive semantic catalog from the same descriptor and
activation artifact future plans will consume, while a dormant fixture proves it was never instantiated
and an active fixture proves one module host instance owns its lifecycle.

## R09-01 coalescence dispositions

| Candidate | Disposition | Evidence/constraint |
|---|---|---|
| Generic typed contribution compiler | **Accept** | Optional ZenGarden and hard segmentation use one activation/order/evidence lifecycle while retaining different target failure policy |
| Shared deterministic election kernel | **Accept only the decision substrate; reject a fixed selector** | Data and Communication tables prove identity, activation, qualification receipts, order/collision, and reasons are common; target keys, precedence, ranks, and fallback are not |
| Shared semantic identity/problem model | **Accept** | Composition, Data, Communication, Jobs, facts, and operator/agent corrections all need stable bounded IDs without recomputation |
| Shared context/segmentation dimension identity | **Accept a typed segmentation family; reject universal encoding** | One dimension can require coverage while Data fields, cache identities, routes, jobs, and blob paths remain pillar-owned |
| One monolithic executable host plan | **Reject** | Keep one semantic application model with references to immutable typed pillar plans; a universal mutable plan would erase bounded-context policy and cache-key differences |
| Host-owned immutable typed plans | **Accept** | Context registry and Communication prove direct hot paths; process-static Data/provenance/pillar/environment state fails multi-host ownership |
| Semantic diff | **Accept bounded canonical-model comparison; defer workbench breadth** | Compare stable declared/resolved decisions without a second scanner, telemetry history, secrets, or high-cardinality runtime values |
| One contracts package per pillar | **Reject as default** | Existing inert abstractions host earned targets; capability-family models avoid N dependency fanout; create a new contracts package only for a real external typed consumer |

## R09-06 async-context convergence

- `SegmentationContextPlan` is now the single Core owner that joins applicable hard dimensions to
  opaque context carriers. It memoizes structural obligations per subject; runtime terminals bind and
  capture values without contributor discovery or provider negotiation.
- Communication and Jobs retain typed policy through `CommunicationContextPlan` and `JobsContextPlan`.
  They no longer independently infer whether an opaque bag satisfies the active segmentation family.
- Communication adapter ingress trust is immutable descriptor metadata. Election refuses weak or
  unknown declarations before startup; ingress applies the elected adapter's fixed provenance.
- The in-process floor and RabbitMQ share the same business semantics without sharing physical claims.
  Facts distinguish logical restoration, typed routing, shared topology, confidentiality, settlement,
  shared Jobs ledger, Data-owned work isolation, context-free wake, and at-least-once execution.
- A hard typed operation may ask its owning carrier to encode a deterministic resolved fallback. The
  value is sealed into the operation bag without mutating ambient state; host-scoped operations retain
  the null-bag allocation-light floor.
- Tenancy's module ID now matches canonical source/package intent (`Sylin.Koan.Tenancy`), closing the
  supported-path activation gap rather than relying on degraded assembly discovery.

The next coalescence question is not another context engine. R09-07 must determine which existing
decision receipts and typed failures can supply one useful explanation/correction/change vertical,
while leaving projection formats and pillar-specific failure meaning distinct.

## R09-07 explanation convergence

- `KoanFact` remains the one safe explanation projection, not a runtime decision or health authority.
  Schema 2 adds explicit guarantee meaning so renderers do not parse pillar codes.
- `KoanStartupFactView` compiles one ordered Decisions/Guarantees/Diagnostics/module-failure view from
  the canonical envelope. Web and MCP continue to serialize that same envelope exactly.
- Activation decisions/problems, provider receipts, segmentation realizations, and typed pillar
  failures remain distinct decision authorities. Their facts share projection mechanics without losing
  concern-specific meaning.
- `KoanLockfileComparer` remains the bounded semantic-change substrate over exact resolved models;
  runtime prose/session/timestamp/correlation are deliberately excluded.
- The public guide no longer treats `IKoanCompositionContributor` as a supported extension contract.
  Six framework sources still use its registry/Activator path; R09-08 must migrate all six to one
  generated, host-owned evidence-source lifecycle and delete the old path in the same slice.

## R09-08 module evidence convergence

- The retained generated `KoanModule` is also the evidence source. Registration, typed structural
  contribution, startup, provenance, and composition evidence no longer create parallel identities,
  factories, registries, or instances.
- Runtime evidence is an optional module override over the safe composition builder. It is not another
  `IContributeTo<TTarget>` target: structural contributions still compile/freeze once, while evidence
  projects already-owned runtime plans after the service provider exists.
- Pillar reporting code may remain as ordinary internal static projectors for cohesion, but these are not
  services or lifecycle objects. Core segmentation is projected directly from its host plan.
- Data, Cache, Jobs, and Media move to canonical generated module descriptors; Communication already uses
  the retained path. Only constitution-active modules can report.
- The complete deletion boundary is `IKoanCompositionContributor`, its discoverable registry population,
  `RunContributors`, contributor Activator construction, and all six reporter implementations as objects.
  No compatibility alias or DI enumerable replaces them.

## R09-01 completion evidence

- The application-language table records the complete current action surface, guarantees, and delight
  gaps rather than relying on the conceptual three-line snippet.
- The project/activation graph records exact relevant references and why current transitive bootstrap is
  not semantic activation.
- The decision/state/hot-path inventory classifies Core, Data, Cache, Communication, Jobs, Storage,
  Tenancy, discovery, capabilities, facts, build, and module lifecycle owners.
- The Data/Communication table proves the shared compiler boundary and rejects a universal selector.
- Core contract placement, capability-family coalescence, pillar ownership, host compilation lifecycle,
  structural plan-cache rules, and bundle-export constraint are explicit.
- Structural baselines name every current reflection, Activator, DI enumeration, per-operation callback,
  and process-static path. Numerical constructor/allocation probes belong in the first implementation
  child beside the red fixtures, where they can become executable ratchets.
- The red matrix covers optional ZenGarden, hard Tenancy, package/build activation, multi-host,
  hot-path economy, security, projections, and the golden application grammar.
- The dependency-ordered sequence deletes the replaced owner at every migration boundary.
- The V1.1 neutral operation model, destructive migration, full workbench, and telemetry store remain
  explicitly out of scope.

## Inventory discipline

When a new pattern is found, record:

- user/business intent;
- smallest honest application expression and the guarantee it creates;
- current and target owners;
- consumers and active chokepoints;
- state lifetime and hot-path cost;
- nearest sibling patterns;
- candidate specificity levels;
- what coalesces and what remains typed;
- public grammar before/after, including every added concept and its semantic justification;
- human readability, IntelliSense discovery, and coding-model legibility;
- facts/errors/health consequences;
- evidence and deletion targets; and
- stop conditions.

Do not add a mechanism to this inventory merely because a type name resembles `Contributor`,
`Provider`, `Resolver`, or `Pipeline`; record the actual decision it owns.
