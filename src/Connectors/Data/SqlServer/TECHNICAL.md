---
uid: reference.modules.Koan.data.sqlserver
title: Koan.Data.Connector.SqlServer - Technical Reference
description: SQL Server adapter for Koan data.
packages: [Sylin.Koan.Data.Connector.SqlServer]
source: src/Connectors/Data/SqlServer/
---

## Contract

- The adapter declares `DataCaps.Query.ProviderBoundedPaging` and applies numbered pages in SQL Server
  before candidate rows are materialized into application memory.
- Relational translation and schema helpers remain in Koan.Data.Relational.
- One `SqlServerRepository<TEntity,TKey>` and compiled `SqlServerEntityPlan<TEntity,TKey>` serve managed Id+object
  storage and explicit flat/object/nested/composite/generated maps.
- External lifecycle performs no DDL; read-only source policy rejects Entity mutation before provider I/O.
- `Inspect()` provides neutral table/view discovery, description, and bounded sampling. Registered SQL reads/scalars
  require a source-owned read lane and execute inside a rollback-only transaction; database grants remain the security
  boundary.

## Configuration

- Connection string, schema governance, naming, discovery, and readiness use
  the existing `SqlServerOptions` and adapter-readiness contracts.

### Options (typical keys)

- `ConnectionStrings:Default` (first-win)
- `ConnectionStrings:SqlServer`
- `Koan:Data:Sources:<name>:ConnectionString`
- `Koan:Data:Sources:<name>:ReadLanes:<lane>:ConnectionString`
- `Koan:Data:SqlServer:ConnectionString`
- `Koan:Data:SqlServer:Schema`
- `Koan:Data:SqlServer:NamingStyle` and `Separator`
- `Koan:Data:SqlServer:DdlPolicy`
- `Koan:Data:SqlServer:SchemaMatchingMode`
- `Koan:Data:SqlServer:AllowProductionDdl`
- standard readiness options under `Koan:Data:SqlServer:Readiness`

`ConnectionString=auto` attempts orchestration discovery before using the localhost development
fallback. The connector does not currently expose provider-specific retry or command-timeout options.

## LINQ and pushdowns

- Supported expressions follow `Koan.Data.Relational` translator. See: `xref:reference.modules.Koan.data.relational#supported-linq-subset`.
- The filter support declared is `RelationalFilterSupport.CaseFolding`: the database is created without a
  `COLLATE`, so string equality follows the server's default collation, which folds case, and
  `FilterSupport.OrdinalStrings` is false. A text id column alone is `Latin1_General_BIN2`, so ids differing only by case
  are two rows (DATA-0164).
- A DateTime binds as `datetime2(7)`, exact to the tick (`SqlServerDateTime`).
- `Upsert` updates, then inserts; a primary-key violation means another writer got there first, and the update runs again, so
  concurrent upserts of a new id have one winner and no error.
- SqlClient sends at most 2,098 parameters in a statement (measured: 2,099 is refused). `GetMany` and `DeleteMany`
  name ids in slices of 2,000
  (deleting them in one transaction), and the store declares `MaximumSetMembers` 1,900, the slice the facade reads a
  scoped key set in, leaving room for the scope's own parameters.
- A filter may bind 2,098 parameters less those its statement binds itself: a conditional replace's SET values and
  identity. An `In`/`Nin` binds a parameter per member while the statement fits; past that, the largest lists move, one
  at a time, to one table-valued parameter. A `HasAny`/`HasAll`/`HasNone` list binds as one table-valued parameter
  wherever its set type exists, because its per-member form reads the JSON array once per member, and per member
  otherwise. A statement still over its bound with nothing more that can move is refused, naming each list that could
  not and why (DATA-0162).
- A JSON array element compares exactly as a document scalar does, so `Has` and the list operators answer what `Eq`
  answers: a Guid or a DateTime as document text, a decimal or a real number converted to decimal(38, 10) (it meets a
  member by value to ten places; a decimal list binds through the sql_variant type), an integer as bigint, a boolean
  as bit.
- A set type is `[schema].[koan_set_<scalar>_<hash>]`, the hash the first eight hex digits of the SHA-256 of its one
  column `[koan_member] <type> NOT NULL`; a changed shape is a new name. The column is exactly the SqlDbType a lone
  parameter of the members binds as: nvarchar(4000) or nvarchar(max) (a char too), uniqueidentifier, datetime2(7),
  datetimeoffset(7), float, real, bit, bigint, int, smallint, tinyint, and sql_variant for decimals, which bind with
  their own precision and scale. Text is read through `CONVERT(nvarchar(…), CONVERT(varbinary(…), [koan_member]))`, whose
  coercible-default collation yields to the column's as a lone parameter's does. A TimeSpan, DateOnly or TimeOnly on a
  document field reaches a set already encoded (ticks, or text) and binds through the bigint or nvarchar type; read
  from a typed column it has no set type (no typed column of them round-trips here), so such a list, or one of mixed
  types, unsigned or signed-byte integers or byte arrays, stays a parameter per member.
