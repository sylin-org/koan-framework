---
name: data-sql
description: Author this when extending the Koan Data pillar to a relational/SQL backend NOT in the shipped fleet — "new data adapter", "old Oracle", "MariaDB/MySQL/DuckDB/CockroachDB adapter", "connect Koan to a SQL database", "relational store via ADO.NET". Routes intent to the relational adapter authoring procedure.
pillar: data
type: adapter/data/relational-sql
family-base: Koan.Data.Relational
conformance: tests/Suites/Data/AdapterSurface/Koan.Data.AdapterSurface.TestKit/AodbConformanceSpecsBase.cs
blast: high
status: current
last_validated: 2026-06-26
grounded-in:
  - src/Connectors/Data/Postgres/PostgresAdapterFactory.cs
  - src/Connectors/Data/Sqlite/SqliteAdapterFactory.cs
  - src/Connectors/Data/SqlServer/SqlServerAdapterFactory.cs
---

# Adapter Blueprint — Data / SQL (relational)

> **What this is (ARCH-0094).** A per-adapter-TYPE, agent-executable authoring procedure for *extending* the Data
> pillar to a relational/SQL backend Koan does not ship. It is the EXTEND-a-pillar parallel to the `.claude/skills`
> cards (which teach how to USE a pillar). It **scripts the hygiene an agent skips** (discover → research → reuse →
> implement → gotchas → test) and states the **obligations** a conformant adapter must satisfy — it does NOT prescribe
> the optimal/performant code (that is the author's craft). Every obligation carries a machine-checkable citation token
> naming the real shipped member it traces to (the `obligation:` HTML-comment form documented in
> [BLUEPRINTS.md](../../BLUEPRINTS.md)); `scripts/blueprint-lint.ps1` grep-verifies each cited member name is still
> present (in code, not comments) in the cited source — so a renamed/deleted member is caught (type-binding is
> grep-level; AST member-on-type checking is deferred, see ARCH-0094 §Phase-3). The proof of a finished adapter is the **Conformance Gate**
> (`conformance:` above) going green against a real instance — not a code review.

## 1. Trigger

Author a relational adapter when the target speaks SQL over ADO.NET and is NOT one of the shipped relational fleet
(Postgres / SQLite / SQL Server): e.g. *old Oracle*, *MariaDB / MySQL*, *DuckDB (relational mode)*, *CockroachDB*,
*a legacy enterprise SQL store*. **Not** for key-value (→ `data/kv` blueprint) or document (→ `data/document`).

## 2. Discover — reuse before build

1. **Enumerate the shipped fleet** before authoring: glob `src/Connectors/Data/*/` — the relational exemplars are
   Postgres, Sqlite, SqlServer. If your engine speaks an existing wire protocol, prefer configuring an existing adapter:
   factories answer to provider aliases via `CanHandle` <!-- obligation: IAdapterFactory.CanHandle @ src/Koan.Data.Abstractions/IAdapterFactory.cs --> (e.g. Postgres answers `postgres`/`postgresql`/`npgsql`).
2. **Check the NuGet catalogue** for `Sylin.Koan.Data.Connector.{Provider}` (assembly `Koan.Data.Connector.{Provider}`)
   — a community or prior adapter may already exist (Reference = Intent if found).
3. Factories are ranked by `[ProviderPriority(N)]` (Postgres = 14) through the shared `FactoryResolver`; a new adapter
   picks an unused priority. Authoring is the **fallback** only when no reusable adapter exists.

## 3. Research — empirical, least-privilege probe

Connect to a live instance with a **limited-privilege** credential (`[Secret]`-class; never logged) and **confirm
before you announce** (ARCH-0094 "no capability-lies" — announce only what you probed):

- **JSON function support** → sets `IRelationalStoreFeatures.SupportsJsonFunctions` <!-- obligation: IRelationalStoreFeatures.SupportsJsonFunctions @ src/Koan.Data.Relational/Orchestration/IRelationalStoreFeatures.cs --> (Postgres `jsonb` true, SQL Server `JSON_VALUE` true, SQLite false → physical-column fallback).
- **Persisted computed columns** → `SupportsPersistedComputedColumns`; **indexes on computed columns** → `SupportsIndexesOnComputedColumns`.
- **Identifier byte limit** → the `MaxIdentifierBytes` you announce in `StorageNamingCapability` (PG = 63). Probe it; do not guess.
- **Native per-database / per-schema isolation** primitives (for the Container / Database AODB modes) and **transaction/ACID scope** (announce only what is native).

## 4. Resources — REUSE, do not re-implement

Sit on the family base **`Koan.Data.Relational`** and reuse the shared helpers (ARCH-0103 §5.1) — hand-rolling any of
these is a review-fail:

- **`IRelationalSchemaOrchestrator`** / `RelationalSchemaOrchestrator` — drives the 3 materialization policies
  (Json / ComputedProjections / PhysicalColumns). You implement only the engine DDL seam below.
- **`IRelationalDdlExecutor`** — the engine seam you DO implement (7 methods): `TableExists`, `ColumnExists`,
  `CreateTableIdJson`, `CreateTableWithColumns` <!-- obligation: IRelationalDdlExecutor.CreateTableWithColumns @ src/Koan.Data.Relational/Orchestration/IRelationalDdlExecutor.cs -->, `AddComputedColumnFromJson`, `AddPhysicalColumn`, `CreateIndex`.
- **`AdapterConnectionResolver.ResolveRoutedConnection`** <!-- obligation: AdapterConnectionResolver.ResolveRoutedConnection @ src/Koan.Data.Core/AdapterConnectionResolver.cs --> — source-aware connection routing + the `"auto"` sentinel collapse. NEVER hand-roll per-source connection selection.
- **`StorageNameGenerator`** (framework-owned) — you only ANNOUNCE constraints via `StorageNamingCapability` (casing, separator, partition policy, `MaxIdentifierBytes`); the generator folds the partition + routed-source particles into the physical name.
- **`ComparableScalarEncoding.Apply(JsonSerializerSettings)`** <!-- obligation: ComparableScalarEncoding.Apply @ src/Koan.Data.Relational/ComparableScalarEncoding.cs --> — the ONE relational JSON wiring point (all three shipped adapters call it): it installs the shared `ManagedFieldJsonInjector` as the contract resolver (the Shared / RowScoped managed-`__`-discriminator write-stamp) **and** adds the DATA-0100 temporal converters (canonical DateTimeOffset/TimeSpan/DateOnly/TimeOnly so filter and write agree) — one call does both. **Do NOT** hand-call the static `ManagedFieldJsonInjector.InjectManaged`: that is the KeyValue/document family's per-record face (it reads a per-record dictionary, not the ambient `ManagedFieldWriteScope`), so using it in a relational repo re-implements a hook `Apply` installs for free and breaks the cross-scope write guard.
- **`IRelationalStoreFeatures`** — announce your research-confirmed feature flags; the orchestrator branches on them.

## 5. Implement — the obligations contract

Each row is a binding obligation; the cited member is grep-verified by `blueprint-lint.ps1`:

1. **Factory** — implement `IDataAdapterFactory.Create<TEntity,TKey>(IServiceProvider, source="Default")` <!-- obligation: IDataAdapterFactory.Create @ src/Koan.Data.Abstractions/IDataAdapterFactory.cs --> + `string Provider`, `bool CanHandle(string)`, `GetNamingCapability(IServiceProvider)`. Decorate `[ProviderPriority(N)]` (unused N) + `[KoanService(ServiceKind.Database, …)]` for orchestration.
2. **Connection** — resolve via `AdapterConnectionResolver.ResolveRoutedConnection(config, sourceRegistry, providerName, source, baseConnString)`; never key a pool on the `"auto"` sentinel.
3. **Repository** — return an `IDataRepository<TEntity,TKey>` that also implements `IDescribesCapabilities`, `IQueryRepository`, and the bulk/conditional surfaces you announce.
4. **Capabilities** — `IDescribesCapabilities.Describe(ICapabilities)` <!-- obligation: IDescribesCapabilities.Describe @ src/Koan.Core/Capabilities/IDescribesCapabilities.cs --> declares ONLY research-confirmed tokens. The AODB isolation tokens — `DataCaps.Isolation.RowScoped` <!-- obligation: DataCaps.Isolation.RowScoped @ src/Koan.Data.Abstractions/Capabilities/DataCaps.cs -->, `DataCaps.Isolation.ContainerScoped` <!-- obligation: DataCaps.Isolation.ContainerScoped @ src/Koan.Data.Abstractions/Capabilities/DataCaps.cs -->, `DataCaps.Isolation.DatabaseScoped` <!-- obligation: DataCaps.Isolation.DatabaseScoped @ src/Koan.Data.Abstractions/Capabilities/DataCaps.cs --> — are each co-defined with a conformance cell; declare a token only if your adapter realizes it (over-claim fails green).
5. **Registration** — ship a `KoanAutoRegistrar` whose `Describe` <!-- obligation: IKoanAutoRegistrar.Describe @ src/Koan.Core/IKoanAutoRegistrar.cs --> reports the module + AODB notes, and whose `Initialize` registers the factory (`AddSingleton<IDataAdapterFactory, …>`), options (`AddKoanOptions`), discovery, health, and `AddRelationalOrchestration()` (Reference = Intent).
6. **DDL seam** — implement `IRelationalDdlExecutor` (the 7 methods) for your engine's dialect.
7. **AODB realization** — Shared via the `ComparableScalarEncoding.Apply`-installed managed-field write-stamp + the read-filter fold + a conflict-aware upsert that carries the discriminator in its WHERE; Container via the partition particle in the physical table name (the framework-owned `StorageNameGenerator` folds it — and any registered separate-container axis particle — from your announced `StorageNamingCapability`; you never hand-build the name); Database via `ResolveRoutedConnection` + the Database-mode route → a per-source connection.
8. **Fail-closed** — DDL policy gates; cross-scope write rejection; routing to an unconfigured source throws a self-explaining error (never a silent fallthrough).

## 6. Gotchas — real, source-observed

- **Identifier byte limit per engine** — announce `MaxIdentifierBytes`; the generator hashes a composed name past the limit so isolation is preserved (PG 63, SQL Server `sysname` 128). Don't emit raw over-limit names.
- **`"auto"` connstring sentinel** must collapse onto the discovery-resolved base for non-Default sources too (ARCH-0103 P5) — `ResolveRoutedConnection` does this; a hand-rolled resolver re-introduces the bug.
- **Comparable encoding** (DATA-0100) — route scalar temporal types through `ComparableScalarEncoding` or filter and write diverge on mixed offsets / day boundaries.
- **No MARS / DDL racing** — avoid interleaving legacy sync command execution on one connection (the SQL Server fix).
- **Byte-identity off-axis** — the shared registries short-circuit on `IsEmpty`; keep that path so a no-axes app is byte-identical (the FC-5 regression guard).
- **JSON-less engines** (SQLite-class) — when `SupportsJsonFunctions` is false, fall back to physical nullable columns; don't fake JSON support.
- **Composite `[Index]` groups** — create best-effort at table-create (`CREATE INDEX IF NOT EXISTS`), JOBS-0008.

## 7. Test — the Conformance Gate (must go green against a real instance)

Subclass `AodbConformanceSpecsBase<YourFixture>` <!-- obligation: AodbConformanceSpecsBase.Declares_all_three_isolation_modes @ tests/Suites/Data/AdapterSurface/Koan.Data.AdapterSurface.TestKit/AodbConformanceSpecsBase.cs --> and supply a fixture mapping two conformance sources to two distinct
physical stores of your engine (relational: a real `CREATE DATABASE` per source). All four cells MUST pass —
`Declares_all_three_isolation_modes`, `Shared_isolation_holds` <!-- obligation: AodbConformanceSpecsBase.Shared_isolation_holds @ tests/Suites/Data/AdapterSurface/Koan.Data.AdapterSurface.TestKit/AodbConformanceSpecsBase.cs -->, `Container_isolation_holds` <!-- obligation: AodbConformanceSpecsBase.Container_isolation_holds @ tests/Suites/Data/AdapterSurface/Koan.Data.AdapterSurface.TestKit/AodbConformanceSpecsBase.cs -->, `Database_isolation_holds` <!-- obligation: AodbConformanceSpecsBase.Database_isolation_holds @ tests/Suites/Data/AdapterSurface/Koan.Data.AdapterSurface.TestKit/AodbConformanceSpecsBase.cs --> (the last includes the
fail-closed-on-unconfigured-source assertion). Also run the shared `ManagedFieldNoLeak` <!-- obligation: ManagedFieldNoLeak.AssertNoLeakAsync @ tests/Suites/Data/AdapterSurface/Koan.Data.AdapterSurface.TestKit/ManagedFieldNoLeak.cs --> and
`FilterConvergence` oracles. **High-blast** (the adapter carries PHI/PII/PCI/Secret — ARCH-0094 §4: blast = the data
classification carried, NOT the pillar; the `blast:` in this blueprint's frontmatter is the *type default ceiling*)
adds the beyond-happy-path suite (contention / soak / chaos / durability) + the static forbidden-pattern lint + an
isolation-line human review. Green against the real instance = shippable.

## 8. See also

- Card: [docs/reference/cards/data.md](../../../docs/reference/cards/data.md)
- ADRs: [ARCH-0094](../../../docs/decisions/ARCH-0094-adapter-forge.md) (the Forge) · [ARCH-0084](../../../docs/decisions/ARCH-0084-unified-capability-model.md) (capability model) · [ARCH-0102](../../../docs/decisions/ARCH-0102-access-overlay-definition-block.md) (AODB) · [ARCH-0103](../../../docs/decisions/ARCH-0103-aodb-adapter-conformance.md) (fleet conformance + shared helpers) · [DATA-0100](../../../docs/decisions/DATA-0100-comparable-encoding-contract.md) (comparable encoding)
