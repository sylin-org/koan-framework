---
id: DATA-0156
slug: scoped-deletes-carry-the-scope-to-the-store
domain: Data
status: Accepted
date: 2026-10-21
title: A scoped delete carries its scope to the store
related:
  - DATA-0105
  - DATA-0106
  - DATA-0124
  - DATA-0141
  - ARCH-0103
---

# DATA-0156: A scoped delete carries its scope to the store

Amends [DATA-0124](DATA-0124-batch-deletes-are-scoped-like-single-deletes.md), which deferred this as follow-up.

## Context

Under a managed or read scope (tenant segmentation, managed fields, DataAxis predicates, soft delete, Web row filters)
the facade's keyed deletes were check-then-delete. `Delete(id)` queried the id through the scope, then called the
adapter's identity-only `Delete(id)`; `DeleteMany` and the batch read the visible subset and deleted it by identity.
The statement the store ran carried no scope. Between the check and the act, the row could leave the scope: scope A's
row deleted and scope B saving the same caller-chosen id, or a predicate axis value changing. A's delete then removed
B's row. Ledger NK-168, NK-181, NK-182 and NK-303.

Measured before this change with a decorator that interleaves exactly that write between the facade's check and the
store's act: on SQLite, PostgreSQL 18.6, MongoDB 8.3.13 and InMemory, the single, many and batch delete each removed
B's row and reported success.

DATA-0124 kept isolation in the facade because the only scope an adapter received was `ManagedFieldWriteScope`, which
carries managed equality values and none of the read-filter predicates. That reason no longer holds once the folded
read scope itself, the filter the check already pushes down, is the argument.

## Decision

**A keyed delete under a scope runs as one store statement that carries the scope, on every adapter that declares
`DataCaps.Write.ConditionalDelete`. The receipt is what the store removed.**

1. **The scope is the guard.** The facade passes its folded read scope to the adapter's conditional delete, the same
   filter its visibility check pushed down: `DELETE ... WHERE identity AND scope`. Single `Delete` calls
   `ConditionalDeleteAsync(id, scope)`. `DeleteMany`, `DeleteAll`, `RemoveAll` and the bounded mass-delete pages call
   the new `IConditionalDeleteRepository.ConditionalDeleteMany(ids, scope)`, whose default deletes one guarded identity
   at a time and which SQLite, PostgreSQL and MongoDB implement as one guarded statement per bounded slice.
2. **Batches guard each delete.** A native batch that implements the new `IConditionalBatchDelete<TEntity, TKey>`
   queues `DeleteIf(id, scope)`, so the delete runs inside the batch, in its transaction where it has one. No
   visibility read precedes it: an id outside the scope deletes nothing and reports `Missing`, as DATA-0124 promises.
   SQLite, PostgreSQL, MongoDB and the key-value family (InMemory) implement it.
3. **The guard sees stored managed values.** `IConditionalDeleteRepository`'s guard is evaluated against the stored row,
   managed discriminators included. InMemory now evaluates it as its query does (the managed-aware evaluator), not with
   the entity-only conditional evaluator that refused managed fields.
4. **An adapter that cannot carry the scope keeps today's path and says so.** Without `Write.ConditionalDelete`, or
   behind a decorator or variant view that does not forward the contract, the facade checks and then deletes by
   identity, as before. `IAxisScopeDiagnostics.ScopedDeletesAtStore` and `AxisAdapter.ScopedDeletesAtStore` report
   which path an entity takes, and `AxisExplanation.Summary()` prints `deletes scoped-at-store` or
   `deletes check-then-delete`. A scope containing a counterpart (relationship) predicate also keeps the checked path,
   because a conditional delete accepts only row predicates.
5. **Decorators forward the contract.** The Entity cache's `CachedRepository` already forwarded the
   `ConditionalDelete` capability without the interface; it now implements `IConditionalDeleteRepository` and guarded
   batch deletes by forwarding them and retiring the cached entry.

## What does not change

- Unscoped deletes are identity deletes, byte-identical.
- Soft delete is still an override write, not a native delete (NK-289 is separate).
- A remove Lifecycle still reads the row first, because `BeforeRemove` needs it. Only the delete that follows carries
  the scope; when the row left the scope in between, the delete removes nothing and `AfterRemove` does not run.
- `DeleteMany` under a remove Lifecycle still throws `BulkMutationReceiptRejectedException` when the store removed
  fewer rows than were prepared, now also when a row left the scope concurrently.

## Compatibility

- Behaviour: a scoped delete that loses its row to a concurrent change now removes nothing and reports false, 0 or
  `Missing`, where it used to remove the other scope's row and report success. Scoped deletes on the declaring
  adapters drop the visibility round trip when no Lifecycle is registered.
- Public surface: `IConditionalDeleteRepository<TEntity, TKey>` gains `ConditionalDeleteMany` with a default body, so
  existing implementations compile. `IConditionalBatchDelete<TEntity, TKey>` is new and optional.
  `IAxisScopeDiagnostics` gains `ScopedDeletesAtStore`; `AxisAdapter` gains the init property `ScopedDeletesAtStore`.
- InMemory conditional deletes accept guards over managed and DateTime fields, evaluated as its queries evaluate them.

## Evidence

`ScopedDeleteRace` (AdapterSurface TestKit) arms one interleaving per leg, fired at whichever comes first: the facade's
visibility read returning or the delete reaching the store. Single, many and batch deletes under scope A must remove
nothing and leave B's row. It runs on SQLite, PostgreSQL 18.6, MongoDB 8.3.13 and InMemory, and failed on every leg on
all four before this change; `JsonScopedDeleteGapSpec` shows Json declares the checked path.
