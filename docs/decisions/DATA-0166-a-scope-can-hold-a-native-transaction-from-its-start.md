---
id: DATA-0166
slug: a-scope-can-hold-a-native-transaction-from-its-start
domain: Data
status: Accepted
date: 2026-11-01
title: A scope can hold a native transaction from its start, on a route that declares it
related:
  - DATA-0078
  - DATA-0131
  - DATA-0158
  - DATA-0161
amended_by:
  - SEC-0036
  - ARCH-0142
---

# DATA-0166: A scope can hold a native transaction from its start, on a route that declares it

> **Amended by [SEC-0036](SEC-0036-a-role-change-joins-a-hold.md) (2026-11-01): a role change joins a hold, and is a short hold
> of its own outside any scope.** Everything else stands, the refusal of a transfer in rule 5 included. Work registered with
> `AfterCommit` runs outside the scope. The first-use note under Compatibility goes further than it says: a hold that uses a table
> another flow is provisioning waits for that flow, which is waiting for the hold's store, until the hold's limit. `Koan.Identity`
> makes its own tables exist at boot for that reason; for any other table first use still belongs at boot.

> **Amended by [ARCH-0142](ARCH-0142-the-realignment-finished.md) (2026-11-02): an entity on a default route that holds has its table at start,** and an atomic commit's writes are uncommitted to a shared cache as a hold's are.

Amends [DATA-0131](DATA-0131-a-transaction-defers-or-refuses-every-write.md) and
[DATA-0158](DATA-0158-sqlite-posture-and-atomic-commit.md) for a scope that asks. Whoever does not ask keeps both unchanged.

## Context

`EntityContext.Transaction` only records writes. The native transaction, where there is one, opens at `Commit`. So code
inside a scope cannot read its own writes, a read can be overtaken before the commit, `SaveWithOutcome` refuses, and nothing
serializes two scopes. A consumer that decides from what it reads has to serialize by itself, in one process, with a queue.

The question was whether a scope can soundly hold the native transaction from its start. The earlier records chose
otherwise for reasons that had to be answered first:

- **DATA-0078** deferred because it was adapter-agnostic: most stores offer no native transaction to hold. That stands, and
  this record does not change it: holding is a capability a route declares.
- **DATA-0131** made every write defer or refuse because a write that ran at once could not be undone: `Rollback` only
  discards what has not run. A held transaction removes that reason on its route, since a native rollback undoes whatever
  ran on its connection. The reason stands everywhere else, and for writes that cannot reach that connection.
- **DATA-0158** provisions before the transaction opens for two reasons. A table created inside it rolls back while readiness
  remembers it, which its rule 5 already answers. A second connection cannot create a table while the transaction holds the
  lock, which schema work on the unit's own connection already answers.

So none of the reasons forbids it. Holding across awaited application code raises four conditions of its own, and this
record is sound only with all four.

1. **One connection, one operation at a time.** The flows that inherit a hold share one connection, and `Task.WhenAll` or
   `Task.Run` inside a scope is ordinary code. Without turns, forty concurrent saves crash the driver with an
   `ArgumentOutOfRangeException` from inside its command list.
2. **A bound on the hold.** A write lock held by a scope that never ends stops every writer of the file, and a flow can die
   without disposing anything. Nothing bounded a scope: `TransactionOptions.DefaultTimeout` is read by no code.
3. **A waiter blocks no thread.** The driver's busy wait is synchronous. A holder's continuation needs the thread pool, so
   waiters that occupy the pool slow down the very hold they wait for. And a synchronous wait cannot observe a token: a
   cancelled wait lasted 21 seconds.
4. **The shared cache must not see uncommitted rows.** `CachedRepository` answers a read before the store does and seeds the
   shared cache from a write. Left alone, a read inside the hold returns the stale entry instead of its own write, and a
   rollback leaves a phantom other flows then read.

A fifth surfaced while proving the others. A Direct session opens a connection of its own, with its own command timeout, so a
Direct write inside a hold waited out the hold's limit on the lock its own flow held, killed the hold, and then landed
durably outside it.

## Decision

