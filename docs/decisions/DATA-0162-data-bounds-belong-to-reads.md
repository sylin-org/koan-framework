---
id: DATA-0162
slug: data-bounds-belong-to-reads
domain: Data
status: Accepted
date: 2026-10-30
title: A data bound sizes memory or a read, never what an application may hold
related:
  - DATA-0049
  - DATA-0107
  - DATA-0158
  - DATA-0159
---

# DATA-0162: A data bound sizes memory or a read, never what an application may hold

## Context

Measured at `51b2c33`. Several limits in the data pillar were shortcuts that stood in for storage: past a fixed count
of something the application keeps (partitions, collections, ids in one call, operations in one batch), Koan threw,
or it returned part of an answer without saying so. Each such limit stops a working application on the day its data
crosses a number nobody chose, and some of them changed with the store or with whether a read scope was active.

## Decision

1. **A cache evicts, never refuses.** A repository's compiled plans, one per physical container, live in
   `RelationalPlanCache`, an LRU bounded by the store's plan setting (`Koan:Data:Mapping:PlanEntries` on PostgreSQL,
   MySQL, SQL Server and Firebird; the adapter constant on SQLite and DuckDB). MongoDB's per-repository memo of
   ensured collections is an LRU of 1,024. A full cache drops the least recently used entry, which is recomputed on
   its next use: a plan is recompiled, a collection is ensured again (an idempotent act). The 257th PostgreSQL
   partition and the 1,025th MongoDB collection work like the first. The readiness coordinator's memo of proven
   targets is the same kind of cache: a hit marks the target used, without a lock, and admitting past the bound
   forgets the least recently used eighth at once, so the hottest tables stay proven instead of being the first
   evicted, and no admission sorts the whole memo.