- The repository ensures the types at ready, once per route and schema: whenever it reads the catalog it creates every
  missing type when the source may create storage objects, remembers a denied `CREATE TYPE` (permission 262 or any
  refusal) as "may not create" without failing the operation, and reads each type's column to check it against the
  definition. A list whose type is missing or of another shape stays a parameter per member; a statement still over
  its bound is refused naming the type and why: the login was denied creating it, or the source may not create storage
  objects, with the `CREATE TYPE` statement; or the type has another shape, with `DROP TYPE` where the source creates
  types and `DROP TYPE …; CREATE TYPE …` where it does not. A refusal marks the catalog stale, and the next operation
  reads it again (at most once a second), so a type created or dropped after a refusal is used without a restart. The
  server's "Cannot find data type" for a set type (351, or 2715) marks the catalog stale at once, and the query, count
  or conditional replace runs once more: a dropped type is created again, or the operation is refused by name
  (DATA-0162).
- A Direct read that stops at its bound cancels its command only when its effect is a declared Read outside a caller's
  transaction. The effect is the caller's declaration, and Koan does not check it against the SQL. PostgreSQL enforces
  a declared Read only on the entity-routed query, which it runs in a read-only transaction, so a write labelled Read
  fails there. On the untyped and typed Direct reads, and on every SQL Server path (SQL Server has no read-only
  statement), a write labelled Read is cancelled at the bound like any read, and the server rolls back the statement
  the cancel interrupts. A write a Read-labelled command carries past the bound may therefore be rolled back here
  (DATA-0162).
- A document field holding a Guid or a DateTime compares as the document's text, both sides rewritten to the
  fixed-width forms of `RelationalDocumentText` (DATA-0139), as on SQLite and PostgreSQL.
- Paging uses `OFFSET`/`FETCH`. Every caller-requested provider-bounded stream sort component must be a
  portable scalar -- an enum, or an integral, decimal, floating, or temporal type -- for its comparison
  to hold on every qualified adapter. Any other key still streams here; Data.Core records a fact naming
  the keys this store, rather than Koan, defines the order of. The one rejection is an Entity-identifier
  sort whose key type is not the provider-stable `string`. Data.Core appends that identifier as an
  opaque tie-breaker, which is not a CLR or cross-provider collation promise.

## Provider-bounded streaming

- `AllStream` and `QueryStream` are coordinated as lazy numbered pages by Data.Core.
- `batchSize` is the maximum Koan-visible candidate page, not a promise about opaque SQL client
  buffers.
- Deep or collection ordering rejects correctively before provider I/O rather than falling back to a
  complete-result sort.
- Offset paging is not snapshot isolation and does not provide mutation-safe traversal, resume tokens,
  or a public cursor.

## Error modes

- Provider errors (`SqlException`) surface to the caller; the connector does not add a retry policy.
- Unsupported predicates surface explicitly; simplify the predicate or materialize a known-small page.
  Provider-bounded streams may apply supported pointwise residuals but never hide a full-source fallback.
- An unreachable selected SQL Server reports unhealthy readiness and never falls back to a different
  data provider.

## Operations

- Health: connection open/round-trip.
- Configuration/discovery logs avoid introducing a second provider-selection path.

## Verification

- Connector suite: 33/33 against the pinned SQL Server 2025 container on 2026-07-28.
- Coverage includes CRUD/query/count/paging/batch, managed fields, compact external mappings, nested JSON preservation,
  composite/generated identities, policy enforcement, named SQL, neutral inspection, and bounded sampling.

## References

- [DATA-0107 provider-bounded Entity streams](../../../../docs/decisions/DATA-0107-provider-bounded-entity-streams.md)
- [Entity access and streaming](../../../../docs/guides/data/entity-access-and-streaming.md)

## Enum storage contract

Default Entity storage preserves enum names as strings, including nullable values, nested values, collections,
and declared EnumMember aliases. Unnamed numeric values fail instead of silently changing the storage format.
Queries use the same spelling. Ordinary enum ordering uses declared ordinal ranks in native expressions while
the stored value stays a string; native ordering of arbitrary Flags combinations rejects correctively.
An explicit external mapping codec remains responsible for its declared physical representation.
Existing numeric rows or columns require a separate, explicit migration; upgrades do not rewrite them automatically.
