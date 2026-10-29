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
   `NotCommitted`; on a sequential route it begins with the first operation. Schema the provisioning created stays, as
   it does whenever provisioning succeeds; no record does.
2. **Everything after it keeps its outcome.** A failed operation inside the native transaction is `NotCommitted` (the
   transaction rolls back); a failure of the native commit itself is `Unknown`, because the store may have committed; a
   failure during sequential dispatch is `Unknown` with the count of operations already durable. A refusal
   (`NotSupportedException`, DATA-0158 rule 4) still escapes as itself. Retry and replay dispositions stay `Never`: the
   coordinator replays nothing, and whether to issue the scope again is the caller's decision.
3. **`NotCommitted`, not `NotDispatched`.** The exception reports the scope's commit, and `NotCommitted` is the outcome
   DATA-0158 already gives a scope that leaves nothing durable, so a caller branches on one value for "nothing landed"
   whichever step failed.

## Compatibility

- Behavioural: code that reads `TransactionException.CommitOutcome` sees `NotCommitted` instead of `Unknown` for a
  commit that failed before dispatch, and the message says so. Every other outcome is unchanged.
- No public signature or stored format changes.

## Evidence

`SqliteAtomicCommitSpec` (Connector.Sqlite): an Atomic scope whose second operation's table cannot be provisioned (its
declared index name is held by another entity's table) reports `NotCommitted` with zero completed operations, the
provisioning refusal as its inner exception, and the first operation's row absent; an Atomic scope whose native
transaction cannot begin because another connection holds the write lock past a one-second busy timeout reports
`NotCommitted` and the row keeps its committed value; a scope whose native commit fails (its token is cancelled by the
last dispatched row) still reports `Unknown`. The first two failed before this change; the third passed before and
after.
