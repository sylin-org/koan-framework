---
type: ADR
domain: data
title: "DATA-0111 - Verified runtime default Data cutover"
audience: [architects, maintainers, developers, ai-agents]
status: accepted
last_updated: 2026-08-06
framework_version: source-first
validation:
  date_last_tested: 2026-08-06
  status: verified
  scope: real SQLite, MongoDB, and PostgreSQL round trip with durable route continuity
---

# DATA-0111 — Verified runtime default Data cutover

## Context

Applications such as Weave need to begin with a local database, prepare a better configured database through the same
Koan Entity model, and make that database the new default without changing every caller. The desired application
expression is deliberately small:

```csharp
var receipt = await Data.Source("local-sqlite")
    .PromoteToDefault()
    .Run(ct);
```

This is not a mutable `Koan:Data:Sources:Default` setting. Today:

- `DataSourceRegistry` freezes physical source definitions during composition;
- `DataDefaultProviderPlan` selects one immutable default decision;
- repositories are cached by Entity root, key, adapter, and source;
- callers may retain repositories, batches, streams, Direct sessions, or deferred operations;
- ordinary `Copy`, `UpsertMany`, and backup restore execute Entity lifecycle, save-time timestamps, managed-field
  semantics, and cache behavior; and
- Koan has no authoritative catalog of every ordinary Entity root or every historical partition.

A truthful database switch therefore requires a verified cutover boundary. It must keep physical source composition
immutable, copy exact persisted Entity state through a migration-only path, prevent operations from straddling the
cutover, verify the inactive target, and commit one durable active-route pointer.

Clone, mirror, directional mirror, merge, continuous replication, and database-format backup remain a later topology
epic. This decision establishes only the minimum architecture needed for a safe default-route switch.

## Application intent

While one Koan host remains available for reads, copy every in-scope Entity root from its captured unqualified default
route into one configured inactive source, verify exact logical readback, then durably and atomically make that source
the default. Any failure before the commit point leaves the active route unchanged and never deletes the source.

## Public expression

The dangerous transition belongs to the optional `Koan.Data.Cutover` capability. Core must always retain and enforce
the active route so removing the cutover package after activation cannot resurrect the configured initial default.
The existing source-first facade names the target without restating its adapter machinery:

```csharp
var target = Data.Source("local-sqlite")
    .PromoteToDefault();

DefaultRouteTransitionPlan plan = await target.Plan(ct); // observational and non-mutating
DefaultRouteTransitionReceipt receipt = await target.Run(ct);
```

`Plan` is optional for code but essential for an operator UI. `Run` repeats all preflight checks; a prior plan is never
an authorization token or reservation.

The configured target source already owns its exact adapter, connector, connection, and policy decision. The common
path therefore has no generic adapter assertion or duplicate provider-selection branch.

This slice does not add a string-provider overload, `.From(...)`, `.UsingSource(...)`, merge policy, rollback policy,
clone, or mirror vocabulary.

## Guarantee and correction boundary

For the first graduated implementation, `Run` guarantees:

1. The current and target routes are already composed immutable sources and are physically distinct.
2. Every compiled Entity root whose routing origin is the unqualified default is either included or causes preflight
   rejection. The operation never silently skips an unsupported default-routed root.
3. The target is dedicated, inactive, `Managed`, `ReadWrite`, and empty before Koan first mutates it.
4. New host-mediated writes to the source and target are refused during the cutover window, and already-admitted writes
   drain before copying begins. Reads remain available.
5. Copy bypasses application lifecycle, save-time timestamps, operation overrides, and Entity cache behavior.
6. Source copy, stable source reread, and identity-matched target readback agree on Entity identities, runtime
   Entity-family types, canonical logical values, count, and SHA-256 source-stability digest before activation.
7. The durable route record is compare-and-set before the in-memory route is published. A persistence failure leaves
   the old route active.
