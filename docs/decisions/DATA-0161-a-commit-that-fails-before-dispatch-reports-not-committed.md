---
id: DATA-0161
slug: a-commit-that-fails-before-dispatch-reports-not-committed
domain: Data
status: Accepted
date: 2026-10-29
title: A deferred commit that fails before it dispatches anything reports NotCommitted
related:
  - DATA-0078
  - DATA-0122
  - DATA-0131
  - DATA-0158
---

# DATA-0161: A deferred commit that fails before it dispatches anything reports NotCommitted

Amends [DATA-0158](DATA-0158-sqlite-posture-and-atomic-commit.md) rule 3, which named the outcome of a failed operation
inside the native transaction (`NotCommitted`) and of a failed native commit (`Unknown`) but not of a failure before the
native transaction opens.

## Context

Measured at `943e6358a`. `TransactionCoordinator.Commit` reported every failure that was not already a
`TransactionException` or a refusal as `TransactionException` with `DataCommitOutcome.Unknown` ("Its commit outcome is
unknown"). That covered failures that happen before anything the scope tracked is dispatched, when nothing can have
landed:

- on an atomic route, provisioning each operation's store, which DATA-0158 runs outside any transaction before the
  native one begins, and beginning the native transaction itself (SQLite's `BEGIN IMMEDIATE` waiting out the busy
  timeout behind another writer);
- on either path, resolving each operation's route and entering the route horizon.

A consumer whose writer treats `Unknown` as "some of this may be durable" recovers by reloading everything it keeps in
step with the store. A container that could not be provisioned (the partition refusal DATA-0160 removes was one) cost
it that reload for a commit that had written nothing.

## Decision

1. **Before dispatch, nothing is durable.** A failure before the first tracked operation is dispatched reports
   `TransactionException` with `DataCommitOutcome.NotCommitted` and `CompletedOperationCount = 0`, saying the scope
   failed before any tracked operation was dispatched and none of them is durable. On an atomic route dispatch begins
   once the native transaction has begun, so a failure provisioning a store or beginning the transaction is
   `NotCommitted`; on a sequential route it begins with the first operation, which provisions its own store, so a
   provisioning failure there is a failure of dispatch and reports `Unknown` with no completed operation (rule 2).
   Schema the provisioning created stays, as it does whenever provisioning succeeds; no record does.
2. **Everything after it keeps its outcome.** A failed operation inside the native transaction is `NotCommitted` (the
   transaction rolls back); a failure of the native commit itself is `Unknown`, because the store may have committed; a
   failure during sequential dispatch is `Unknown` with the count of operations already durable. Retry and replay
   dispositions stay `Never`: the coordinator replays nothing, and whether to issue the scope again is the caller's
   decision.
4. **A store's `NotSupportedException` before dispatch is a failure before dispatch.** A store that refuses an
   operation while it is provisioned (an explicit SQLite map, which pins one container, under an ambient partition) or
   while the native transaction begins has dispatched nothing, so the commit reports `TransactionException` with
   `NotCommitted` and the refusal as its inner exception, like any other failure before dispatch. Letting it escape
   bare gave the caller an exception without an outcome, which a writer must treat as "some of this may be durable".
   Only the scope's own refusal of its guarantee — an Atomic scope whose tracked operations no longer resolve to one
   route that commits atomically (DATA-0158 rule 4) — escapes as `NotSupportedException`: it is the corrective
   refusal the same scope gives at tracking time, which names the correction in the caller's code, and a caller
   branches on one type for it wherever it is raised; it too is raised before anything is dispatched.
3. **`NotCommitted`, not `NotDispatched`.** The exception reports the scope's commit, and `NotCommitted` is the outcome
   DATA-0158 already gives a scope that leaves nothing durable, so a caller branches on one value for "nothing landed"
   whichever step failed.

## Compatibility

- Behavioural: code that reads `TransactionException.CommitOutcome` sees `NotCommitted` instead of `Unknown` for a
  commit that failed before dispatch, and the message says so. Every other outcome is unchanged.
- Behavioural (rule 4, K8): a `NotSupportedException` a store raises while an atomic commit provisions or begins now
  arrives as the inner exception of a `TransactionException` with `NotCommitted`; it used to escape as itself.
- No public signature or stored format changes.

## Evidence

`SqliteAtomicCommitSpec` (Connector.Sqlite): an Atomic scope whose second operation's table cannot be provisioned (its
declared index name is held by another entity's table) reports `NotCommitted` with zero completed operations, the
provisioning refusal as its inner exception, and the first operation's row absent; an Atomic scope whose native
transaction cannot begin because another connection holds the write lock past a one-second busy timeout reports
`NotCommitted` and the row keeps its committed value; a scope whose native commit fails (its token is cancelled by the
last dispatched row) still reports `Unknown`. The first two failed before this change; the third passed before and
after.

Rule 4 (K8): `SqliteAtomicCommitSpec.A_store_refusing_to_provision_before_dispatch_reports_not_committed_with_its_refusal_inside`
— an Atomic scope that saves an ordinary row and then a row of an entity explicitly mapped to one SQLite container, under
an ambient partition, reports `NotCommitted` with zero completed operations, the store's `NotSupportedException` as its
inner exception and the first row absent. Before the change the `NotSupportedException` escaped the commit as itself.
`TransactionGuaranteeSpec` still sees the scope's own refusals as `NotSupportedException`.
