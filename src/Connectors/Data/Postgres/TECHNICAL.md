---
uid: reference.modules.Koan.data.postgres
title: Koan.Data.Connector.Postgres - Technical Reference
description: PostgreSQL adapter for Koan data.
packages: [Sylin.Koan.Data.Connector.Postgres]
source: src/Connectors/Data/Postgres/
---

## Contract

- The adapter declares `DataCaps.Query.ProviderBoundedPaging` and applies numbered pages in PostgreSQL
  before candidate rows are materialized into application memory.
- JSON pushdown and projection behavior remain capability-dependent.
- Managed aggregates use the native two-column `Id` + `jsonb` path. Explicit `Map<T>` declarations use their physical
  schema/table, scalar names, object roots, and nested JSON paths without routing through the managed table convention.
- `StorageLifecycle.External` disables DDL below the facade. `Access=ReadOnly` is enforced by the compiled source plan
  before mapped or managed writes.
- `Inspect()` lists, resolves, describes, and samples tables/views through neutral storage descriptors.
- The inspector exposes non-creating storage status through an open plus `SELECT 1`, returning redacted status codes for
  capability-qualified cutover planning.
- Registered `.Sql(...)` records/scalars require a declared read lane and execute in a PostgreSQL read-only transaction.
- `QueryRaw` and `CountRaw` take SQL the caller owns, never request text (WEB-0074). They run it as exactly one statement
  (`;` and NUL are refused before the server sees the text) inside a `SET TRANSACTION READ ONLY` transaction, so a
  mistaken raw read can read but cannot write. A predicate can still read anything the database user can.

## Configuration

- Connection options, SSL, timeouts; naming conventions.

### Options

- `ConnectionStrings:Postgres` or `ConnectionStrings:Default`
- `Koan:Data:Postgres:ConnectionString`
- `Koan:Data:Postgres:SearchPath` (default `public`)
- `Koan:Data:Postgres:NamingStyle` and `Separator`
- `Koan:Data:Postgres:DdlPolicy` (`NoDdl`, `Validate`, or `AutoCreate`)
- `Koan:Data:Postgres:SchemaMatchingMode` (`Relaxed` or `Strict`)
- `Koan:Data:Postgres:AllowProductionDdl` (default `false`)
- standard adapter readiness options under `Koan:Data:Postgres:Readiness`

Named routes use the standard `Koan:Data:Sources:<name>` configuration. Secrets belong in normal
.NET secret/configuration providers, not application source.

## LINQ and pushdowns

- Supported expressions follow `Koan.Data.Relational` translator. See: `xref:reference.modules.Koan.data.relational#supported-linq-subset`.
- String contains/starts/ends use `LIKE` or `ILIKE` depending on collation/case needs.
- JSON fields: when mapped to `jsonb`, limited containment queries may be pushed down; otherwise filter in memory.
- Paging uses `OFFSET`/`LIMIT`. Every caller-requested provider-bounded stream sort component must be a
  portable scalar -- an enum, or an integral, decimal, floating, or temporal type -- for its comparison
  to hold on every qualified adapter. Any other key still streams here; Data.Core records a fact naming
  the keys this store, rather than Koan, defines the order of. The one rejection is an Entity-identifier
  sort whose key type is not the provider-stable `string`. Data.Core appends that identifier as an
  opaque tie-breaker, which is not a CLR or cross-provider collation promise.

## Provider-bounded streaming

- `AllStream` and `QueryStream` are coordinated as lazy pages by Data.Core: in identity order each page
  continues after the last identity read (`"Id" > @p`, compared on the native `uuid`, integer or text
  column it is ordered by; DATA-0151), and under a caller's order as numbered pages.
- `batchSize` is the maximum Koan-visible candidate page, not a promise about opaque Npgsql buffers.
- Deep or collection ordering rejects correctively before provider I/O rather than falling back to a
  complete-result sort.
- Offset paging is not snapshot isolation and does not provide mutation-safe traversal, resume tokens,
  or a public cursor.

## Lexical index

An entity's `[Lexical]` properties are served by one stored generated column `__koan_lexical tsvector`
(`to_tsvector('<language>'::regconfig, coalesce(<field>, '') || ' ' || ...)`, each field read the way a filter reads it)
and a GIN index `LX_<table>`, fitted to 63 bytes like every index name
([DATA-0159](../../../../docs/decisions/DATA-0159-lexical-search-over-declared-entity-text.md)). The server computes
the column on every write and for every row the table already holds, so the index is current as soon as it exists and
`Rebuild` answers the row count without re-deriving anything. The column's recipe is recorded as its comment; a changed
declaration drops and re-adds the column and its index. A search or a rebuild first confirms, once per table, that
the catalog holds the column with that recipe and the index; on a route whose DDL policy did not build them it refuses
naming both and the policy correction. A search runs `websearch_to_tsquery` over the OR-joined terms,
filters with `@@`, orders by `ts_rank_cd` and then identity, pages with `LIMIT`/`OFFSET`, and cuts snippets with
`ts_headline` over the same text. CockroachDB shares this runtime and keeps the feature off.