2. **A set of ids of any size works, with or without a read scope, and answers what Eq answers.** `In` and `Nin`
   lower through the dialect's `MemberOf`, which binds the whole set as one value with each member typed exactly as a
   lone parameter of it binds, so a member in the set compares as the member alone does and In's answer is Eq's
   answer member by member:
   - PostgreSQL binds one typed array (`= ANY`) of text, integers, uuids, numerics, doubles, reals, booleans, or
     timestamps of one `DateTimeKind`;
   - SQLite binds one JSON array read by `json_each`, each member written as the value Microsoft.Data.Sqlite binds
     (INTEGER, REAL, or the driver's TEXT for strings, upper-case Guids, dates, decimals);
   - SQL Server binds one JSON text read through `OPENJSON`, so the store holds nothing for a set and any login that can
     read can use one. Each member reads as the type a lone parameter of it binds as: nvarchar(4000) or nvarchar(max)
     (a char binds as text), uniqueidentifier, datetime2(7) for a DateTime (DATA-0164), datetimeoffset(7), bit, bigint,
     int, smallint, tinyint. A double or a single travels as the integers that are its exact value, mantissa and
     exponent, because the server's parser lands some decimal text a unit off. A decimal reads at the precision and
     scale of its own value, one `OPENJSON` per shape, compared as sql_variants by value, because a lone decimal takes
     its own value's precision and scale. Text is read through a conversion from varbinary, whose coercible-default
     collation yields to the column's exactly as a parameter's does; read straight from the JSON, a member would carry
     the database collation as an implicit label and conflict with a column declared under another. `Nin` reads as
     `NOT EXISTS`, because members read as nullable and `NOT IN` over a nullable set rescans it per row (over 70,000 ids:
     past 60 seconds against 0.2). A TimeSpan, DateOnly or TimeOnly on a document field reaches a set already encoded,
     as ticks or as text, and binds through the bigint or nvarchar form; read raw from a typed column it stays one
     parameter per member, because no typed column of them round-trips on SQL Server.

   The parameter bound belongs to the statement. `ILinqSqlDialect.MaximumParameters` is the store's measured limit
   (32,766 on SQLite, 65,535 on PostgreSQL, 2,098 on SQL Server, whose client refuses 2,099; none declared elsewhere),
   and a filter is translated against it less the parameters its statement binds itself
   (`SqlFilterTranslator.Translate(filter, reservedParameters)`): a conditional replace's SET values and identity, a
   guarded delete's identity; a query reserves none. Every list-valued operator is a list that can move: `In` and
   `Nin` through `MemberOf`, and `HasAny`, `HasAll` and `HasNone` through `CollectionMemberOf`, which binds wherever
   the dialect carries the set because the per-member form reads the JSON array once per member. SQLite and PostgreSQL
   bind every list they can carry as one value; SQL Server keeps an `In`/`Nin` as a parameter list
   (`PrefersSetParameters`), which it plans best, while the statement fits. While a statement is over its bound the
   translator moves its largest remaining per-member list to one value and lowers the filter again; it refuses only
   when nothing more can move and the statement is still over, naming each list that could not and why, or saying the rest are plain comparisons. What still refuses, past that bound:
   on SQLite a list of byte arrays, non-finite reals or unsigned 64-bit integers; on PostgreSQL a list of mixed types,
   of timestamps of mixed `DateTimeKind`, or of a type Npgsql builds no array of (chars, unsigned integers, bytes,
   byte arrays); on SQL Server a list of mixed types, of TimeSpan, DateOnly or TimeOnly read from a typed column, of
   unsigned or signed-byte integers, or of byte arrays.
   MySQL, Firebird and DuckDB carry no set as one value and declare no bound: their per-statement limits were not
   measured here, a DuckDB query over 70,000 members ran, and a set past what the server accepts fails there. The
   translator also splits a per-member set into IN lists the dialect allows (1,500 on Firebird). `GetMany` and
   `DeleteMany` name ids the same way, or in slices that stay inside the store's per-statement parameters, deleting
   the slices inside one transaction. A store whose sets cost a parameter per member declares that bound as
   `FilterSupport.MaximumSetMembers`, and the facade reads a scoped key set (`In(ids) AND scope`) in slices of it, so
   tenancy or soft-delete never turns a working call into a failing one. A `Not` over a field's `In` lowers as its
   `Nin`: the two are exact complements, and the direct form stays a plain membership the store can hash.

   An element of a JSON array compares as `Has` compares it, and as a scalar read out of a document compares
   (DATA-0139), so `HasAny`, `HasAll` and `HasNone` answer what their members answer one by one. The comparand is
   encoded to the canonical store form (DATA-0100) and then to the dialect's element form
   (`ILinqSqlDialect.JsonArrayComparand`), and the element is read as its type (`JsonArrayContains(column, parameter,
   elementType)` and `CollectionMemberOf(…, elementType, …)`): a Guid and a DateTime as the document's text on SQLite,
   PostgreSQL and SQL Server; on PostgreSQL every other element cast to its type, a real number to double precision;
   on SQL Server every other element converted as the scalar comparison converts it, a decimal or a real number to
   decimal(38, 10), so it meets a member by value to ten places, as `Eq` does, and a decimal list binds through the
   sql_variant type; on SQLite a decimal comparand as the real `json_each` reads. On PostgreSQL a document holding
   null rather than an array there matches nothing instead of failing.
3. **A batch is bounded by its caller, never by the store.** A SQLite or DuckDB batch runs each queued operation as its
   own statement inside one transaction, so its fixed cap of 4,096 operations protected nothing and is gone. The only
   bound left is the one a caller declares in `BatchOptions.MaxItems`. Multi-row statements (`UpsertMany`) are sliced
   inside their one transaction.
4. **A cut answer says it was cut.** A Direct query returns `DirectRows<T>`, an `IReadOnlyList<T>` with a
   `Completion`: at most the session's `MaxRows`, and `RecordLimit` when the command had more. An opaque SQL command
   has no key Koan can page by, so the answer labels itself instead of paging. The untyped query reads one row past
   the bound to tell a cut answer from one of exactly `MaxRows`; the typed query passes on `RecordSetMaterializer`'s
   completion, which it used to drop; the entity-routed query, which ignored `MaxRows`, asks its adapter for one row
   more than `MaxRows` through the `RelationalInstructions.MaxRows` option of `SqlQuery`, and every relational adapter
   (SQLite, PostgreSQL, CockroachDB, MySQL, SQL Server, Firebird, DuckDB) stops reading there, so no row past the
   bound is materialized. Npgsql and SqlClient drain the rest of a result when its reader closes. `BoundedReads`
   decides what happens to it, on every Direct path (untyped, typed, transaction, entity-routed): only a declared Read
   outside a caller's transaction cancels its command at the bound, so the server stops producing rows nobody reads,
   and a failure the abandoned rest raises while the reader closes — the cancellation itself, or a row past the bound
   that could not be produced — does not fail the read; the caller's own cancellation still does. Any other command,
   and every read inside a `DirectTransaction`, drains to its end and is never cancelled: cancelling an `UPDATE …
   OUTPUT` at its bound rolled the update back, and cancelling a `SELECT …; INSERT …` batch skipped the INSERT, both
   silently. A failure while it drains is raised. A driver that cannot cancel drains instead. The effect is the
   caller's declaration, and Koan does not check it against the SQL. PostgreSQL enforces a declared Read only on the
   entity-routed query, which it runs in a read-only transaction, so a write labelled Read fails there. On the untyped
   and typed Direct reads, and on every SQL Server path (SQL Server has no read-only statement), a write labelled Read
   is cancelled at the bound like any read, and the server rolls back the statement the cancel interrupts.
5. **A setting nothing reads is not reported.** `DataRuntimeOptions.EnsureSchemaOnStart` and its boot-report entry
   (`Koan:Data:Runtime:EnsureSchemaOnStart`) are deleted. Nothing read them: schema is provisioned on first use of
   each container, which is the only way a container that does not exist yet (a new partition or tenant) can be
   provisioned, and a report saying otherwise was false.
6. **A probe that cannot measure says so.** The `data:lexical` readiness probe holds no tenant, and a tenant-scoped
   entity's rows are each tenant's to count, so the probe cannot read its index's currency. It reports that entity as
   `unmeasured:segmented` and the report as Unknown (with every other index current), never as a permanent Degraded
   that would hide a real stale index; `Entity.Lexical.Status()` inside the tenant measures it on demand. A host-wide
   cross-tenant count was not chosen: it would read every tenant's rows to answer an operator, and under
   container-per-tenant isolation there is no one container to count.

## Compatibility

- Behavioural: a partition or collection past the bound no longer throws `InvalidOperationException`.
- Behavioural: `GetMany` and `DeleteMany` over tens of thousands of ids, and a query's `In`/`Nin` over tens of
  thousands of members of the types each store carries in one value, no longer fail with the store's parameter or
  expression-depth error, and on SQL Server they answer what Eq answers. A statement past `MaximumParameters` whose
  sets cannot move to one value is refused before it reaches the store, naming them. MySQL, Firebird and DuckDB
  declare no such bound; a statement past what their server accepts still fails at the server.
- Behavioural: `Has`, `HasAny`, `HasAll` and `HasNone` over a JSON array of Guids, numbers, booleans or DateTimes
  answer on PostgreSQL, which failed with "operator does not exist: text = uuid" and its kin, and on SQL Server for
  decimals and DateTimes, which failed with an arithmetic overflow or a date conversion; on SQLite they find the Guids,
  decimals and DateTimes a row holds, which they missed. On PostgreSQL a null list no longer fails a list filter.
  DuckDB, MySQL and Firebird compare elements as before.
- Behavioural (SQL Server): a document field holding a Guid or a DateTime compares as the
  document's text (DATA-0139), as on SQLite and PostgreSQL, where a DateTime used to fail converting the document's
  seven-digit fraction to `datetime`.
- Behavioural: a SQLite or DuckDB batch of more than 4,096 operations commits instead of throwing
  `ArgumentOutOfRangeException`. Each adapter's `Constants.MaximumBatchItems` is renamed `MaximumRowsPerStatement`, the
  slice its multi-row statements write per statement.
- Removed: `DataRuntimeOptions.EnsureSchemaOnStart` and the `Koan:Data:Runtime:EnsureSchemaOnStart` setting and
  boot-report entry. A configuration that sets it is ignored, as it always was.
- Public surface: `RelationalInstructions.MaxRows`, `InstructionSql.Query(string, DataOperationEffect, object?, int)`
  and `InstructionSql.MaxRows(Instruction)`; `AdoCommands.QueryRowsAsync` takes an optional `maxRows`;
  `IDirectSession.Query` and `IDirectTransaction.Query` return `DirectRows<T>` (an `IReadOnlyList<T>`, so reading code
  compiles unchanged); `FilterSupport.MaximumSetMembers`, `ILinqSqlDialect.MemberOf` and `MaximumInListMembers`
  (default members, so a dialect outside this repository keeps compiling), `ILinqSqlDialect.PrefersSetParameters` and
  `MaximumParameters` (default members), `ILinqSqlDialect.CollectionMemberOf` (default member, taking the element
  type) and `SetMatch`, `JsonArrayComparand` and the element-typed
  `JsonArrayContains(string, string, Type)` (default members), `SqlFilterTranslator.Translate(Filter, int)`,
  `ISqlParameterValue` (a parameter value that configures its provider parameter; `SqlArrayParameter` is one),
  `BoundedReads` (`MayCancel`, `Close`) in `Koan.Data.Core.Direct`, an optional `cancelAtBound` on
  `AdoCommands.QueryRowsAsync`, and, for test hosts, `KoanDataSpec.BootAsync(Action<KoanApplicationBuilder>)`.
- No stored format changes. Stores are rebuilt, never migrated.

## Evidence

- `PostgresPartitionPlanBoundSpec` (Connector.Postgres): 257 partitions of one entity are written and read, and the
  first, whose plan was dropped, reads again. It failed before the change with "reached its configured mapping-plan
  limit of 256".
- `MongoPartitionSchemaBoundSpec` (Connector.Mongo): 1,025 partitions are written and read, and the first reads again.
  It failed before the change with "reached the bounded collection-plan limit of 1024".
- `LargeIdentitySets` (AdapterSurface TestKit), run by `SqliteLargeIdentitySetSpec`, `PostgresLargeIdentitySetSpec`,
  `SqlServerLargeIdentitySetSpec` and `MongoLargeIdentitySetSpec`: 70,000 ids are read in order and removed with and
  without a read scope, another scope's row is neither read nor removed, and a query's `In` and `Nin` over the same set
  answer every row and none. Before the change SQLite failed with "too many SQL variables" (scoped) and "Expression
  tree is too large (maximum depth 1000)" (unscoped), PostgreSQL with "A statement cannot have more than 65535
  parameters", and SQL Server's query `In` (measured before it moved past the parameters) with "The server supports a
  maximum of 2100 parameters".
  The oracle also passed once on DuckDB; it takes nearly nine minutes there, so the DuckDB suite does not run it.
- `SetMembershipConvergence` (AdapterSurface TestKit), run by `SqliteSetMembershipConvergenceSpec`,
  `PostgresSetMembershipConvergenceSpec` and `SqlServerSetMembershipConvergenceSpec`: for Guid keys and Guid, DateTime,
  DateTimeOffset, double, single, decimal, long, int, short, byte, TimeSpan, DateOnly, TimeOnly, enum, char, string and
  bool properties, on document fields and on typed columns, `In` over sets of 1, 2, 2,502 and 70,000 members (fewer
  where the type has fewer values) answers the union of what `Eq` answers for each member, and `Nin` the rest; a
  statement holding two sets of 1,501 answers their intersection, and statements holding 1,050 boolean lists and nine
  whole-domain byte lists answer as one list does; no query falls back to memory. The row values are a non-dyadic
  double, doubles of magnitude 1e14 to 1e19, a subnormal, singles, dates, offsets, times and spans with sub-millisecond
  ticks, a decimal with fourteen integral digits and one with scale 28, a long past 2^53, text past 4,000 characters,
  and a quote and non-Latin chars.
  Typed offset columns are checked on SQLite and SQL Server, and typed TimeSpan, DateOnly and TimeOnly columns on
  SQLite and PostgreSQL: PostgreSQL's timestamptz neither writes a non-zero offset nor hydrates one, and SQL Server
  neither writes a TimeSpan column nor hydrates a date or time column — storage matters outside this record, and the
  properties stay covered on the document there. Run against the round-three code, this widened oracle failed on SQL
  Server and on PostgreSQL; at the round-two code the earlier oracle failed on SQL Server as recorded in round three.
  During this round, before the char kind and the interval, date and time arrays existed, the current code refused a
  2,502-char set on SQL Server and 70,000-member interval, date and time sets on PostgreSQL.
- `SqlFilterSetMembershipSpec` (Relational): a statement past the dialect's bound with no set that can move is refused
  naming its sets; with two sets past the bound together, the larger moves to one value and the other stays a list.
- `SqlServerSetCollationSpec` (Connector.SqlServer): on an External source, a 2,502-member text set over a
  `Latin1_General_CS_AS` column answers what Eq answers.
- `SqlServerStatementBoundSpec` (Connector.SqlServer): a conditional replace on a row of nineteen SET columns, guarded
  by an `In` over its key of every size from 2,070 to 2,098 members and of 2,502, replaces the row each time. Run
  against the round-four code it failed with "The server supports a maximum of 2100 parameters".
- `CollectionSetConvergence` (AdapterSurface TestKit), run by `SqliteCollectionSetConvergenceSpec`,
  `PostgresCollectionSetConvergenceSpec` and `SqlServerCollectionSetConvergenceSpec`: over rows holding two values,
  one duplicated, a third alone, an empty array and a null, `HasAny`, `HasAll` and `HasNone` over text, Guid, decimal,
  double, int, long, bool and DateTime lists of 1, 2 and 2,502 members (and 70,000 for text and long) answer the
  union, intersection and complement of what `Has` answers per member, and every per-member `Has` answers and finds
  each value a row holds as read back, the doubles including 1e-5, 2.5e-5, 1e-7 and 1e20, which the write path stores
  in decimal notation. Comparing SQL Server's double elements as the serializer's text missed those four; they now
  convert as the scalar comparison converts. Run against the round-four code it failed on PostgreSQL, where every
  `Has` but text failed with "operator does not exist: text = uuid" and its kin and a null list with "cannot extract
  elements from a scalar", and on SQL Server, where a decimal `Has` overflowed and a DateTime one failed converting;
  on SQLite it passed only because `Has` and the lists alike missed every Guid, decimal and DateTime a row held. It
  notes one value: a row saved with one third holds 0.333333333333333 after the relational write path, so no `Has`
  finds one third on any of the three stores; that loss is outside this record.
- `SqlServerCollectionSetSpec` (Connector.SqlServer): `HasAny`, `HasAll` and `HasNone` over lists of 2, 2,050 and 70,000
  values answer as over two. At the round-three code the 2,050 and 70,000 lists were refused ("none of its sets can bind
  as one value here: ."); kept per member, the 2,050-member list ran past SQL Server's 30-second timeout.
- `PostgresDirectEntityQuerySpec` and `SqlServerDirectEntityQuerySpec`, round four: an untyped and a typed Direct read
  bounded at three rows over 30,000,000 answer in milliseconds (201 and 37 ms on PostgreSQL, 52 and 27 ms on SQL
  Server when measured); at the round-three code they drained, past one second. A Write-effect `UPDATE … OUTPUT` over
  500,000 rows on SQL Server returns three rows and updates all 500,000, and a Write-effect `SELECT …; INSERT …` on
  PostgreSQL runs its INSERT; at the round-three code both writes were lost to the cancel.
- `SqliteLargeBatchSpec` (Connector.Sqlite): a batch of 5,000 adds and then 5,000 deletes commits. It failed before the
  change with "SQLite accepts at most 4096 items per bounded batch".
- `DirectDataAccessSpec.A_direct_query_past_its_max_rows_says_it_was_cut` (Data.Core) and
  `SqliteDirectEntityQuerySpec` (Connector.Sqlite): over five rows with `MaxRows` 3 the untyped, typed, transaction
  and entity-routed queries return three rows labelled `RecordLimit`, and with `MaxRows` 5 all five labelled
  `Complete`. Before the change none of them could say so. A second `SqliteDirectEntityQuerySpec` case runs an
  entity-routed query with `MaxRows` 3 over SQL whose sixth row raises an integer overflow: it returns three rows
  labelled `RecordLimit`. Before round two it failed with that overflow, because the adapter read every row.
- `PostgresDirectEntityQuerySpec` and `SqlServerDirectEntityQuerySpec`: an entity-routed query with `MaxRows` 3 over SQL
  whose sixth row divides by zero returns three rows labelled `RecordLimit`, a 30,000,000-row result bounded at three
  rows answers in milliseconds (165 ms and 35 ms when measured), and the connection serves the next query. Without the
  cancel, PostgreSQL failed with "22012: division by zero" while the reader drained, and SQL Server took 30,071 ms on
  the 30,000,000-row result.
- `SourceReadinessCoordinatorSpec.A_full_stage_cache_keeps_the_targets_in_use_and_forgets_the_least_recently_used`
  (Data.Core): with room for four targets, the first target proven and then used again is still proven after a fifth
  is admitted, and the least recently used one is proven again. It failed before the change, which evicted the first
  target proven.
- `SqliteLexicalReadinessUnderTenancySpec` (Connector.Sqlite): with a tenant dimension over a `[Lexical]` entity, the
  probe reports it `unmeasured:segmented` and the report Unknown, while `Status()` inside the tenant reports it
  current. Before the change it reported `error:SegmentationRequiredException` and Degraded.
- `DuckDbLargeBatchSpec` (Connector.DuckDb): a batch of 4,100 adds and then 4,100 deletes commits. It failed before the
  change with "DuckDB accepts at most 4096 items per bounded batch".
