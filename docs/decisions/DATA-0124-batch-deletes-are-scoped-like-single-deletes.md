---
id: DATA-0124
slug: batch-deletes-are-scoped-like-single-deletes
domain: Data
status: Accepted
date: 2026-10-21
title: A batch delete is scoped exactly like a single delete
related:
  - DATA-0007
  - DATA-0105
  - DATA-0106
  - ARCH-0101
  - ARCH-0102
  - ARCH-0103
---

# DATA-0124: A batch delete is scoped exactly like a single delete

## Context

Adapter deletes match on identity alone: `IDataRepository.Delete(TKey)` and `IBatchSet.Delete(TKey)` carry no predicate.
Delete isolation is therefore the facade's job. The single `Delete` and `DeleteMany` read the requested ids through the
active managed and read scope first (tenant segmentation, managed fields, read-filter contributors such as soft delete
and DataAxis predicates) and delete only what that read returned.

`RepositoryFacade.BatchFacade.Save` did not. Without a remove Lifecycle it handed every queued id to the native batch.
With one, it handed over only the ids it could read, unless it could read none of them, in which case it fell back to
the raw ids and skipped `BeforeRemove`. The managed write scope it entered around the native save is honoured by upserts
only. So under `Tenant.Use("acme")`, `Note.Batch().Delete(globexId).Save()` deleted globex's row and reported it
`Applied`. Measured before this change, on SQLite with Koan.Tenancy, and with the generic managed-field oracle on SQLite,
InMemory and Json: the foreign row was deleted every time. A non-equality read-filter axis (a moderation status) let a
viewer delete a row it could not see.

The same path mis-remapped complete item outcomes. When a remove Lifecycle shrank the dispatched deletes, the remap still
indexed native outcomes by queued position, so a batch holding an invisible or missing id threw
`IndexOutOfRangeException` after the native batch had committed, and `AfterRemove` never ran.

ARCH-0102 named the gap; ARCH-0103 dismissed it as "the facade is the gate". The facade was the gate, and this verb went
around it. Nothing caught it because the isolation proofs (`DataAxis.AssertNoLeak`, the adapter-surface
`ManagedFieldNoLeak` oracle) had no batch leg.

## Decision

**A batch delete removes exactly the rows the single scoped `Delete` would remove, and reports the others as `Missing`.**

1. **Visibility before dispatch.** With a remove Lifecycle, the batch reads each queued id in scope and dispatches only
   the ids `BeforeRemove` ran for. There is no fallback to the caller's ids. Without one, the batch resolves the visible
   subset of the queued ids with one scoped query, the lowering `DeleteMany` uses (`VisibleIds`), and dispatches only
   those. A soft delete is still lowered per id through the canonical remove path, which scopes each id itself.
   Unscoped entities take no extra read.
2. **One outcome vocabulary.** An id outside the scope is never dispatched and reports `Missing`, the outcome a native
   batch reports for an id that does not exist. The single `Delete` returns `false` for both, so neither path tells a
   caller whether a foreign row exists. `Deleted` counts native deletes only.
3. **Outcomes follow the builder, always.** Each queued delete records its native position, or "not dispatched". Complete
   item outcomes are validated in native positions, then remapped through that slot, so an invisible or missing id can
   neither crash the remap nor shift another item's outcome. A batch whose every operation was out of scope dispatches
   nothing, and its empty receipt is complete.
4. **`AfterRemove` follows the delete.** When the native batch reports complete outcomes, `AfterRemove` runs for each
   dispatched delete it reports `Applied`, not for one it reports `Missing` (removed concurrently). This matches the
   single `Delete`, which completes only when a row was removed. Without complete outcomes there is no per-item fact,
   and every dispatched delete completes, as before.
5. **Fail closed the same way.** A scoped batch delete on an adapter that cannot push the scope predicate refuses, as the
   single `Delete` does, instead of deleting by id.

## Why the facade, not the adapters

The proposal to put the scope into each native batch delete (SQLite and PostgreSQL can delete `WHERE id = @id AND
<scope>`) was considered and deferred:

- The only scope an adapter receives today is `ManagedFieldWriteScope`, which carries managed equality values. It does
  not carry read-filter contributor predicates. Soft delete, DataAxis predicates, and access row filters would still
  depend on the facade, so the facade guard is needed for every adapter anyway.
- Honouring it in two relational adapters would make an isolation guarantee depend on the adapter, when isolation is a
  required guarantee the facade owns, and would leave Mongo, the key-value stores, Json, DuckDB and CouchDB on a
  different footing from SQLite.
- The single `Delete` and `DeleteMany` are facade check-then-delete. The batch now has exactly their guarantee, which is
  what this record promises.

The residual is shared by every keyed delete: check-then-delete is not atomic. A managed stamp cannot move between
scopes, because the guarded upsert rejects a cross-scope write, but a predicate axis value can change between the read
and the delete. Closing that is a contract change for all keyed deletes (single, many and batch): pass the compiled
scope to the adapter as an explicit argument, as `ScopedVectorRepository` already passes `VectorScope`, so stores
delete with the scope in the native predicate. That is follow-up work, not part of this fix.

## Guarantee and correction

- `Entity.Batch().Delete(id)` and `Data<T,K>.Batch().Delete(id)` cannot delete a row outside the caller's tenant,
  managed field, DataAxis, soft-delete or other read scope, on any adapter.
- A caller learns which deletes applied from `Deleted` and, where the adapter earns complete outcomes, from per-item
  `Applied` / `Missing` in builder order.
- `RequireAtomic` still means the native batch commits its dispatched writes together. Out-of-scope ids are not
  writes.

## Concepts

None. The change uses existing vocabulary: `BatchItemOutcome.Missing`, the read scope, and the remove Lifecycle.

## Evidence

- `DataAxis.AssertNoLeak` gains a scoped batch-delete leg: a batch over one own id and one foreign id deletes one row,
  reports the foreign id `Missing`, and leaves the other context's row intact. Koan.Tenancy's `AssertNoTenantLeakSpec`
  (SQLite) and the region-axis `AssertNoLeakSpec` run it. Both failed before the fix with "deleted 2 rows".
- The adapter-surface `ManagedFieldNoLeak` oracle gains the same leg. It failed before the fix on SQLite, InMemory and
  Json, and passes after. PostgreSQL, MongoDB, Redis and SQL Server run the same oracle; they were not executed for this
  record because no container runtime was reachable.
- `ReadFilterContributorSpec` (SQLite): a non-equality moderation predicate scopes batch delete, and a remove-Lifecycle
  batch over hidden, missing and visible ids deletes only the visible row, reports `[Missing, Missing, Applied]`, and
  runs `BeforeRemove` and `AfterRemove` once. A batch of only hidden ids dispatches nothing and runs no handler.
- `EntityExecutionSemanticsSpec`: the remap with an invisible id reports `Missing` in builder order instead of throwing
  `IndexOutOfRangeException`; a Lifecycle batch never dispatches ids it cannot read; `AfterRemove` runs only for
  `Applied` deletes.
