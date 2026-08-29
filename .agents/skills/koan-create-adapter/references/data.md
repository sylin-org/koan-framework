# Data-adapter playbook

Authority: `docs/architecture/data-adapter-development-primer.md` (status current), corrected against
the code as shipped — the primer lags the tree in places, and current code wins. This file is the
distilled, agent-executable form of its "Build a new adapter" route. Where this file and the primer
disagree, check the exemplars, then the code — and fix this file.

Worked exemplars on the shared substrate, newest first: **MySql** (`src/Connectors/Data/MySql/` —
the client-server shape: server engine, container fixture, `Discovery/`), **DuckDb** (`DuckDb/` +
`DuckDb.Native/` — embedded engine, dockerless tests), then **SqlServer**, **Postgres**, **Sqlite**
(AOT floor — raw ADO, no runtime IL emit). **Firebird** (`src/Connectors/Data/Firebird/`) is the
agent-authored proof of this playbook: a JSON-less engine on the same substrate. A document/key/value
adapter (e.g. Mongo, Couchbase) follows the same sequence against the document contracts instead of
the relational substrate.

## The oracle (prove behavior, not intent)

Shared behavioral suites live in the TestKits; a provider's test project supplies a host fixture and
inherits the specs. Record pass counts and any reasoned skip per spec.

| Oracle | Location | What it proves |
|---|---|---|
| Record-plane conformance (the gate) | `tests/Suites/Data/AdapterSurface/Koan.Data.AdapterSurface.TestKit/AodbConformanceSpecsBase.cs` | All three AODB isolation modes realized AND declared (row/container/database — all REQUIRED for record adapters), provider-bounded streaming realized-or-fail-closed, polymorphic entity roots round-trip |
| Filter convergence | same kit, `FilterConvergence` | Every filter in the shared corpus converges with the in-memory oracle through the real adapter |
| Sort oracle | same kit, `SortPushdownConvergence` | Scalar orders converge with the CLR oracle and the receipt claims them; pages are real windows |
| Temporal convergence | same kit, `TemporalConvergence` | DateTimeOffset/TimeSpan/DateOnly/TimeOnly round-trip and order per DATA-0100 |
| Capability truth | exemplar `*CapabilityTruthSpec` | The declared capability set matches what the store can honor — over-claim is the defect this locks out |

### Hosting truth the primer does not tell you

- `FilterConvergence.AssertConvergesAsync` (correctness) is unconditional — host it always. `FilterConvergence.AssertPushesDownAsync` (pushdown guard) **fails on any residual**: the coordinator records a `koan.data.query.fallback` fact whenever the floor finishes an axis. A store with declared limits (e.g. no JSON functions) cannot host the guard over the full corpus; host instead (1) convergence, (2) the guard over a scalar-only query set the store really lowers, and (3) a residual-honesty spec proving a collection filter records the fallback fact — silence is the defect, residual is the declared limit.
- `SortPushdownConvergence`: host `AssertScalarOrderingConvergesAsync` + `AssertPagesAsync` always; `AssertConvergesAsync` (collection-aggregate order) only if the store can compute it; `AssertNothingFallsBackAsync` and `AssertStreamsAsync` only if streaming is announced.
- Managed isolation (the AODB row cell) is fail-closed by design: `RepositoryFacade.RequireScopeForRead` refuses a scoped read whose managed predicate is not fully store-pushed. RowScoped is a REQUIRED token — an adapter cannot skip its way out; the managed predicate must lower in SQL.
- Package versions: `FilterSupport` takes `IReadOnlySet<FilterOperator>`; `ILinqSqlDialect` carries `JsonArrayElementLike` alongside `JsonArrayContains`/`JsonArrayLength`; `IRelationalMappingDialect` adds `Read` + `JsonArrayOrderTerm`. The dialect interfaces live in `Koan.Data.Relational.Abstractions`.

### The mapping reality that decides your dialect

The default relational mapping stores the whole entity as ONE JSON document column — the table is
`(Id, Json)`, and every non-identity filter/sort path resolves through the dialect's nested `Read`
(`Json/Name`, `Json/Level`, …). Every shipped relational dialect lowers those with store JSON
functions (SQLite `json_each`, MySQL `JSON_*`, Postgres jsonb, SQL Server `OPENJSON`). A store
without JSON functions must do what the Firebird adapter does:

