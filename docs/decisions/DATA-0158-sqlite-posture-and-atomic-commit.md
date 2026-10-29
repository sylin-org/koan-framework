---
id: DATA-0158
slug: sqlite-posture-and-atomic-commit
domain: Data
status: Accepted
date: 2026-10-29
title: SQLite carries a declared posture on every connection, and a transaction scope on one atomic route commits natively
related:
  - DATA-0007
  - DATA-0078
  - DATA-0131
  - ARCH-0084
---

# DATA-0158: SQLite carries a declared posture on every connection, and a transaction scope on one atomic route commits natively

Amends [DATA-0131](DATA-0131-a-transaction-defers-or-refuses-every-write.md) and
[DATA-0078](DATA-0078-ambient-transaction-coordination.md).

## Context

Measured at `c87c30ed2` by a consumer building a single-writer application on the connector:

- A fresh file database opened with a rollback journal (`PRAGMA journal_mode` answered `delete`, header bytes 18–19
  were `1 1`). The connector set no journal mode, busy timeout or synchronous level; the only pragmas it issued were
  `query_only` on the read lane, `table_info` and `quick_check`. The busy timeout in effect was whatever the driver
  defaulted to, and nothing reported the mode in effect.
- `EntityContext.Transaction` dispatched its tracked operations at `Commit` one autocommit statement at a time
  (DATA-0131). A worker that saved two entity types and failed on the second row left the first durable; the
  coordinator's own comment said so. Only `UpsertMany`, `DeleteMany` and `Batch().Save` opened a SQLite transaction,
  and each covered one entity type. A consumer that needs one commit per command across entity types could not get
  it, and could not ask for it either: a scope that silently fell back to sequential dispatch looked the same as one
  that did not.

## Decision