## Index names

PostgreSQL index names are unique per schema and are cut to 63 bytes, and `CREATE INDEX IF NOT EXISTS` skips on any
name match. Koan therefore names every index so a declared index can exist, then confirms it in the catalog
([DATA-0146](../../../../docs/decisions/DATA-0146-postgres-index-names-fit-63-bytes-and-are-qualified-per-partition.md)):

- A derived name (`IX_<table>_<members>`) is used as is while it fits in 63 bytes. A longer one keeps a readable prefix
  and ends in `_` plus an eight-character hash of the whole name, so names that used to be cut alike stay distinct.
- A name you declare with `[Index(Name = ...)]` is used as written on the entity's own table. On a partition table it is
  qualified, `<name>_<eight-character hash of the table name>`, so the declaration creates one index per partition table.
  This is the relational rule SQLite follows too (`RelationalIndexNames`,
  [DATA-0160](../../../../docs/decisions/DATA-0160-sqlite-qualifies-declared-index-names-per-partition-table.md)).
- After each create the catalog is asked what the name holds. A name owned by another table (two entities that declare
  the same name), a non-unique index under a unique declaration or a different key count is refused with the remedy.

An upgrade creates the new names beside the old ones; Koan never drops an index. The old index of a table that gains a
new name is redundant and can be dropped: list a table's indexes with
`SELECT indexname, indexdef FROM pg_indexes WHERE tablename = '<table>'`, then `DROP INDEX "<schema>"."<old name>"`. The
old name of a partition table's declared index is the unqualified declared name, which only the first partition table
ever held. CockroachDB scopes index names to their table and does not cut them, so it keeps the names as declared.

## Unique indexes under tenancy

Under row-scoped tenancy a unique `[Index]` is unique per tenant
([DATA-0150](../../../../docs/decisions/DATA-0150-unique-indexes-are-unique-per-tenant.md)): it leads with
`("Json" #>> '{__koan_tenant}')`, the expression the tenant filter emits, and has its own name (a derived name gains
`__koan_tenant` as its first member, a declared one gains it as a suffix), fitted and verified as above. A duplicate in
one tenant is a `UniqueValueConflictException` that does not name the index; SQLSTATE 23505 is not passed through.

A database built before DATA-0150 keeps its global unique index beside the new one, and Koan logs a warning naming it.
Until it is dropped, a value one tenant holds is still refused for every other. Migrate in three steps: let Koan build
the per-tenant index (start the application and use the entity), confirm it with
`SELECT indexname, indexdef FROM pg_indexes WHERE tablename = '<table>'`, then run the `DROP INDEX "<schema>"."<old>";`
the warning gives. Koan never drops it itself.

## Error modes

- Provider errors (NpgsqlException) surfaced; transient failures retried per options.
- Timeouts honor `CommandTimeoutSeconds`.
- Unsupported predicates surface explicitly; simplify the predicate or materialize a known-small page.
  Provider-bounded streams may apply supported pointwise residuals but never hide a full-source fallback.

## Operations

- Health: an available PostgreSQL package stays non-critical until it wins default election or a runtime operation
  selects one of its sources. Active sources resolve the same source-specific connection as repositories, then open
  it and execute `SELECT 1`.
- Metrics: command duration and failures; track server version when diagnosing feature behavior.
- Logs: SQL with parameter redaction; note when in-memory filtering occurs.

## Verification

- Connector suite: 26/26 against pinned `postgres:18.4-alpine` on 2026-07-28.
- The suite covers native query receipts, atomic batch behavior, managed-field isolation, compact legacy mapping,
  composite/generated identities, nested `jsonb` preservation, read-only safety, non-creating External lifecycle,
  named SQL, inspection, and bounded sampling.

## References

- DATA-0045 default projection policy: `/docs/decisions/DATA-0045-default-projection-policy-and-json-pushdown.md`
- [DATA-0107 provider-bounded Entity streams](../../../../docs/decisions/DATA-0107-provider-bounded-entity-streams.md)
- [Entity access and streaming](../../../../docs/guides/data/entity-access-and-streaming.md)