1. **`await EntityContext.Hold(name, ct)` holds one native transaction from the start of the scope.** It takes the route the
   ambient context names: its source, its adapter, or the default. It is a verb and not a third `TransactionGuarantee`,
   because `TransactionGuarantee` says what a scope promises at commit, and a hold promises exactly what `Atomic` promises. A
   hold reports `Guarantee == Atomic`, `IsHeld == true` and `TxCaps.Local`, and not `TxCaps.DeferredCoordination`. The two
   stay distinct because they trade differently: `Atomic` keeps other writers out only while the commit runs and its body is
   blind, while a hold's body sees its own writes and keeps other writers out for all of it. The verb is awaitable because the
   wait can last the busy timeout. The ambient context enters as the await completes, in the caller's flow, so a hold that
   could not begin leaves nothing behind.
2. **A route declares it.** `DataCaps.Write.HeldTransaction` (`write.heldTransaction`, profile "Held transaction") is a separate
   token from `write.atomicCommit`, because it promises more: every read and write of the flow, across awaited code, and writers
   that serialize. A store can commit atomically without promising that. The factory that declares it implements
   `IHeldTransactionProvider`. SQLite declares it; no other connector does, and `EntityContext.Hold` refuses them with a
   `NotSupportedException` naming the route, the token and the corrections, before anything is held, rather than quietly deferring.
3. **Inside a hold, nothing defers.** Save, Remove, `Patch`, batches, conditional writes, `Insert`, mass deletes and SQL
   instructions run at once on the hold's connection, and its rollback undoes them. A read sees the scope's own writes, a raw read
   included, which takes its turn on the connection under `query_only` for that turn, so it still cannot write. A lifecycle handler
   runs at the save and what it writes joins the hold. A unique index answers at the save and costs the operation, not the
   transaction, so the scope can go on. An operation of several statements, a batch or a bulk save or delete, runs in a savepoint of
   the transaction, so a failure leaves nothing of that operation and the hold goes on; its `Atomic` receipt stays true, and because
   no row of it landed the cache has nothing to follow. `TrackedOperationCount` stays zero.
4. **A receipt reports what is true.** `SaveWithOutcome`, `RemoveWithOutcome` and `Insert` report `Inserted`, `Updated` or `Deleted`
   with `DataCommitOutcome.NotCommitted`, and a batch reports `NotCommitted`, because only `Commit` commits. `Commit` reports as
   DATA-0158 and DATA-0161 do: `Unknown` when the native commit fails, `NotCommitted` when the hold had already ended or the commit
   was cancelled before it began. A held commit observes its token only until then, because a native commit cannot be abandoned
   part way. To answer the insert or the update at all, SQLite now declares `write.mutationOutcomes`, reading whether the row exists
   inside the write's own immediate transaction; this works outside a hold too, and on a cached entity, whose entry the outcome
   retires as an insertion's does.
5. **One hold covers one store.** A write routed elsewhere refuses when it is issued, naming the route and the correction. A read
   elsewhere sees that store's committed state. A write through another source naming the same file joins, by the store identity
   DATA-0158 keys units with. A transfer and a role change still refuse. Every vector write refuses at `ScopedVectorRepository`, the
   chokepoint every path to a vector adapter passes, because a vector store cannot join and the write would survive the rollback; a
   vector read goes on. A Direct write refuses at once, on any route, because its connection cannot join; a declared Direct read,
   and a named read on a lane, keep their own connections and see committed state.
6. **Writers wait, in order, for a bounded time.** On SQLite the hold is `BEGIN IMMEDIATE`, so a writer in another process waits on the
   store's busy timeout. Within a process, holds of one database queue on an asynchronous semaphore for at most that timeout, so a waiter
   costs no thread and observes its token; only the one at the head waits on the file. A waiter that meets both waits at most twice the
   busy timeout. A hold that waits it out throws `TransactionException` with `NotCommitted` and the store's busy error inside: nothing was
   written. A writer that is not a hold meets the busy timeout as it always did, and reads go on throughout, under WAL.
7. **A hold is bounded.** Past `TransactionOptions.HoldLimit` (`Koan:Data:Transactions:HoldLimit`, 20 seconds, below SQLite's 30-second
   busy timeout so that a waiter outlives an abandoned hold) it rolls back, frees the store and refuses the rest of its scope: every
   later operation, a read included, because a read would otherwise run outside the transaction the scope believes it is in. The commit
   reports `NotCommitted`. The refusal stays until the scope is disposed, so a write that raced the limit cannot fall through to a
   connection of its own. A host that stops aborts its open holds; a process that dies loses them, as SQLite does for any open transaction.