8. The old route and its data remain intact. Failed or cancelled target writes are quarantined and never activated.
9. Operations admitted before the cutover may complete against the old route. Operations newly admitted after commit
   use the new route. A retained stale handle fails with a correction to reacquire it; it never silently uses the old
   route.

The first eligibility envelope is intentionally narrow:

- one process and one Koan host;
- the application is the sole writer, or has externally quiesced every non-Koan writer;
- all candidate sources are configured before host composition;
- the graduated provider matrix is SQLite, MongoDB, and PostgreSQL, including transitions between unlike providers;
- only source-generated, concrete Entity roots with provider-stable string IDs;
- only the unpartitioned base slice;
- no Database or Container axis, segmentation, managed fields, operation override, non-default read filter, explicit
  compatibility mapping, or stored-field transform on an included root;
- source and target expose complete physical-container inspection and provider-bounded ordered paging; and
- both physical databases are dedicated to the compiled Entity roots, with no unexplained user containers.

Every limitation is a named preflight rejection with a correction. It is not an eventual-consistency warning.

The guarantee covers Koan Entity operations, unqualified and configured Direct operations, and internal cutover work
admitted through this host. Code that resolves an adapter factory and calls a raw repository directly, opaque external
database clients, and another process are outside the fence.

Multi-step application workflows are not made transactional by the cutover. Applications with file watchers,
background jobs, or read/compute/write workflows should pause their top-level admission before calling `Run` and
resume it after the receipt. Weave can do this in its service orchestration. A generic participant protocol can be
earned later; it is not required to make the Data-operation guarantee honest.

## Routing model

### Immutable physical plans, mutable operational fact

`DataSourceRegistry`, `DataProviderCatalog`, and every `DataSourcePlan` remain immutable. Switching never registers,
removes, or rewrites a named source.

Add one host-owned active-route authority above the initial plan:

```text
DataDefaultRouteSnapshot
  source
  canonical provider
  physical RouteIdentity and ConnectionIdentity
  monotonic authority revision
  per-physical-route content generation
  activation time
  selection receipt
```

`DataDefaultProviderPlan` remains the initial election and compatibility fact. `AdapterResolver.ResolveDefault` reads
the current authority snapshot. Existing precedence remains unchanged:

1. explicit `EntityContext.Source`;
2. Database-axis source;
3. explicit ambient adapter;
4. `[SourceAdapter]` / `[DataAdapter]`; and
5. active unqualified default.

Consequently:

- `Data.Source("Default")` remains an explicit literal configured source;
- `Data.Direct(source: "Default")` remains literal;
- unqualified `Data.Direct()` follows the active logical default; and
- pinned Entity roots are neither copied nor redirected.

The resolution decision must carry its origin and route binding, not only adapter/source strings. A default-derived
repository is bound to the captured authority revision and route generation. An explicit repository is bound to its
physical route and current generation.

### Durable route state

The active pointer must live outside both databases so startup can decide which database to open. The default
single-host store is an atomically replaced, versioned JSON control record under the host content root:

```text
.Koan/data/active-route.json
```

`Koan:Data:Route:StatePath` may override it. Weave should place it with project artifacts, for example
`.weave/control/active-data-route.json`.

The file contains no connection strings or payloads. It records:

- schema version;
- active source, canonical adapter, saved route identity, revision, and activation time;
- a bounded map of route identity to content generation; and
- pending/failed operation ID, expected source revision, target route, and phase.

The in-process switch mutex supplies compare-and-set serialization for the first release. File replacement supplies
crash-safe persistence, not multi-process consensus. Startup recomputes the configured route identity and fails closed
on a corrupt record, missing source, adapter mismatch, or connection-identity drift. It never silently falls back to
the configured initial default.

Before the first target mutation, `Run` persists a pending intent. A crash or failure keeps the old active record and
marks the target quarantined. A later run may reuse that source only after inspection proves it empty again. Koan does
not automatically delete a dirty target.