1. **Shadow columns** — mirror every top-level scalar document path into a plain column: the DDL
   executor creates it, the insert/update writes it from the same encoded value the document carries
   (`plan.ShadowValues(entity)` in the Firebird exemplar), and the dialect's `Read` answers a
   single-segment path with the quoted column name. Deeper paths refuse by name and `NestedPaths=false`
   is declared.
2. Managed discriminators ride the same mechanism (their predicates MUST push — see above).
3. Collections (`List<T>` properties) stay document-only: their operators are excluded from the
   declared `FilterSupport` and answered by the floor, visibly.

Hosting shape — one file per suite in `tests/Suites/Data/Connector.<Name>/<...>.Tests/Specs/`:

```csharp
public sealed class <Provider>AodbConformanceSpec(<Provider>Fixture fixture, ITestOutputHelper output)
    : AodbConformanceSpecsBase<<Provider>Fixture>(fixture, output)
{
    // The ONLY adapter-specific input: two routed sources on distinct physical stores of the same
    // backend + the fail-closed source the base proves stays unconfigured.
    protected override IEnumerable<KeyValuePair<string, string?>> RoutedSourceSettings() => new Dictionary<string, string?>
    {
        ["Koan:Data:Sources:conformance_a:Adapter"] = "<provider>",
        ["Koan:Data:Sources:conformance_a:ConnectionString"] = A,
        ["Koan:Data:Sources:conformance_b:Adapter"] = "<provider>",
        ["Koan:Data:Sources:conformance_b:ConnectionString"] = B,
    };
}
```

The fixture lives in `src/Koan.Testing.Containers/Fixtures/<Provider>Fixture.cs` (container via
Testcontainers, or dockerless temp-file for embedded engines) and is registered with
`[assembly: AssemblyFixture(typeof(<Provider>Fixture))]`. `RequireBackingStore()` skips only when
infrastructure is genuinely absent — and a skip is inconclusive, never a pass (§10.3).

## Authoring sequence

### Step 0 — Reuse check (stop when an existing connector suffices)

Search `docs/reference/connector-matrix.md`, `docs/reference/product-surface.md`, NuGet, and the repo.
Record family (relational / document / key/value), the intended conformance kind, alternatives checked,
and the concrete gap. Check near-names against the actual provider (Couchbase ≠ CouchDB).

### Step 1 — Pick the family substrate

Relational providers ride `Koan.Data.Relational`. Document/key/value providers first test whether the
existing shared stores express their semantics (InMemory/Json floors; Mongo/Couchbase exemplars).
Record why reuse, alias, or an application-owned SDK integration is insufficient.

### Step 2 — Write the user contract and support profile

State the package reference and the zero-configuration outcome (only if honest), explicit source
configuration, managed vs external storage lifecycle, which Entity operations work, optional earned
claims (inspection, mapping, named reads, streaming, transactions, bulk), and the exact corrective
failure for every unsupported claim. A smaller truthful profile is conformant. The connector ships
**not assessed** — no product claim anywhere.

### Step 3 — Probe the real provider

Official driver docs + a live instance (container or embedded). Capture: identity/key encodings; null,
numeric, temporal, binary, JSON behavior; identifier quoting/case/length/reserved words; filters, sort
order (NULLS placement!), paging, count; transaction and atomic-batch boundaries; parameter-count
limits; generated-identity mechanics; read-only session support; cancellation/timeout/pooling
behavior. Every surprise becomes a focused integration test with a stated consequence.

### Step 4 — Declare only capabilities with a conformance cell

Every capability token is co-defined with an objective test (ARCH-0094). Unverified support stays
unclaimed and rejects correctively.

### Step 5 — Build the smallest provider package

```text
src/Connectors/Data/<Name>/
  <Provider>AdapterFactory.cs        IDataAdapterFactory (+ IDataSourceIntegrationFactory when claimed)
  <Provider>ConnectionFactory.cs     native connection/pool owner (when not substrate-owned)
  <Provider>Options.cs               typed configuration
  <Provider>OptionsSetup.cs          IConfigureOptions<> binding from Koan:Data:<Provider>:*
  Infrastructure/Constants.cs        provider id, priority, parameter/plan bounds
  Initialization/<Provider>Module.cs the ONE KoanModule (assembly's only concrete KoanModule)
  Runtime/<Provider>Route.cs         resolved source route (connection string, options, plan, read lanes)
  Runtime/<Provider>Connections.cs   connection creation keyed by physical source
  Runtime/<Provider>Dialect.cs       IRelationalMappingDialect — the provider's SQL words
  Runtime/<Provider>EntityPlan.cs    compiled mapping + command plans per entity (cached, bounded)
  Runtime/<Provider>Features.cs      capability declaration + store features
  Runtime/<Provider>DdlExecutor.cs   schema ensure/validate executor behind IRelationalSchemaOrchestrator
  Runtime/<Provider>Repository.cs    the Entity repository (see Step 7)
  Runtime/<Provider>Inspector.cs     container listing/resolve/describe/sample (when inspection claimed)
  Runtime/ReadOnly<Provider>Transaction.cs  read-lane enforcement (when read lanes claimed)
  Discovery/<Provider>DiscoveryAdapter.cs   autonomous endpoint discovery (server engines)
  <Provider>HealthContributor.cs     readiness via DataAdapterHealthContributorBase
  README.md / TECHNICAL.md           setup + honest limits / provider contract
```

