---
uid: reference.modules.Koan.data.sqlite
title: Koan.Data.Connector.Sqlite - Technical Reference
description: SQLite gold-reference adapter for managed and externally mapped data.
packages: [Sylin.Koan.Data.Connector.Sqlite]
source: src/Connectors/Data/Sqlite/
last_updated: 2026-10-29
---

## Execution model

SQLite has one repository and one compiled mapping path. A managed Entity map is simply the framework-generated
`Id + Json` mapping; an explicit `Map<TEntity>` supplies another immutable mapping plan to the same executor. CRUD,
hydration, identity predicates, filters, sorts, counts, bulk work, conditional writes, and schema checks therefore
cannot drift between managed and legacy shapes.

There is no compatibility bridge, mapped-repository variant, adapter-local object mapper, or query compiler. The
host bounds compiled entity plans, memory-source keepers, batch items, and native parameters. Mapping accessors and
mapping-use decisions are compiled outside warm operations; mapped member access performs no reflection.

## Source integration

The adapter exposes registered SQL record/scalar operations and provider-neutral inspection of tables and views.
Container continuations are bounded, opaque after Data wraps them, and resumable. Sampling reads at most `take + 1`
native rows so it reports `Complete` versus `ProviderLimit` truthfully.
Repository plans derive container names from the constructing SQLite factory, including when SQLite is an inactive
cutover target behind a different active provider.

`SqlOperationBinding` is opaque. A registered SQL operation therefore selects a configured read lane. SQLite opens
that lane's connection and enables native `query_only` before executing the command; an effective write fails at
the provider boundary. Connection strings remain outside public plans and diagnostics.

## Mapping

An explicit map supports:

- one scalar or composite identity;
- provider-generated single-column identity;
- scalar physical names;
- structured object values stored as SQLite JSON text;
- scalar logical values at nested JSON paths; and
- symmetric codecs compiled by Data's mapping plan.

The same physical binding is used for hydration, parameters, filters, order expressions, conditional replace, and
writes. Sibling nested-path writes use `json_set` against the existing value, preserving unrelated legacy fields.
Whole-object bindings replace the whole declared object by design.

SQLite accepts an empty mapping namespace or `main`. Explicit mappings reject framework-managed row fields and
ambient partitions until those concerns have explicit physical bindings; this is a fail-closed boundary, not a
fallback to a shared container.

## Query guarantees

Supported Filter AST operations are lowered through `Koan.Data.Relational`. Filter, fully handled sort, numbered
page, and exact-count receipts reflect only provider-executed work. Provider-bounded candidate reads request one
extra row to prove whether the bound was exceeded. Collection operations use SQLite JSON functions.

Raw Entity queries are an explicit escape hatch. Registered named SQL is preferred for stable application intent
and is independently bounded by Source Integration limits.

`QueryRaw` and `CountRaw` take a predicate the caller owns, never request text (WEB-0074). The adapter runs it as exactly
one statement - `;` and NUL are refused before the engine sees the text - on a dedicated, unpooled connection with
`PRAGMA query_only = ON`, so a mistaken raw read can read but cannot write. The connection is unpooled because a pooled
one goes back to its pool still holding the pragma. A predicate can still read anything the database holds.

## Schema and policy

Managed sources with `AutoCreate` create the required table on first use. External sources perform validation only
and never create, alter, or drop a physical object. `Access: ReadOnly` is enforced by Data before repository
construction or provider I/O. The two decisions are independent.

Under row-scoped tenancy a unique `[Index]` is unique per tenant
([DATA-0150](../../../../docs/decisions/DATA-0150-unique-indexes-are-unique-per-tenant.md)): it leads with the
`json_extract` of `__koan_tenant` that the tenant filter emits, under its own name (`IX_<table>___koan_tenant_<members>`,
or a declared name with `___koan_tenant` appended). A duplicate in one tenant is a `UniqueValueConflictException` that
does not name the index. A database built before keeps its global unique index; Koan logs a warning naming it and the
`DROP INDEX "<old>";` that removes it, and never drops it. Until it is dropped, a value one tenant holds is still refused
for every other. Confirm the new index with `SELECT name, sql FROM sqlite_master WHERE tbl_name = '<table>'`, then drop
the old one.

