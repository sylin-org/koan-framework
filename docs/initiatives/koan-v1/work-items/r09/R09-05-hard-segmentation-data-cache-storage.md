---
type: SPEC
domain: framework
title: "R09-05 - Compile Hard Segmentation Across Data, Cache, and Storage"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-16
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-16
  status: passed
  scope: Core family plus Data, Cache, Storage, startup, HTTP, and MCP focused evidence
---

# R09-05 — Compile hard segmentation across Data, Cache, and Storage

- Tranche: `T7A — semantic composition prerequisite`
- Status: `passed`
- Depends on: R09-03 typed contribution lifecycle and R09-04 provider-qualified host decisions
- Unlocks: Communication/Jobs tenant carriage and guarantee-level compilation
- Owner: Core segmentation-family mechanics; Tenancy meaning; Data, Cache, and Storage realization

## Explore gate

**Task:** Replace Data-owned cross-pillar tenancy shortcuts with one host-owned segmentation-family
plan, then make Data, Cache, and Storage compile and directly execute their own hard realization.

**Application intent:** “This application is tenant-aware; ordinary work in tenant A must never read,
mutate, evict, list, or address tenant B's state.”

**Public expression:**

```csharp
builder.Services.AddKoan();

public sealed class Todo : Entity<Todo> { }
```

The complete common-path action is: reference `Koan.Tenancy` plus the desired Data/Cache/Storage
packages, call `AddKoan()`, and write ordinary Entity/cache/storage business code. Development receives
the existing dev-tenant fallback. Production must establish a trusted `ITenantResolver`; explicit
administrative, job, test, or support flows may use `Tenant.Use(id)`. `[HostScoped]` declares a typed
control-plane subject; `Tenant.None()` is explicit host intent only on an untyped pillar operation that
supports it. No contributor, dimension, plan, provider, key prefix,
field, or cache namespace appears in application code.

**Guarantee/correction:** When a concrete tenant is in scope, every applicable covered Data, Cache,
and Storage operation binds the same logical dimension and cannot affect another value. A hard
dimension with no value, an uncovered operation, or a provider incapable of the compiled realization
rejects before physical I/O with one business correction: establish a tenant, mark a genuine
control-plane type `[HostScoped]`, choose a faithful provider, or use an explicit host operation. Koan
never silently drops the dimension. General facts and errors never contain tenant values.

**Complete intent surface:** No common-path action exists beyond the package references, `AddKoan()`,
and normal business code. `Tenant.Use`, `[HostScoped]`, provider/mode configuration, and host-scope
operations exist only when the application is making the corresponding real business/deployment
decision. Container-per-tenant and database-per-tenant placement remain explicit future realizations;
this card claims the existing shared-row Data floor plus key-segmented Cache and Storage.

**Public concepts:**

- `Koan.Tenancy` reference: the application chooses tenant isolation.
- `Tenant.Use(id)`: an explicit non-ingress flow chooses which tenant it represents.
- `[HostScoped]`: an Entity declares that it belongs to the control plane, not any tenant.
- `Tenant.None()`: a loud host intent for untyped pillar operations that support it; never an implicit
  typed-Entity exemption.
- No new public application concept is introduced. Segmentation contracts are framework/module ABI and
  hidden from normal IntelliSense and agent guidance.

### Documentation read

- `docs/architecture/principles.md` establishes Entity-first, Reference = Intent, fail-loud,
  progressive-complexity, and single-canonical-way laws; all are directly relevant.
- `docs/decisions/ARCH-0115-semantic-contribution-compilation.md` requires Tenancy to declare one
  dimension and each pillar to own a provider-qualified immutable realization; it is the controlling decision.
- `docs/decisions/ARCH-0099-tenancy-realignment.md` establishes dev-open/prod-closed posture,
  `[HostScoped]`, the shared-row default, and the mandatory future placement ladder.
