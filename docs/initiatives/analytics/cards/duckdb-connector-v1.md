# ANL-1 · DuckDB connector v1 — SQLite-parity on the relational plane

> **Tier**: T3 · **Depends on**: ANL-0 (go verdict) · **Normative decision**: [DATA-0123](../../../decisions/DATA-0123-embedded-analytics-and-duckdb-connector.md)
> Self-contained session prompt — paste into a fresh session. **Template of record:**
> `src/Connectors/Data/Sqlite/` — this connector is the SQLite connector's sibling, not a new
> design. Update [../README.md](../README.md) when done.

---

## Why this exists

DATA-0123 phase 1. The relational plane already owns schema orchestration, naming particles,
segmentation, filter pushdown, isolation modes, and the AODB conformance suite
([DATA-0120](../../../decisions/DATA-0120-one-relational-repository-four-drivers.md)). A DuckDB
connector implements the same repository stack and rides all of it. The June survey's cost
estimate stands: ~2k LOC implementation + ~3.6k LOC conformance tests — module-scale, fully
paved.

## Mission

Create `src/Connectors/Data/DuckDb/` (package `Sylin.Koan.Data.Connector.DuckDb`) mirroring the
SQLite connector's structure file-for-file:

### Surface (mirror `src/Connectors/Data/Sqlite/`)

- `DuckDbAdapterFactory` / `DuckDbConnectionFactory` / `DuckDbConnections` — including the
  content-root anchoring of relative paths (`SqliteConnections.AnchorDataSource`) so unrelated
  applications never share one store file by accident; **one writer per file is enforced by
  composition**: document and enforce that a source resolves to exactly one read-write owner
  per process.
- `Runtime/DuckDbRepository` — the ten-interface stack (`IDataRepository`, `IQueryRepository`,
  `IRawQueryRepository`, `IBoundedQueryRepository`, `IOptimizedDataRepository`,
  `IConditionalWriteRepository`, `IInstructionExecutor`, `IMutationOutcomeRepository`,
  `IBulkUpsert`, `IBulkDelete`), per-entity `DuckDbEntityPlan` caching.
- `Runtime/DuckDbDialect : ILinqSqlDialect` — Postgres-derivative (verify against probe-4
  results; quote identifiers per DuckDB rules; `RETURNING` for mutation outcomes where probe
  verdict was `works`).
- `Runtime/DuckDbFeatures` / `DuckDbStoreFeatures` — declare everything
  `SqliteFeatures.cs` declares, plus `DataCaps.Query.FastCount`, `OptimizedCount`, and
  `Write.MutationOutcomes` **only if** probe-4 proved them. Nothing aspirational — ARCH-0084
  over-claim cannot stay green.
- **Writes:** per-entity paths as ordinary parameterized SQL; `IBulkUpsert` implemented over
  the **Appender** (probe: the research numbers are ~1M rows/s appender vs ~6–8k/s row-at-a-
  time — the appender path is the promoted one); `Write.AtomicBatch` via real transactions.
- **Schema policy (honest DDL):** create + validate by default; where probe-4 recorded ALTER
  failures, the policy declares repair-by-rebuild explicitly; `AllowProductionDdl` semantics
  preserved from the relational schema policy.
- `DuckDbHealthContributor : DataAdapterHealthContributorBase` — non-creating probes for
  managed-awaiting-provisioning sources (copy the SQLite 503-on-first-boot fix);
  `IDataFailureClassifier` registrations: file-lock conflict → ownership/not-retryable;
  transaction-conflict → retryable ([DATA-0122](../../../decisions/DATA-0122-adapters-classify-failures-the-framework-decides.md)).
- `DuckDbOptions` + setup — first-class: `memory_limit`, `threads`, `temp_directory`,
  extension directory + preload list, `autoinstall: false` default, storage-version pin
  (`STORAGE_VERSION`), read-only attach mode for lanes.
- Initialization module + `KoanAutoRegistrar` per the SQLite module pattern.

### Packaging (DATA-0123)

- `Sylin.Koan.Data.Connector.DuckDb` references **managed** DuckDB.NET bindings only.
- `Sylin.Koan.Data.Connector.DuckDb.Native` rides the per-RID native payload (analogous to
  `DuckDB.NET.Bindings.Full`). The floor stays lean; the analytics pillar's bootstrapper
  explains which package provides the engine when neither is referenced.

### Tests (mirror `tests/Suites/Data/Connector.Sqlite/`)

`Koan.Data.Connector.DuckDb.Tests` with the same spec set: AODB conformance (all three
isolation modes — note Database mode = one file per routed tenant, which is the supported
multi-tenant posture), cold restart, boot provenance, read filter contributor, managed-field
no-leak, plus new specs: **writer-lock** (second process/second host fails classified), **bulk
appender correctness** (order + update-in-place semantics), **extension-off failure** (Parquet
query without preloaded extension fails correctively).

### Docs

Connector `README.md` (zero-config target `.koan/data/Koan.duckdb`; the ATTACH-SQLite demo from
probe 6; single-writer explained as composition, not caveat) and `TECHNICAL.md`. Stamp
dependency floors per the release machinery. Register the connector in the retrieval surfaces
(`docs/reference/capability-map.md` data section + `llms.txt` if the roster is listed there).

## Acceptance evidence

- `dotnet build` green; `Koan.Data.Connector.DuckDb.Tests` fully green including AODB
  conformance cells; no other suite regressed.
- Capability declaration audit: every declared token has a passing conformance cell; every
  undeclared capability fails with a corrective message.
- A package-reference-only sample (bare `AddKoan()`) composes, elects, and completes Entity
  CRUD against `.koan/data/Koan.duckdb`, with startup facts naming the provider and its
  guarantees.

## STOP rule

If the relational plane's shared machinery cannot host DuckDB without a seam change (schema
orchestration, isolation, or failure classification), stop and record the exact seam — the fix
belongs to the plane, not to a DuckDB-local workaround (root-fix rule).