Package mechanics (csproj, `Sylin.*` id inheritance, `version.json`, one module per assembly) come from
`docs/engineering/adding-a-connector.md`. `[KoanService(ServiceKind.Database, shortCode: ..., ...)]`
supplies discovery/facts metadata; model the field set after the exemplar adapter's factory.

### Step 6 — Compile route and mapping state once

Immutable plans keyed by stable structure (provider, source, entity/key type, partition); warm path
consumes the plan. The warm operation excludes reflection over entity members, DI enumeration, policy
recomputation, capability discovery, and dictionary-to-JSON-to-object materialization. Plans are
bounded (`MaximumPlans`); a declared map under an ambient partition rejects (pinned container cannot
honor a different one).

### Step 7 — Implement the mandatory surface (Entity Persistence)

Implement `IDataRepository<TEntity,TKey>` + `IQueryRepository<TEntity,TKey>`, plus the earned
interfaces the exemplar carries: `IRawQueryRepository`, `IOptimizedDataRepository`,
`IConditionalWriteRepository`, `IInstructionExecutor`, `IDescribesCapabilities`, `IBulkUpsert<TKey>`,
`IBulkDelete<TKey>`. (`IBoundedQueryRepository` + the `ProviderBoundedPaging` token are earned
streaming — an adapter that does not announce streaming fails closed automatically, which the AODB
suite proves.) The substrate consumes — copy the exemplar's wiring, do not reinvent it:

- `RelationalManagedMapping.Compile<TEntity>(source, StorageAddress)` — the compiled mapping plan;
  the default table shape is `(Id, Json)` (see "The mapping reality");
- table-name resolution: `AdapterNaming.GetOrCompute<TEntity, TKey>(services)` (declared maps via
  `IDataMappingPlans.Find<TEntity>(source)`; a declared map under an ambient partition rejects);
- `plan.Commands.Get(id)/Insert(model)/Update(model)/Delete(id)` — `RelationalCommandPlan`
  (identity + value sets; `Insert` omits a generated identity);
- `new SqlFilterTranslator(dialect, mapping, plan.ManagedPath).Translate(filter)` → `(whereSql, parameters)`;
- `IRelationalSchemaOrchestrator.EnsureCreatedAsync/ValidateAsync(mapping, ddl, storeFeatures, policy, ct)`;
- `DataSourceReadinessCoordinator.Provision/ValidateShape` — single-flight readiness;
- `AdapterConnectionResolver.ResolveRoutedConnection(...)` — routed connection strings;
- `AdoCommands` + `SqlParameters` (`Koan.Data.Relational.Ado`) — shared raw-ADO helpers: row
  dictionaries, enum-as-underlying binding, IN-expansion. Use them; every adapter copy of this was
  deleted once already;
- `RelationalEntityPlan<TEntity,TKey,TDialect>` — the plan base: override `Project` (SELECT list
  rendering), `EncodeScalar` (DATA-0100 order-preserving form), and pass the qualifier (a schema,
  a database, or `null`).

Behavior each repository must show (B-01–B-08): identity/value round-trips without culture or JSON
drift; `GetMany` one slot per input in order, `null` for missing; upsert/delete correct outcomes;
`UpsertMany`/`DeleteMany`/`DeleteAll`/`RemoveAll(strategy)` scoped, cancellable, honest counts;
`CreateBatch` incl. deferred `Update(id, mutate)` semantics; query/count over the complete pushable
definition with accurate receipts (`FilterHandled`, `SortHandled`, `PaginationHandled`,
`CountExecution`); unsupported operations reject before unbounded work. Sort: append the identity as
stable tiebreaker; state NULLS placement explicitly per dialect. Remove: `Fast` demands
SchemaOrAdmin; `Optimized` may downgrade honestly.