1. **The connector owns a posture and applies it on every connection it opens.** `SqliteOptions` gains
   `JournalMode` (default `Wal`), `BusyTimeout` (default 30 seconds, the driver's own; it sets SQLite's `busy_timeout` and the driver's
   command timeout, since Microsoft.Data.Sqlite retries a busy lock until the latter whatever the pragma says) and
   `Synchronous` (default `Full`, SQLite's own; `Normal` is the throughput choice under WAL). They bind from `Koan:Data:Sqlite:*` and the
   Default-source provider keys `Koan:Data:Sources:Default:sqlite:*`, as the connector's other settings do, and a
   value that is not one of the choices refuses at boot naming the key and the choices. `SqliteConnections`, the one
   owner of every connection, sets the pragmas as each connection opens, whoever opens it: a repository, the schema
   executor, a read lane, a Direct session, readiness. The journal mode is set only on a writable file connection
   (SQLite stores it in the file); an in-memory database and a read-only connection keep the mode they have. The
   startup facts publish the three settings with their source, and readiness reports them beside the journal mode
   each probed source's database actually holds.
2. **An adapter may declare an atomic commit.** `DataCaps.Write.AtomicCommit` (`write.atomicCommit`, profile
   "Atomic commit") says the provider can run every record write a deferred coordination scope dispatches to one of
   its sources inside one native transaction, across entity types. The factory that declares it implements
   `IAtomicCommitProvider`: `BeginAtomicCommit(services, source, ct)` returns a scope that is committed or, on
   disposal without a commit, rolled back. SQLite declares it: the scope is `BEGIN IMMEDIATE` on a dedicated unpooled
   connection, made ambient for the calling flow before the first await, which every repository read and write,
   every schema statement and every lifecycle read of that route then shares until the scope ends. Raw reads keep
   their own `query_only` connection and see committed state only.
3. **The coordinator negotiates.** One route is one physical store: the `ConnectionIdentity` of the source plan,
   which the adapter mints from the store it opens (an anchored, case-folded file path; one in-memory database per
   source), so two sources that name one file two ways are one route, and the SQLite unit is keyed by that same
   identity so both find it. At `Commit`, when every tracked operation resolves to one route whose provider is
   an `IAtomicCommitProvider`, the coordinator first provisions each operation's store outside any transaction, then
   begins the native transaction, dispatches in call order inside it and commits it once. A failure on any operation
   rolls the transaction back and reports `TransactionException` with `DataCommitOutcome.NotCommitted` and
   `CompletedOperationCount = 0`; a failure of the native commit itself reports `Unknown`. A scope whose operations
   span two routes, reach a provider without the token or include a vector operation keeps DATA-0131's sequential
   dispatch unchanged.
4. **A consumer can require it.** `EntityContext.Transaction(name, TransactionGuarantee.Atomic)` opens a scope whose
   commit is one native transaction or nothing. The first tracked operation that cannot join one — a provider without
   `write.atomicCommit`, a second route, a vector operation — is refused with a `NotSupportedException` that names the
   route, the token and the two corrections (route the scope to a provider that declares it, or use
   `TransactionGuarantee.Coordinated`), before anything is held. Such a scope publishes `TxCaps.Local`; a coordinated
   scope does not, even when its commit happens to run natively. `TransactionGuarantee.Coordinated` is the default
   and the behaviour of the one-argument `Transaction(name)`.
5. **Schema provisioned inside the native transaction is forgotten with it.** Provisioning normally runs before the
   transaction opens (3). Where a write the scope did not track still provisions inside it, the SQLite unit records the
   target and, on rollback, invalidates its readiness, so the next use provisions it again instead of writing to a
   table the rollback removed.

## Compatibility

- A file database the connector opens for writing becomes WAL on first open, and stays WAL for every other process
  that opens it. `Koan:Data:Sqlite:JournalMode=Delete` restores the previous mode. A database that cannot take the
  requested mode (a network file system) logs a warning once with the mode it kept.
- On one SQLite route a coordinated scope now rolls everything back when a later operation fails. Code that relied on
  the durable prefix on SQLite no longer gets one; the sequential contract is unchanged on every other route.
- `ITransactionCoordinator` gains `Guarantee`; `ITransactionCoordinatorFactory.Create` gains a defaulted `guarantee`
  parameter. `IAtomicCommitProvider` is a new, optional adapter facet. No stored format changes.
- `AfterUpsert` lifecycle handlers of a tracked operation run when the operation is dispatched inside the native
  transaction, before its commit; a failed commit does not undo what they did.

## Parity on other connectors

A relational connector that already opens native transactions (PostgreSQL, SQL Server, CockroachDB, DuckDB) earns the
token by declaring it, implementing `IAtomicCommitProvider` over a dedicated connection held ambient per route, and
routing its repository's connection acquisition, schema executor and lifecycle reads through that ambient unit, as
the SQLite connector does in `SqliteConnections`, `SqliteRepository.Open` and `SqliteDdlExecutor`. Document and
in-memory stores without a multi-statement native transaction keep sequential dispatch and refuse an Atomic scope.

## Evidence

- `SqlitePostureSpec` (Connector.Sqlite): a fresh file database has header bytes `2 2` and reports `wal`; a configured
  `Delete` yields `1 1`; a Direct session reads the configured busy timeout and synchronous level; a write against a
  lock another connection holds fails in about the configured 2 seconds, not the driver's 30; 200 simultaneous saves
  all succeed; a misspelt mode refuses at boot naming the key and the choices; readiness reports the
  posture and the mode each source holds; the startup facts carry the three settings with their source.
- `SqliteAtomicCommitSpec` (Connector.Sqlite): an Atomic scope commits two entity types on a fresh partition; one
  that throws between two saves persists neither; one whose second dispatched row fails leaves the first not durable
  and reports `NotCommitted` with zero completed operations; a coordinated scope on one SQLite route does the same; an
  update and a remove of different types commit together; two sources that spell one file with different directory
  casing commit as one route; an Atomic scope publishes `TxCaps.Local`.
- `TransactionGuaranteeSpec` (Koan.Tests.Data.Core): an Atomic scope on the JSON and InMemory routes refuses the
  first save naming `write.atomicCommit` and the correction, holding nothing; an Atomic scope that reaches a second
  SQLite route refuses it; a coordinated scope on JSON still dispatches sequentially.
- The three Data.Core specs that prove the sequential contract (`Failed_commit_reports_and_preserves_the_completed_prefix_without_replay`,
  `Cancelled_commit_reports_the_operations_already_durable`, `Auto_commit_on_dispose_reports_a_failed_commit`) now
  pin the JSON route, because the suite's default route is SQLite and commits atomically.