- `docs/architecture/tenancy-contributor-purity-assessment.md` proves the existing generic Data seams
  and identifies Cache convergence plus Storage ownership gaps; its static/Data-owned implementation is
  now superseded by ARCH-0115's host-owned plan direction.
- `R09-COALESCENCE-INVENTORY.md` records the current action surface, owner/lifetime costs, exact gaps,
  specificity decision, deletion sequence, and no-leak matrix for this slice.

### Code read

- `TenantAxis` translates tenancy directly into Data's axis DSL; relevant meaning, wrong cross-pillar
  authority, and therefore deleted after the family contribution is live.
- Tenancy's module and `TenantStorageGuard` own posture, current-value correction, Core carriage, and a
  Data-specific guard registration; retain posture/carriage, absorb the hard value contract into its one
  family contribution, and delete the Data guard.
- `DataAxisExpander` has useful typed accumulation and validation but Activator-creates discovered axes
  into process-static registries; rebuild its structural result as a host-owned Data plan.
- `RepositoryFacade` is the correct unavoidable Data boundary and already closes most point/query/write
  paths; keep it, inject its compiled plan, and close or reject instruction/direct gaps.
- `EntityCachePlan` and `CacheClient` are the correct Entity and generic Cache chokepoints, but the former
  reads Data registries while the latter carries scope only as metadata; rebuild one Cache identity plan
  and execute it for key, tag, singleflight, eviction, and coherence operations.
- `ScopedStorageService` is the correct all-verbs Storage boundary; keep it while replacing static
  `StorageKeyScoper`, ambient `AppHost`, Data registry reads, and per-operation guard DI enumeration with
  one injected immutable Storage plan.
- `SemanticCompositionSession` plus the discovery-plan builder is the closest compilation pattern:
  retained active modules contribute once, the concern validates/freezes once, and runtime executes the
  plan directly. Reuse its lifecycle, not discovery's source vocabulary or optional fallback policy.

### Existing and new pieces

Already exists:

- generic `IContributeTo<TTarget>` activation/ordering/freeze lifecycle;
- tenant tri-state ambient meaning (`unset`, concrete tenant, explicit host) and dev fallback;
- stable semantic IDs, decisions/problems, immutable provider receipts, and runtime fact projection;
- Data `RepositoryFacade`, Cache `CacheClient`/`EntityCachePlan`, and Storage
  `ScopedStorageService` chokepoints;
- Data row-isolation capabilities, managed equality filters/stamps, host exemption, and no-leak test kit;
- Core `IdentifierComposer` and canonical Cache/Storage key composition primitives.

Needs to be created or rebuilt:

| New code | Location | Justification |
|---|---|---|
| segmentation value/status, dimension, immutable plan, target, and builder | `src/Koan.Core/Semantics/Segmentation/` | capability-family meaning and host compilation are identical across pillars; Core must not name tenant or any encoding |
| segmentation target scheduling and stable reason/fact IDs | Core semantic composition + `Koan.Core/Infrastructure/Constants.cs` | one always-available empty floor and one canonical evidence vocabulary |
| Tenancy family contribution | retained module under `src/Koan.Tenancy/Initialization/` | Tenancy owns the `tenant` identity, tri-state value accessor, applicability, sensitivity, and correction once |
| host-owned Data scope/entity plan compiler | `src/Koan.Data.Core/Semantics/` | Data owns field/filter/container/database realization and provider capability validation |
| Cache identity plan/renderer | `src/Koan.Cache/Identity/` | Cache owns physical key/tag/eviction/coherence identity for Entity and generic APIs |
| Storage identity plan/renderer | `src/Koan.Storage/Identity/` | Storage owns logical/physical path translation and its all-verbs enforcement boundary |
| focused family and pillar proofs | existing Core, Tenancy, Data, Cache, and Storage test projects | keep behavior evidence beside its decision owner; no release aggregate |