## Coherent operation admission

The existing `IDataOperationGate` is a source-policy check, not a concurrency gate. Add a Data-owned coordinator keyed
by physical `ConnectionIdentity`, with route identity, authority revision, and content generation carried for diagnostics and stale
handle checks.

Normal mutation admission:

1. validates the repository binding is current;
2. refuses a source whose maintenance barrier is closed;
3. acquires a re-entrant async-flow mutation lease;
4. holds it through provider work and lifecycle completion; and
5. releases it after the complete semantic operation.

Reads validate stale bindings but need not join the mutation drain. An in-flight read may finish on the old route after
commit. A paged stream cannot mix routes: its next page through a stale facade fails.

During `Run`, the coordinator:

1. serializes switch attempts;
2. closes mutation admission for both source and target connection identities;
3. drains existing mutation leases;
4. grants one unforgeable internal migration lease;
5. keeps the barrier closed through durable commit and in-memory publication; and
6. reopens admission on success or safe pre-commit failure.

New writes fail fast with a stable `DataSwitchInProgressException` and retry correction rather than accumulating for
the duration of a potentially long copy.

Integration is required at these multi-operation boundaries:

- every `RepositoryFacade` write, schema/admin operation, and batch save;
- predicate patch/delete helpers that otherwise resolve or use repositories more than once;
- deferred transaction commit, which pins one default snapshot and holds one lease across all Data operations;
- transfer execution when either endpoint is unqualified default;
- unqualified and explicit Direct commands; unknown Direct effect is conservatively mutation-like;
- Direct transactions, whose lease lasts through commit, rollback, or dispose; and
- retained aggregate compatibility repositories and variant batches.

The existing polymorphic variant repository already resolves its root delegate per ordinary operation. Its cached
wrapper does not need a route key. A variant batch captures a root batch; its `Save` must therefore fail as stale if a
cutover occurred after batch creation.

Old provider resources may remain alive until host disposal. Correctness requires stale-handle rejection, not immediate
connection-pool retirement.

## Entity and physical-slice inventory

`AggregateConfigs.GetRegisteredTypes()` and diagnostics contain only types touched so far. They are not a migration
inventory. `EntityTypeCatalog` primarily catalogs family variants and has no root snapshot.

Extend the existing Entity companion generator so every concrete, closed ordinary Entity and family variant registers
with the generated Data Entity catalog. At host composition, compile one immutable `DataEntityRootCatalog`:

```text
root CLR type
key CLR type
stable root identity
closed migration delegate
routing origin
eligibility facts
expected base container
```

Variants collapse through `EntityRootDescriptor` to one physical root. Abstract/open types are ignored. Late dynamic
Entity registration after the catalog freezes is unsupported in this slice.

Preflight suppresses ambient `EntityContext` and trusted carriers before classifying each root. Pinned and
Database-axis roots are reported as outside the default route; an ambiguous origin rejects.

The first release has no authoritative logical partition catalog. It therefore supports only the base unpartitioned
slice and requires complete source inspection. Every non-system source container must correspond exactly to one
compiled base root. An additional partition/container, unexplained Entity container, or unrelated user table rejects
the operation. The target must contain no user container. This deliberately favors a dedicated application database
over an unsafe guess at historical partition names.

Finite partition and Container-axis inventories are a later extension. They must supply logical values from which each
target provider can derive its own physical name; reverse-parsing one provider's storage name is not sufficient.

## Exact copy and verification

The switch orchestrator does not call public `Entity.Copy`, transfer builders, or backup restore.

For each root, composition builds closed delegates that create exact source and target repositories directly from the
captured factories and sources. This bypasses `RepositoryFacade` and repository decorators. Source policy, readiness,
provider receipts, cancellation, bounds, and Entity-family allowlisting still apply.