8. **A hold counts as an active mutation of its route** (`DataOperationHorizon.AdmitHold`), so a maintenance window waits for it, bounded by
   its drain timeout. It enters no ambient horizon, so `Patch` and the other verbs that declare their own still can.
9. **The Entity cache reads past itself inside a hold and retires its entries after the commit**, through
   `ITransactionCoordinator.AfterCommit`. Until then every other flow is right to read the committed value an entry holds.

## Compatibility

- Additive. `EntityContext.Transaction` and `TransactionGuarantee.Atomic` behave as they did, on every route.
- `ITransactionCoordinator` gains `IsHeld` and `AfterCommit`; `ITransactionCoordinatorFactory` gains `CreateHeld`;
  `TransactionOptions` gains `HoldLimit`; `TransactionHold` and `IHeldTransactionProvider` are new. No stored format changes.
- On SQLite, `SaveWithOutcome` now answers outside a scope, where it refused as unsupported.
- A SQLite batch, bulk save or bulk delete that joins any ambient transaction now runs in a savepoint, so a lifecycle handler that catches
  such a failure during an atomic commit also finds nothing of it left. Nothing else about an atomic commit changes.
- A hold keeps every other writer of the file out for as long as it lasts. That is what it asks for; its cost is the application's.
- A table first used inside a hold is created inside it. If the hold rolls back, its readiness is forgotten (DATA-0158 rule 5), but until
  then another flow can see that readiness and fail on the missing table; and a flow provisioning that table on its own connection can
  time out on the hold's lock. First use belongs at boot on a route that holds.
- Awaiting `Hold` in a helper method loses the scope when the helper returns, as for any ambient context. The transaction then ends at its limit.

## Evidence

- `SqliteHeldTransactionSpec` (Connector.Sqlite, 39 specs). **Two real processes on one file**, the test assembly started again through
  `AddKoan()`: 25 read-increment-commit holds from each of two processes lose no update on one counter; a hold here waits for a hold
  there and then reads what it committed; a waiter on another process's hold, a hold and a plain save alike, fails within the configured
  busy timeout with nothing landed; a process killed mid-hold leaves nothing and frees the store. **The flow**: a read sees its own write
  while another connection does not; disposing without a commit and throwing both leave nothing; a batch, a conditional replace and delete,
  a SQL instruction and `DeleteAll` all roll back; outcomes report as they happen and `NotCommitted`; a unique conflict costs the statement;
  a failed batch, and a failed bulk save across statements, leave nothing of themselves while the hold goes on and commits; a handler's write joins and rolls back; a raw read sees its own write and a mislabelled write still fails at the engine; a stream's body
  may write; forty concurrent operations take turns; a read lane and a Direct read see committed state and a Direct write refuses at once.
  **The commit**: one cancelled before it begins reports `NotCommitted`, and a deferred foreign key the store refuses at `COMMIT` reports
  `Unknown`, and both roll back. **The bounds**: a hold past its limit frees the store and refuses reads, writes and the commit; an abandoned hold ends and the writer behind
  it goes on; a stopped host leaves no lock; a cancelled wait ends in well under the busy timeout; `8 x ProcessorCount` holds serialize without
  starving their continuations; a failed `Hold` leaves nothing ambient.
- `RoleSqliteSpec` (Koan.Identity.Sqlite.Tests): a role change inside a hold refuses and writes nothing.
- `HeldVectorSpec` (Koan.Data.Axes.Integration.Tests, SQLite with InMemoryVector): every vector write refuses, through `Vector<T>` and the
  repository alike, while a vector read goes on, and nothing reached the vector store.
- `TransactionGuaranteeSpec` (Koan.Tests.Data.Core): a hold on the JSON and InMemory routes refuses naming `write.heldTransaction` and the
  correction, and the flow goes on.
- `CacheHoldSpec` (Koan.Tenancy.Tests, SQLite with Koan.Cache): a hit does not answer for the hold's write, another flow reads the committed value,
  the commit retires the entry, and a rollback leaves no phantom; a save reports its outcome on a cached entity, in and out of a hold, and the
  entry follows it; a failed batch leaves store and cache agreeing, and a successful one retires its entries at the commit.
- Three conditions were measured by removing them: without turns the driver throws `ArgumentOutOfRangeException` from its command list, without
  the in-process queue a cancelled wait lasted 21 seconds, and without the cache's awareness both `CacheHoldSpec` specs fail. What pool exhaustion
  would cost without the queue was reasoned and not measured separately.