No new options are required for the shared-row/key-prefix result. Existing `TenancyOptions`,
`CacheOptions`, and `StorageOptions` retain their bounded meanings. New stable identifiers belong in
the owning project's `Constants`; no new DTO/request/response is required.

## Coalescence decision

### Closest pattern and specificity

Closest pattern: `ServiceDiscoveryPlanBuilder` scheduled through `SemanticCompositionSession` and
contributed by the retained `ZenGardenModule`.

- Current owners: Tenancy declares a Data axis; Data static registries accidentally supply Cache and
  Storage; Cache scopes are metadata-only; Storage resolves guards from ambient DI on every operation.
- Current lifetime/cost: process-static mutable registration and memos cross host boundaries; Cache
  omits generic physical segmentation; Storage reads `AppHost` and enumerates guards per verb.
- Chosen specificity: Core capability family owns only stable dimension identity, tri-state value
  accessor, applicability, sensitivity, hard/soft posture, and safe correction. Tenancy owns tenant
  meaning. Each pillar owns encoding, supported operation coverage, provider qualification, and failure.
- Disposition: keep the three pillar chokepoints; absorb generic compilation/identity into the family;
  rebuild their plan inputs as host-owned immutable state; delete Data-owned cross-pillar reads and
  process-static/runtime-discovery paths as each consumer moves.
- Wider is wrong: a Core universal partition renderer would erase Data row/container/database, Cache
  key/tag/coherence, and Storage key/container semantics. Narrower is wrong: making Tenancy implement
  three pillar builders recreates the N-pillar fanout and forces changes when a new segmented pillar appears.

### Superseded paths to delete before pass

- `TenantAxis` as the cross-pillar declaration and `TenantStorageGuard`/`IStorageGuard` as Tenancy's
  Data-specific integration path;
- Cache reads of `ManagedFieldRegistry`, `ScopedEntityCacheKey`'s separate tenant fold, and scope metadata
  that does not affect physical identity;
- Storage reads of Data registries, static `StorageKeyScoper`, ambient `AppHost`, and per-operation
  `IStorageGuard` enumeration;
- Data's process-static field/particle/route ownership for migrated segmentation meaning and any
  per-operation contributor enumeration on the covered path;
- unscoped tenant `ExecuteAsync`/Direct paths that can reach physical I/O without the compiled plan;
- facts or docs that infer cross-pillar coverage from Data registration rather than pillar receipts.

Legacy pre-V1 APIs are not preserved as alternate authorities. Temporary bridges may exist only inside
an unfinished red-to-green increment and are removed before this card can pass.

## Ergonomics assessment

- Human readability: application code continues to read as business code; package intent creates the
  guarantee, and `[HostScoped]`/`Tenant.Use` remain explicit only where the business sentence changes.
- IntelliSense: no `.Segmentation`, builder, provider, or plan surface appears on Entity or Cache/Storage
  common paths. Existing `Tenant` vocabulary remains the only explicit application discovery surface.
- Coding-model legibility: one package, one ambient business identity, one canonical correction, and
  one machine-readable coverage matrix remove the need to trace Data registries or infer key prefixes.
- Cognitive branches: default shared-row/key segmentation is automatic; only trusted ingress, deliberate
  host scope, and future physical placement add branches because they change the guarantee.
- Machine delight: plans and facts use stable dimension/pillar/realization IDs without tenant values, so
  an agent can answer “is this path isolated?” without reading implementation code or secrets.

## Red proof and implementation sequence

1. **Family vertical:** prove empty/direct/transitive/removal, duplicate dimension correction, tri-state
   value binding, two-host isolation, sensitive-value exclusion, and zero runtime contributor discovery.
   Ship the Core family target and make the retained Tenancy module contribute `tenant` once.
2. **Data realization:** compile the shared-row field/filter/stamp and provider requirement into a
   host-owned Entity/source plan; preserve point/query/write/no-leak behavior; add batch-delete,
   instruction, and Direct rejection/coverage cells; remove `TenantAxis`, the tenant Data guard, and
   migrated static authorities.