Because the first release rejects managed fields, segmentation, mappings, and transforms, the raw typed repository is
a sufficient exact migration seam. It preserves supplied IDs, runtime family shape, and stored values while avoiding
identity generation, `[Timestamp(OnSave)]`, application load/save/remove lifecycle, cache, and downstream AI/media
side effects.

The copy algorithm is:

1. Persist pending intent and close/drain the source and target barriers.
2. Re-run source inventory and target emptiness checks.
3. Provision only the empty `Managed + ReadWrite` target.
4. Stream the source in provider-bounded, provider-handled string-ID order.
5. Write bounded raw target batches and require exact affected-count receipts.
6. Independently stream the source again and read the corresponding target records by exact ID in bounded batches.
7. Require equal IDs, runtime family type IDs, canonical logical records, and exact target cardinality.
8. Require the verification source digest to equal the digest observed during copy.

Provider-handled ID ordering is a bounded traversal mechanism, not a cross-provider collation promise. Target
verification therefore cannot compare page positions: SQLite, MongoDB, and PostgreSQL may legally order the same
string identities differently. Exact identity lookup plus equal total cardinality proves that no record is missing or
extra while preserving the bounded-memory guarantee.

`EntityJsonSerialization` remains useful for safe family materialization but its normal JSON text is not declared a
canonical cross-provider hash. Add a canonical writer with deterministic property and dictionary ordering, invariant
scalar/date/binary encoding, explicit nulls, typed ID, root identity, and runtime family identity. Hash length-prefixed
canonical records so concatenation is unambiguous.

Verification is bounded: one source record, one target record, and one write batch are resident at a time. The receipt
contains only per-root counts, digests, durations, and safe identities, never Entity values.

## Commit, cancellation, and failure

The linearization point is durable active-route compare-and-set:

1. Confirm the captured source revision remains current.
2. Confirm every root verified.
3. Atomically persist the new active route and fresh target content generation, clearing pending state.
4. Publish the immutable in-memory snapshot with `Volatile.Write`.
5. Record runtime facts.
6. Release the source/target barrier.

Caller cancellation is honored through verification. Once durable commit begins, Koan completes publication and
barrier release instead of returning an ambiguous cancellation result.

Before commit, cancellation or failure:

- keeps the old route active;
- releases the old route for writes;
- records bounded partial receipts;
- marks `TargetMayContainData = true` once target mutation could have occurred; and
- requires the target to be emptied/reprovisioned before retry.

After durable commit, Koan never automatically flips back. The old database is a retained physical source, not an
automatic rollback replica. A later switch to it must satisfy the same empty-target and verification contract; live
reverse synchronization belongs to the topology epic.

## Cache identity

Entity cache keys must be hard-qualified independently of the user's key template:

```text
data-route:{RouteIdentity}:{ContentEpoch}:{user-formatted-key}
```

The current default template, `{TypeName}:{Partition}:{Id}`, omits source and cannot distinguish a cutover. Ambient
`Source` is also optional and can be omitted by a custom template. Best-effort cache flush is not an adequate
correctness mechanism because entries are not generally enumerable.

Add a compatible route-aware repository-decoration context rather than breaking `IDataRepositoryDecorator`. The Cache
decorator binds the route namespace when constructing `CachedRepository`; `EntityCachePlan` prefixes every formatted
key; and `EntityCacheEvictionCoordinator` captures the effective route binding before deferred enumeration.

The physical route's content generation advances only on successful activation after out-of-band migration. This prevents
a target cached before activation, or a route reused in a later cutover, from exposing stale entries. Old generation entries
expire normally. Explicit access to the retained old source remains isolated and usable.

The Data repository cache key must likewise include route binding and content generation. Otherwise a facade constructed for
an earlier activation of the same source can be reused incorrectly.

## Facts and health

Composition continues to report immutable source availability and the initial election. Runtime facts separately
report:

- active default route and revision;
- selected, rejected, failed, and completed switch operations;
- pending/quarantined target state; and
- bounded per-root verification evidence.

