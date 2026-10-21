---
id: DATA-0131
slug: a-transaction-defers-or-refuses-every-write
domain: Data
status: Accepted
date: 2026-10-21
title: Inside EntityContext.Transaction every write is deferred in call order under its own context, or refused
related:
  - DATA-0078
  - DATA-0129
---

# DATA-0131: Inside EntityContext.Transaction every write is deferred in call order under its own context, or refused

## Context

`EntityContext.Transaction` is a deferred coordinator (DATA-0078): tracked writes run sequentially at `Commit`,
`Rollback` discards undispatched work. Its guarantees held for two verbs only (ledger NK-043, NK-044, NK-047, NK-012,
NK-088, NK-103):

- Only single `Upsert` and `Delete` enlisted. `UpsertMany`, `DeleteMany` (and so collection `Save`/`Remove` and
  `SaveReplacing`), `DeleteAll`, `RemoveAll`, `Patch`, `Batch().Save`, instructions, transfers and the untyped
  `Upsert(this object)` (what a Guid- or int-keyed `entity.Upsert()` binds to) ran immediately and survived `Rollback`.
  A rolled-back Move had already deleted its source rows.
- A tracked operation kept only source, adapter and partition. Tenant and axis carriers were read at commit, so a
  save issued under tenant A and committed under tenant B was stamped B.
- Operations sat in a dictionary keyed by adapter hint and ran group by group, not in call order.
- The cancellation check ran outside the per-operation `try`, so a cancelled commit reported
  `CompletedOperationCount = 0` after operations were durable.
- With `AutoCommitOnDispose`, `Dispose` swallowed every exception, including a failed commit's
  `TransactionException(Unknown)`. DATA-0078 still shows `AutoCommitOnDispose = true` as the default; the code default
  is `false`.

All of these were measured before this change (InMemory and Json for Data.Core, SQLite with Koan.Tenancy).

## Decision

1. **Bulk Save/Remove defer per item.** Inside the scope, `UpsertMany` and `DeleteMany` (both partition forms) track
   one save or delete per item, exactly as the single verbs do (source-policy demand, identity minted at enqueue). As
   with a deferred `Delete`, the returned count is the work tracked.
2. **Every other write refuses before it writes.** The facade's guard refuses any non-read operation while a
   coordinator is ambient, because a write that reaches the facade there is one Data could not defer. That covers
   `DeleteAll`, `RemoveAll`, `Patch`, batches, instructions (schema instructions excepted) and writes issued by
   extensions or lifecycle code. Transfers refuse at admission, before their first read. The refusal names the
   transaction and the deferrable alternatives.
3. **`Upsert(this object)` goes through `Data<,>.Upsert`**, so it defers and keeps every Data rule. Its signature is
   unchanged.
4. **A tracked operation carries the context it was issued in.** At enqueue the coordinator captures every registered
   context carrier (tenant, data axes and other ambient scopes); commit restores exactly that set, then the operation's
   routing, both for horizon binding and for execution.
5. **Commit runs in call order** from one list; the adapter set is derived from it. Cancellation is observed inside
   the per-operation `try`, so `CompletedOperationCount` is the number of durable operations.
6. **A failed auto-commit surfaces.** With `AutoCommitOnDispose`, `Dispose` lets the `TransactionException` escape
   (the routing scope is still left). `Dispose` cannot tell a clean exit from an exceptional one, so auto-commit also
   dispatches when an exception leaves the block; that is now documented instead of promised otherwise. The commit and
   operation awaits use `ConfigureAwait(false)`, which the blocking dispose path needs. The option's default stays
   `false`; this record supersedes DATA-0078's `true` sample.

## Compatibility

- Code that called `DeleteAll`, `RemoveAll`, `Patch`, `Batch().Save`, an instruction or a transfer inside a
  transaction now gets `NotSupportedException` or `InvalidOperationException`; before, the write was durable at once.
  Move it outside the scope, or express it as deferrable Save/Remove.
- Collection `Save`/`Remove` inside a transaction now wait for `Commit` and are discarded by `Rollback`.
- A deferred write issued under one tenant or axis value commits under that value whatever the commit ambient; work
  issued with no tenant now meets the missing-tenant refusal at commit even if the commit runs under a tenant.
- Commit order across adapters changes to call order.
- `AutoCommitOnDispose = true`: a failed commit now throws from `Dispose`.
- No public signature or stored format changes.

## Evidence

- `TransactionDeferralSpec` (Koan.Tests.Data.Core): collection Save/Remove are discarded by Rollback; commit order
  across InMemory and Json equals call order; DeleteAll, RemoveAll, Patch, Batch, a Clear instruction and Copy refuse
  and leave the store unchanged; an untyped Guid-keyed upsert defers; a commit cancelled after two of three operations
  reports 2; auto-commit with a failing operation throws `TransactionException(Unknown)` from `Dispose`.
- `AssertNoTenantLeakSpec` (Koan.Tenancy.Tests, SQLite): a save issued under acme and committed under globex is
  visible to acme and not to globex.
- All twelve failed before this change.