3. **Cache realization:** qualify physical keys, tags, singleflight identities, explicit eviction,
   type/tag flush, and coherence once at `CacheClient`; make Entity caching consume the same renderer;
   prove real hits and A/B isolation on Memory, then focused SQLite/Redis convergence; delete Data reads
   and the separate Entity scope fold.
4. **Storage realization:** inject one immutable plan into `ScopedStorageService`; prove every verb,
   list prefix, presign, transfer, raw/typed/host scope, two-host isolation, and fail-before-I/O; delete
   static scoping and per-operation service discovery.
5. **Evidence/deletion closure:** project one bounded coverage matrix to startup/HTTP/MCP facts, run
   removal/privacy/hot-path sweeps, reconcile current docs/ADRs, and leave no alternate selector or renderer.

## Implementation record

- Core now compiles one immutable segmentation plan per host. Typed scopes are memoized by subject;
  ambient values bind once per operation and never enter plan keys, general facts, or error details.
- Tenancy contributes the `tenant` dimension once through semantic composition. `TenantAxis`,
  `TenantStorageGuard`, and the Tenancy-owned Data/Storage refusal paths are deleted.
- Data owns shared-row fields, stamps, filters, provider capability checks, Direct/instruction refusal,
  and Vector realization. Focused Core, Data, SQLite, Direct, and Vector no-leak proofs are green.
- Cache owns physical key, tag, singleflight, eviction, flush, and coherence identity for both Entity
  and generic APIs. The Data-registry renderer is deleted; focused topology and A/B isolation proofs are green.
- Storage owns one injected logical/physical identity plan at `ScopedStorageService`. Static scoping,
  ambient service location, Data registry reads, and guard enumeration are deleted. Typed, raw,
  `[HostScoped]`, explicit-host, media, transfer, list, range, head, delete, and presign paths are covered.
  Physical prefixes no longer leak through returned objects or listings. The Local provider's sharded
  list implementation was corrected so provider mechanics remain invisible above the connector.
- Focused Storage evidence: `StorageTenantIsolationSpec` passes 12/12 against a real Local provider plus
  a capturing presign provider. The dormant legacy Local connector test project is recorded as PMC-027;
  it is not used as evidence for this card.
- No broad release-certification suite has been run. The bounded evidence/privacy, hot-path, deletion,
  and documentation closure in step 5 is complete.

## Closure record

- The family, Data, Vector, Cache, and Storage runtime paths execute compiled/memoized plans and bind
  ambient values once per operation. The final sweep found no runtime contributor discovery, Storage
  guard enumeration, Data-registry use in Storage, or deleted cross-pillar renderer in production code.
- Data-local managed fields remain a Data concern. Cache excludes an applicable Data-only field until
  its capability explicitly contributes to Core segmentation; the equality-axis no-leak proof prevents
  omission from becoming a cache leak.
- `ISegmentationRealization` is the one value-free evidence seam. Data, Cache, and Storage expose their
  existing execution plan through one stable receipt; one Core reporter projects dimension and
  `enforced-or-rejected` coverage IDs. Duplicate pillar receipts reject instead of overstating support.
- Startup renders the shared receipts under `Guarantees`; HTTP `/.well-known/Koan/facts` and MCP
  `koan://facts` serialize the exact same host envelope. Receipt IDs reject prose-shaped tokens and
  tests prove ambient values are absent.
- Focused closure passed: Core evidence 4/4, startup render 1/1, real host receipt 3/3, Storage 12/12,
  Cache topology 17/17, Cache/Tenancy convergence 8/8, Data-only cache refusal 3/3, HTTP 1/1, and MCP
  1/1, in addition to the earlier Core family, Data, Direct, SQLite, Vector, and no-leak cells recorded
  above. Directly changed Core/Cache/Storage projects build cleanly.