Health must consult the active route authority rather than constructor-fixed `DataDefaultProviderPlan`. Adapter
participation needs a route role so a historical default is not kept critical forever merely because it was previously
observed. Explicit use of that old source may make it an active dependency again.

`AggregateConfig.Provider`, its compatibility repository, relationship diagnostics, and unqualified Direct routing
must resolve one coherent current route instead of retaining the initial default.

## Architecture parts

The implementation has four semantic parts. Persistence formats, compare-and-set, async-flow pinning, leases, generated
delegates, and hashes are mechanisms inside these parts rather than additional architecture:

1. **Default Route Authority** (`Koan.Data.Core`) owns durable active-route truth, route generations, quarantine state,
   startup hydration, and atomic publication.
2. **Data Operation Horizon** (`Koan.Data.Core`) binds a complete logical operation to one route generation, validates
   stale handles, and owns read/write admission plus mutation close-and-drain.
3. **Application Data Manifest** (`Koan.Data.Core`) is the exhaustive immutable host-owned catalog of Entity roots,
   routing classifications, eligibility facts, and closed raw-data delegates.
4. **Verified Route Transition** (`Koan.Data.Cutover`) plans, copies, verifies, and activates a target. Referencing this
   leaf package expresses permission to perform the dangerous operation; removing it never changes active-route truth.

Authority and operation admission remain separate because durable truth and runtime enforcement have different
lifetimes and failure modes. The manifest remains separate from cutover because whole-application Entity truth is also
needed by facts, recovery, and future topology capabilities.

## Construction stages

### Stage A — Route authority and durable startup

- Add the versioned state options/store and active-route snapshot/authority.
- Preserve frozen source/provider catalogs.
- Route unqualified Entity and Direct resolution through the authority.
- Make repository and aggregate compatibility caches binding/epoch aware.
- Prove restart hydration and fail-closed configuration drift.

This stage is an internal construction checkpoint, not the completed feature.

### Stage B — Live admission and cache safety

- Add route mutation leases, barriers, stale-handle exceptions, and route pinning.
- Integrate RepositoryFacade, Direct transactions, deferred transactions, multi-step Data helpers, and transfers.
- Add the mandatory Entity cache namespace through a compatible decoration seam.
- Prove race behavior before building the destructive-capable copy path.

### Stage C — SQLite-first migration and cutover

- Add generated root catalog and exact migration delegates.
- Add plan/preflight, dedicated-source inventory, bounded copy, canonical verification, receipts, and commit.
- Extend SQLite inspection so a missing managed target is distinguishable from corrupt, locked, or inaccessible storage.
- Graduate the public API only when the full failure-injection matrix passes.

### Stage D — Cross-provider graduation

- MongoDB and PostgreSQL: add non-creating storage readiness, require complete container inventory, reject inspector
  `ProviderLimit`, and treat any bulk receipt or logical normalization mismatch as a quarantined partial target.
- Replace positional lockstep verification with bounded identity-matched target reads and exact cardinality so
  provider-specific string collation cannot affect correctness.
- Prove one durable SQLite → MongoDB → PostgreSQL → SQLite round trip, including target-only writes, cold PostgreSQL
  hydration, and final cold SQLite hydration.

### Stage E — Further provider graduation

- JSON: add complete directory inspection and a truthful bounded-snapshot migration capability. Its current 64 MiB
  whole-file limit and lack of provider-bounded paging mean it is not admitted by pretending to satisfy the paging
  contract.

Per-namespace JSON files, removal of the existing file-size ceiling, managed-field migration envelopes, partitions,
and Container axes may follow without changing the public switch expression.

## Code placement