## Index names

SQLite index names are database-wide, so every keyed index is created under the relational rule for such stores, the
one PostgreSQL follows ([DATA-0160](../../../../docs/decisions/DATA-0160-sqlite-qualifies-declared-index-names-per-partition-table.md)).
A derived name (`IX_<table>_<members>`) carries its table, which carries its partition. A name declared with
`[Index(Name = ...)]` is used as written on the entity's own table and becomes `<name>_<eight-character hash of the
table name>` on a partition table, so every partition table holds its own copy of each declared index and the entity's
own table keeps the names its queries are proven against. Two entities that declare one name are refused on their own
tables, naming the table that holds it. A partition table provisioned before its entity's own table by an earlier
build can hold the unqualified name itself; the entity's own table is then refused with the `DROP INDEX` statement
that frees the name. Such a partition table's unqualified name is also what DATA-0150's warning reports when that
table's global unique index is the one left behind. Koan drops nothing on its own; `SELECT name, sql FROM sqlite_master WHERE type = 'index' AND
tbl_name = '<table>'` lists what a table holds.

## Bulk and batch

Constrained single-item endpoint creates use `IInsertOnlyRepository.Insert`. SQLite executes `INSERT OR ABORT`,
overriding any external table replacement policy. Only a native primary-key violation naming the validated table
and identity columns produces a conflict receipt; other constraints remain failures. Zero affected rows, including
an external trigger's `RAISE(IGNORE)`, do not prove collision or a safe commit outcome. Schema validation requires
the declared identity to match the native primary key before dispatch. Provider-generated identities
use `RETURNING` and are propagated to the submitted entity and receipt. Ordinary `Save` remains an upsert.

The initial constrained bulk surface rejects any create-classified item before persistence; this insert contract
does not claim atomic mixed insert/update execution. Existing authorized-update races remain outside its guarantee.

`SqliteInsertOnlySpec` verifies physical row preservation, concurrent single-winner insertion, generated keys,
unrelated unique constraints, missing native primary keys, trigger ignore, and other-table trigger key failures.

`UpsertMany` emits one bounded multi-statement SQLite command inside one transaction and consumes each `RETURNING`
result in input order, including provider-generated identities. Oversized batches or parameter sets reject before
dispatch. `DeleteMany` uses one predicate command. The Entity batch surface uses one transaction and reports a
complete ordered outcome for every queued operation; mutate-by-id necessarily reads the target inside that same
transaction.

## Atomic commit

The factory implements `IAtomicCommitProvider` and declares `write.atomicCommit`
([DATA-0158](../../../../docs/decisions/DATA-0158-sqlite-posture-and-atomic-commit.md)). When every operation an
`EntityContext.Transaction` scope tracked resolves to one SQLite route, the coordinator provisions each operation's
table outside any transaction, then asks the factory for one native transaction: `BEGIN IMMEDIATE` on a dedicated
unpooled connection, which `SqliteConnections` makes ambient for the calling flow before its first await. The unit is
keyed by the store identity the coordinator admits routes with (an anchored, case-folded file path; one in-memory
database per source), so two sources that spell one file differently share the one unit. Until the
scope ends, `SqliteRepository.Open` returns that connection and transaction for every read and write of the route,
and `SqliteDdlExecutor` describes and writes schema over it, so a lifecycle read sees the rows written earlier in the
scope and a schema statement does not wait on the lock the scope holds. The coordinator dispatches in call order and
commits once; a failed operation disposes the scope, which rolls the transaction back, and the readiness of any
target the scope itself provisioned is invalidated. A failure before the transaction begins — a table that cannot be
provisioned, or `BEGIN IMMEDIATE` waiting out the busy timeout behind another writer — reports `NotCommitted`; only a
failed `COMMIT` reports `Unknown`
([DATA-0161](../../../../docs/decisions/DATA-0161-a-commit-that-fails-before-dispatch-reports-not-committed.md)). Raw reads (`QueryRaw`, `CountRaw`, Read-labelled SQL) keep their
own `query_only` connection and see committed state only. `UpsertMany`, `DeleteMany`, conditional bulk deletes and
`Batch().Save` join the ambient transaction instead of beginning their own.

## Held transaction

The factory also implements `IHeldTransactionProvider` and declares `write.heldTransaction`
([DATA-0166](../../../../docs/decisions/DATA-0166-a-scope-can-hold-a-native-transaction-from-its-start.md)): the same unit,
begun at the start of a scope and held across awaited application code. Three things differ. Holds of one database in a
process queue on an asynchronous semaphore, for at most the busy timeout, so a waiter blocks no thread and observes its
token; only the one at the head waits on the file, where another process may hold the lock, so a waiter that meets both
waits at most twice the busy timeout. Every flow that inherits the hold shares one connection, which is not safe for
concurrent use, so each operation, schema work included, takes a turn on it (`SqliteUnit.Enter`); a turn that does not come
within the busy timeout is refused, as one taken from inside another's callback never would. And a raw read runs on the
unit's connection under `query_only` for that one turn, so it sees what the hold wrote and still cannot write. A unit
ends at commit, at disposal, at `IHeldTransaction.Abort` (the hold's limit), or when the host stops, which aborts every
hold still open so that no write lock outlives the host. Once aborted it stays in the flow's ambient and refuses every
operation, so a write that raced the abort cannot fall through to a connection of its own. A table first used inside a
hold is created inside it and forgotten if the hold rolls back; until then another flow can see its readiness and fail on
the missing table, so first use belongs at boot on a route that holds. `IMutationOutcomeRepository` reads whether the row
exists inside the write's own immediate transaction, or the hold, so the answer cannot be overtaken. A batch, bulk save or bulk delete that joins a
unit runs in a savepoint of it, so a failure leaves nothing of that operation and the unit goes on. A held commit observes its token only
until the native commit begins. A named read on a lane keeps its own connection and sees committed state. An in-memory database has no WAL:
a reader on another connection waits for the hold.

## Lexical index

An entity's `[Lexical]` properties are served by one FTS5 external-content table per table
([DATA-0159](../../../../docs/decisions/DATA-0159-lexical-search-over-declared-entity-text.md)): a view
`<table>__koan_lexical_source` reads each declared value out of the document with the same `json_extract` a filter
emits, the virtual table `<table>__koan_lexical` (`content=` that view, `content_rowid=koan_rowid`, tokenizer
`porter unicode61` for `english`, `unicode61` for any other language) indexes it, and three triggers
(`<table>__koan_lexical_ai`, `_au`, `_ad`) keep it in step with every insert, update and delete of the row. The triggers
run inside the row's own statement, so the index entry lands in whatever write unit the row does — a single `Save`,
`UpsertMany`, a batch, or the ambient atomic commit — and a write that fails or rolls back takes its entry with it.
The structures are created on first use under the same DDL consent as a declared index; a set that no longer matches
the declaration is dropped and created again, empty, and readiness reports the index as not current until
`Rebuild` (`INSERT INTO <fts>(<fts>) VALUES ('rebuild')`) fills it. `Status` compares the count of `<fts>_docsize` with
the row count.

A search matches each term quoted, joined by `OR`, and joins the matches back to the row, where a scope (`Within`, a
managed read scope) applies before `ORDER BY bm25` and the page. Read as typed (`AsTyped`,
[DATA-0165](../../../../docs/decisions/DATA-0165-a-lexical-search-reads-a-word-being-typed-and-takes-a-scope.md)), the
term the text ends in carries FTS5's `*` outside its quotes. The `porter` tokenizer stems it before it compares, so a
partial word whose stem is not the start of the stored one finds nothing until it is whole: "runnin" against "running",
stored as "run". `unicode61` alone has no such gap. No `prefix=` index serves it, so a one-letter prefix walks its terms.

A search or a rebuild first confirms, once per table, that the schema holds the five objects; on a route whose DDL
policy did not build them it refuses naming the index, the objects and the policy correction, and `Status` answers
`Exists = false`. A search joins the virtual table to the row on rowid, matches every term as a quoted phrase joined by OR, orders by
`bm25` and then by identity, and pages with `LIMIT`/`OFFSET`; the score answered is the negated `bm25`, so higher ranks
first. A snippet is FTS5's `snippet()` over the view, at most 64 tokens. The caller's read scope (the tenant, the
not-yet-deleted predicate) is translated by the same filter translator and ANDed in.

## Connection posture

`SqliteConnections` applies the host posture (`SqliteOptions.JournalMode`, `BusyTimeout`, `Synchronous`) as each
connection it creates opens, through the connection's state change, so a Direct session or a read lane that opens the
connection itself gets it too. `busy_timeout` and `synchronous` are set on every connection, and `BusyTimeout` also sets
the connection's `DefaultTimeout`, because Microsoft.Data.Sqlite retries `SQLITE_BUSY` until its own command timeout
whatever the pragma says; `journal_mode` only on a writable file connection, since an in-memory database has no WAL and a read-only connection cannot change the file.
A database that refuses the requested mode is logged once with the mode it kept. The store-distinctness probe
(`ProveDistinct`) does not take the posture: it promises to write nothing, and setting the journal mode writes the
header. Readiness reads `PRAGMA journal_mode` on its non-creating probe and reports it per source.

## Connection ownership

File connections use Microsoft.Data.Sqlite pooling. The host records only the exact pool groups it uses and clears
them on disposal. Private and named memory targets receive source-isolated shared-memory identities plus one
host-owned keeper connection, so per-operation connections observe the same database for that host lifetime.
Connection and provenance output are redacted.

## Limits

- Atomic insertion requires an assigned non-default identity or a mapped Generated identity; a default
  non-generated key rejects before dispatch.
- Repository operations are buffered; Entity streaming is coordinated as bounded numbered pages.
- Offset paging is not snapshot isolation or a resumable cursor.
- Explicit mapped containers do not combine with ambient partitions or managed row fields.
- SQLite attached databases are not inferred from mapping namespaces.
- Provider exceptions surface directly; commands are not replayed after failure.
- A filter's `In`/`Nin`, and the ids of a one-column identity, bind as one JSON array matched through `json_each`, so
  the set meets no parameter limit. Each member is written as the value Microsoft.Data.Sqlite binds for it: integers
  and booleans as INTEGER, reals as REAL, and strings, Guids (upper-case), dates, `DateOnly`, decimals and chars as the
  TEXT the driver writes. A member JSON cannot carry (a byte array, a non-finite real, an unsigned 64-bit integer)
  binds one parameter per member: a filter whose statement then passes 32,766 parameters, less those the statement
  binds itself, is refused, naming its sets, and
  `GetMany`/`DeleteMany` over such keys, or over a composite identity, run in slices of 30,000 parameters and 500
  identity predicates (SQLite allows 32,766 variables and an expression depth of 1,000). The store declares
  `MaximumSetMembers` 30,000, the slice the facade reads a scoped key set in (DATA-0162).
- A batch has no size bound of its own: each queued operation is its own statement inside the batch's one
  transaction. `UpsertMany` writes at most 4,096 rows per multi-row statement, all inside its one transaction (DATA-0162).
- A lexical index is keyed by the row's rowid. A `VACUUM` of a table with a text primary key may renumber rows; run
  `Rebuild` after one. `Inspect()` lists the index's view and shadow tables beside the application's containers, and a
  `DELETE FROM` of a declaring table deletes row by row because the delete trigger runs.
- A host accepts at most 256 distinct in-memory source targets. A repository keeps at most 512 compiled entity plans,
  one per partition table; past that it drops the least recently used plan and compiles it again on that partition's
  next use, so a partition per customer or per day keeps working (NK-170).

## Enum storage contract

Default Entity storage preserves enum names as strings, including nullable values, nested values, collections,
and declared EnumMember aliases. Unnamed numeric values fail instead of silently changing the storage format.
Queries use the same spelling. Ordinary enum ordering uses declared ordinal ranks in native expressions while
the stored value stays a string; native ordering of arbitrary Flags combinations rejects correctively.
An explicit external mapping codec remains responsible for its declared physical representation.
Existing numeric rows or columns require a separate, explicit migration; upgrades do not rewrite them automatically.

Conditional replacement consumes the normalized Filter in one UPDATE with identity AND guard.
Readiness validates the mapped physical primary key before mutation; a nonunique mapped key cannot
silently update duplicate rows. Matching identical replacements report success. Guard mapping/value
qualification can follow advisory Entity BeforeUpsert; it still precedes the UPDATE.