### Step 8 — Earned capabilities, each with its cell

Source Integration (`DescribeSource`/`CreateSource`), granular inspection (listing, resolve, describe,
sample — separate cells), neutral `RecordSet` materialization, registered reads, provider-enforced
read-only lanes. Earned = declared only with a green cell; otherwise corrective rejection, tested.

### Step 9 — Explain

`Describe()` (pure), `Explain(name)` (pure), `Doctor(ct)` (non-mutating). Facts and logs redact
connection strings, parameters, business values, tenant identifiers; native failures use exact
provider code/type — never message-text classification; timeout ≠ caller cancellation.

### Step 10 — Prove against the real store

Run the oracle table above + provider-specific boundary facts. Exercise all four policy combinations
the provider supports (Managed/ReadWrite, Managed/ReadOnly, External/ReadWrite, External/ReadOnly).
Unavailable infrastructure is reported inconclusive. Exit gate: suites green, unsupported paths fail
correctively, claims match facts and docs.

## Failure modes this family has already paid for

- **Corrective rejection, not silent degradation.** An unsupported filter/sort/isolation rejects
  before provider I/O with a named capability, store, and remedy. Silence is worse than refusal.
- **Probe the provider BEFORE writing code** — every probe finding below was a would-be defect:
  driver/auth negotiation (a client speaking Srp cannot reach an Srp256-only server; the failure
  reads as a login error, not a config error), wire encryption requirements, which env var actually
  sets the admin password, missing TRUNCATE, missing JSON functions, native types the driver refuses
  to bind (FirebirdClient refuses DateTimeOffset — encoded text columns close that).
- **Encoded comparands are the contract (DATA-0100).** Override `EncodeScalar` once with
  `ComparableScalarEncoding.EncodeComparand` so writes and filters agree byte-for-byte; column types
  must match the encoded form (DateTimeOffset → UTC ISO text, TimeSpan → ticks, DateOnly/TimeOnly →
  fixed text). The store's native ordering then equals the CLR ordering — which is what the temporal
  oracle proves.
- **Shadow columns for a JSON-less engine.** The default mapping is a document column; without JSON
  functions nothing inside it is reachable by SQL. Mirror top-level scalars (and managed
  discriminators) into plain columns — created by DDL, written beside the document, read by the
  dialect. Filters, sorts and the row-isolation predicates stay store-enforced.
- **Mixed-space guard.** A declared map pins one physical container and must reject an ambient
  partition; a caller asking for isolation must not silently get none.
- **Stable total order.** The framework appends the identity tiebreaker to paginated reads; the
  adapter's own ORDER BY must state NULLS placement explicitly when the store's default differs from
  NULLS-FIRST-ascending.
- **Parameter-count and batching.** Chunk bulk dispatch by parameter bounds; assert the calculated
  bound so arithmetic drift throws instead of silently exceeding.
- **Transactionally consistent DDL.** Some engines (Firebird) run transactional DDL that deadlocks
  under concurrent first-use — serialize schema work in the adapter behind one gate.
- **The upsert verb differs per store.** `ON DUPLICATE KEY` (MySQL), `ON CONFLICT` (SQLite/Postgres/
  DuckDB), `UPDATE OR INSERT ... MATCHING` (Firebird), `MERGE` (SQL Server). A bare INSERT that
  "works" in the happy path fails the second identical save with a PK violation — the oracle's
  re-save and re-seed cells exist to catch exactly that.
- **NativeAOT.** No `dynamic`, no `Reflection.Emit`, Newtonsoft canonical. `MakeGenericType` sweep on
  every changed generic contract — reflection call sites bypass the compiler.
- **Hydration is store-authoritative.** `ObjectCreationHandling.Replace` at every deserialize site:
  constructor-seeded collections must not accumulate duplicates across save/reload cycles.
- **One build at a time.** The repo builds into one shared output path (not the working tree —
  expect the binaries under the temp root); serialize builds/tests and re-read warnings from the
  build that actually recompiled the changed project.
- **When a test run stalls with zero output, stack the test process** (`dotnet-stack report -p <pid>`).
  A container fixture whose wait strategy waits for an image HEALTHCHECK that does not exist hangs
  forever with no message — wait on the port or a command instead, and run the xunit v3 exe directly
  for live output when `dotnet test` buffers everything.
- **Read receipts before believing claims.** `SortHandled`/`PaginationHandled` receipts are the proof
  pushdown happened; a green suite with silent in-memory fallback proves nothing.