| Concern | Reuse/change | Placement |
|---|---|---|
| Public transition, plan, receipt, failures | New | `src/Koan.Data.Cutover/` as a `DataSource` extension facet |
| Active route snapshot/authority | New | `src/Koan.Data.Core/Routing/DefaultDataRouteAuthority.cs` |
| Atomic control record | New | owned by `DefaultDataRouteAuthority` |
| Route options/constants | New | `src/Koan.Data.Core/Options/DataRouteOptions.cs`, `Infrastructure/Constants.cs` |
| Route origin/binding | Extend | `Routing/AdapterResolutionDecision.cs`, `AdapterResolver.cs` |
| Physical repository cache | Extend | `DataService.cs` |
| Stale guards and operation leases | Extend | `RepositoryFacade.cs`, new `Routing/DataOperationHorizon.cs` |
| Aggregate coherence | Correct | `AggregateConfig.cs`, `AggregateConfigs.cs` |
| Direct route and transaction pinning | Correct | `Direct/DirectSession.cs`, `Direct/DirectTransaction.cs` |
| Deferred transaction pinning | Correct | `Transactions/TransactionCoordinatorFactory.cs`, `TransactionCoordinator.cs`, `TrackedOperations.cs` |
| Multi-step Entity/transfer pinning | Correct | `Data.cs`, `Transfers/EntityTransferBuilderBase.cs` |
| Root discovery | Extend/new | discoverable `IEntity`, `Composition/DataApplicationManifest.cs` |
| Exact migration/canonical verification | New | `Koan.Data.Cutover/Runtime/DefaultRouteTransitionService.cs`, `CanonicalEntityWriter.cs` |
| Decorator route context | Compatible extension | `Decorators/DataRepositoryDecorationContext.cs`, `IDataRouteAwareRepositoryDecorator.cs` |
| Entity cache namespace | Correct | `Koan.Cache/Decorators/*`, `Koan.Cache/Entity/EntityCachePlan.cs`, `EntityCacheEvictionCoordinator.cs` |
| Current-route facts and health | Correct | `DataDiagnostics.cs`, `Composition/DataCompositionContributor.cs`, `Diagnostics/DataAdapterHealthContributorBase.cs` |
| SQLite target inspection | Extend | `Connectors/Data/Sqlite/Runtime/SqliteInspector.cs` and connector tests |
| MongoDB/PostgreSQL target inspection | Extend | their adapter-owned inspectors and connector tests |
| Cross-provider realization | New | `tests/Suites/Data/Cutover/Koan.Data.Cutover.CrossProvider.Tests/` |
| JSON graduation | Later in epic | its factory, inspector, capabilities, and provider suite |

## Graduation proof

The public feature graduated after the Stage A-C implementation and its focused regression matrix passed:

| Area | Required evidence |
|---|---|
| Routing | initial route, A→B revision, restart hydration, corrupt/unknown/drifted state fails closed |
| Precedence | explicit source, explicit adapter, Entity attribute, Database axis, and literal `"Default"` remain pinned |
| Concurrency | no mutation admitted after barrier closure; in-flight mutation drains; concurrent switches serialize |
| Stale use | retained repository, capability facade, aggregate repository, batch, variant batch, and stream page fail safely |
| Direct/deferred | Direct transaction holds its lease; deferred commit cannot straddle a cutover |
| Inventory | ordinary untouched roots discovered; variants deduplicate; unknown source/target containers reject before copy |
| Eligibility | custom key, partition, axis, managed field, mapping, transform, readonly/external/same/nonempty target all reject |
| Exactness | IDs, nulls, timestamps, decimals, dates, binary values, dictionaries, and family runtime types round-trip |
| Suppression | identity/timestamp writers, Entity lifecycle, cache, AI/media hooks, and operation overrides do not run |
| Verification | missing, duplicate, extra, reordered, normalized, or mutated target record rejects before activation |
| Failure | injected failure/cancellation at every phase leaves old route active; partial target is quarantined |
| Cache | same Entity ID across routes/epochs never collides, including custom templates that omit source |
| Observability | receipts/facts/health show current safe identities and never connection strings or payloads |
| SQLite | real file-to-file cutover, missing-target provisioning, cold restart, locked/corrupt target, and target-only writes |
| Cross-provider | real SQLite → MongoDB → PostgreSQL → SQLite round trip, collation-neutral verification, revision continuity, and cold restart |
| Packaging | a consumer compiles the exact terse expression with Cutover, Core, and the selected connector references |