- `git diff --check` reports no whitespace errors; only repository line-ending notices remain. The
  dormant Local connector test project is honestly deferred as PMC-027; repaired listing is exercised
  through the active real-provider Storage proof.
- No broad certification, package publication, commit, push, tag, release, or remote mutation occurred.

## Next safe action

Assess Communication and Jobs together as the next hard-overlay slice. Separate authenticated logical
context restoration from route/binding segmentation, physical topology isolation, confidentiality,
and job-ledger/work-item ownership before changing production code. Do not infer those guarantees from
the three state-pillar receipts closed here.

## Focused verification matrix

| Cell | Required result |
|---|---|
| Activation | direct Tenancy reference contributes exactly one dimension; transitive contracts are inert; removal restores byte-identical baseline |
| Family determinism | duplicate IDs reject before pillar effects; order is stable; two hosts do not share dimensions or values |
| Value contract | concrete, missing, and explicit-host states remain distinct; hard missing rejects with safe correction |
| Data | tenant A cannot point/query/batch/delete/clear/execute into B; unsupported raw/Direct paths fail before I/O |
| Data provider | RowScoped-capable provider is accepted; incapable provider rejects without fallback |
| Entity Cache | tenant-qualified Get produces a real second-read hit; explicit eviction and type/tag flush stay within tenant |
| Generic Cache | Get/Set/GetOrAdd/Exists/Touch/Remove/tag/bulk/singleflight/coherence use one qualified identity |
| Cache providers | Memory proof plus focused SQLite and Redis identity convergence |
| Storage | Put/read/range/delete/exists/head/list/transfer/presign use one qualified path; host scope is explicit |
| Economy | known operations do not discover contributors, enumerate DI guards, reflect, negotiate providers, or mutate plans |
| Evidence/privacy | startup/HTTP/MCP agree on dimension/coverage IDs and never contain tenant values, keys, credentials, or raw exceptions |

## Constraints satisfied

- Entity-first Data access remains canonical; repositories and Direct are advanced boundaries only.
- No HTTP endpoint or controller change is introduced.
- Stable identifiers go in project-scoped constants; no magic dimension/fact/reason strings in consumers.
- No placeholder abstraction ships without its first meaningful Data consumer.
- No large-data scan is introduced; scoped queries continue to require provider pushdown.
- Docs and controlling ADRs are updated when ownership changes.
- No broad release-certification suite; each increment runs only its named owner/consumer cells.

## Risks and stop conditions

- Stop if Core must know tenant, field names, cache separators, blob prefixes, Data capabilities, or pillar corrections.
- Stop if Tenancy must reference Cache or Storage, or implement an N-pillar target list.
- Stop if an ambient value becomes a plan-cache key, fact value, or general error detail.
- Stop if a covered operation can silently omit segmentation or if a provider label substitutes for an
  executable capability proof.
- Stop if a temporary static/compatibility bridge remains a second authority at card closure.
- Container/database-per-tenant, heterogeneous placement, migration, and relocation remain unsupported
  here; do not imply those stronger physical guarantees from shared-row/key segmentation.
- Data's static registry migration is the largest structural risk because serializers/translators
  currently consult it. Migrate one executable adapter vertical first, then generalize through the
  shared Data plan rather than adding an ambient service locator.
- Do not run broad certification, publish, push, tag, mutate remotes, or inspect private downstream applications.

## Kickoff record

- Date: 2026-07-16.
- Branch/HEAD baseline: `dev` at `546817ee0d3a`; preserve the intentional R08/R09 working tree.
- Exploration disposition: one equality-segmentation family is earned; non-equality row visibility
  remains Data's separate typed predicate concern and is not forced into this model.
- First production action: add family activation/value/collision/two-host red proofs, then implement the
  Core plan and Tenancy contribution together with the first Data shared-row consumer.