Focused test placement is `tests/Suites/Data/Cutover/Koan.Data.Cutover.Tests/DefaultRouteTransitionSpec.cs`, with
the existing Data Core, SQLite connector, Cache Topology, product-surface, and packaging suites providing the wider
regression boundary.

Validation on 2026-08-06 passed the Cutover suite (5/5), the real cross-provider round trip (1/1), Data Core
(473/473), SQLite (48/48), MongoDB (40/40), PostgreSQL (26/26), Cache Topology (64/64), the CockroachDB shared-Npgsql
build, and product-surface generation (44 claims, 94 packages). The earlier packaging run passed 60 tests and retained
one unrelated existing Weaviate Zen Garden intent failure; its focused rerun reproduced that baseline failure without
touching the Weaviate implementation.

## Coalescence

Absorb:

- the revision and late-result rejection pattern from AI runtime source control;
- provider-bounded ordering and receipt validation from `QueryStreamCoordinator`;
- immutable source plans and exact provider selection from current Data routing;
- the quiesce → copy → verify → atomic cutover sequence from tenancy relocation; and
- stable Entity-family serialization/materialization rules.

Collapse route revision and cache epoch into one route content generation; obtain the mandatory cache namespace from
the current operation horizon rather than adding a route-aware decorator sibling. The source-first expression removes
the generic adapter assertion because the immutable target source plan already owns exact provider selection.

Do not reuse as the execution owner:

- public transfer builders, because they run ordinary Entity save semantics and have no whole-host inventory;
- `Koan.Data.Backup`, because it is one-Entity archive/restore and explicitly declines global recovery;
- `IKeyedLeaseGate`, because it serializes one action but cannot close admission and drain operations; or
- mutable AI source definitions, because Data physical plans remain frozen.

## Consequences

- The application expression stays small while the operational guarantee is explicit and inspectable.
- Weave can keep all physical databases and the route control record in its project artifact folder and restore the
  exact active state with the project.
- Reads can remain available, but the first implementation has write downtime proportional to copy and double-read
  verification. Change capture and short final reconciliation belong to the later topology epic.
- The old source is a recoverable retained artifact, not a live replica. It diverges if explicitly written after
  cutover.
- SQLite, MongoDB, and PostgreSQL form the first graduated local-to-network and document-to-relational matrix. A
  connector joins the envelope only after its non-creating status, complete inventory, bounded paging, exact bulk
  receipts, and logical round-trip behavior are proven; shared interfaces alone are not a compatibility claim.
- The route authority adds a small current-snapshot read to default resolution and a mutation lease to writes. No
  migration, reflection, or target inspection runs on the ordinary hot path.
- Cross-process and cross-node cutover remain unsupported until an external durable coordinator and writer fence are
  designed and proven.

## Related

- [DATA-0077 Entity context source/adapter/partition routing](DATA-0077-entity-context-source-adapter-partition-routing.md)
- [DATA-0079 Entity transfer helpers](DATA-0079-entity-transfer-helpers.md)
- [DATA-0107 Provider-bounded Entity streams](DATA-0107-provider-bounded-entity-streams.md)
- [DATA-0108 Integrity-first Entity backup and recovery](DATA-0108-integrity-first-entity-backup.md)
- [DATA-0109 Adapter-neutral polymorphic Entity roots](DATA-0109-polymorphic-entity-roots.md)
- [DATA-0110 Compact provider-neutral Data adapter language](DATA-0110-compact-data-adapter-language.md)
- [ARCH-0095 Tenancy](ARCH-0095-tenancy.md)
