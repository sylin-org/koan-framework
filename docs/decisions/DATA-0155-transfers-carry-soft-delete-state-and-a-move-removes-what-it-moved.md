---
id: DATA-0155
slug: transfers-carry-soft-delete-state-and-a-move-removes-what-it-moved
domain: Data
status: Accepted
date: 2026-10-21
title: Transfers carry soft-delete state, and a Move removes what it moved and reports what it could not
related:
  - ARCH-0101
  - ARCH-0102
  - DATA-0079
  - DATA-0143
  - DATA-0149
---

# DATA-0155: Transfers carry soft-delete state, and a Move removes what it moved and reports what it could not

Amends [DATA-0079](DATA-0079-entity-transfer-helpers.md) and [DATA-0149](DATA-0149-transfers-carry-stored-values-exactly-by-default.md).

## Context

A soft-deleted row is the stored row plus a managed field the delete override sets (`__deleted` for `[SoftDelete]`,
ARCH-0101 §4). The Entity type cannot hold that field, so a transfer, which reads and writes Entities, dropped it
(NK-184, what DATA-0149 left open):

- inside `WithDeleted()` a `Copy` read soft-deleted rows and wrote them visible, resurrecting them in the destination;
- a `Move` deleted its moved rows through the delete override, so every one stayed in the source as a tombstone while
  `DeletedCount` counted it as deleted;
- outside `WithDeleted()` a transfer did not read soft-deleted rows at all, and there was no way to carry them
  faithfully.

The delete phase of a `Move`, and of a `Mirror`, also threw `BulkMutationReceiptRejectedException` as soon as one batch
removed fewer rows than asked, after earlier batches were already deleted, so the counts were lost and the rest of
the delete never ran (NK-183). A short receipt there means another writer removed the row first, or the store removed
fewer; the rows are in the destination either way.

## Decision

1. **A transfer reads what the caller sees.** Outside `WithDeleted()` soft-deleted rows are not selected and stay
   where they are; inside it they are selected.
2. **A soft-deleted row arrives soft-deleted.** For an Entity with a delete override, each batch asks the side it was
   read from which of its rows carry the override's mark, within the read scope, and writes those rows to the
   destination with the mark, through the same operation-override write channel a delete uses. The other rows are
   written as before. This holds for `Copy`, `Move` and both directions of `Mirror`.
3. **A Move removes what it moved.** Under the default exact fidelity the rows a `Move` deletes from the source are
   removed physically, bypassing the delete override but not the read scope, because the destination now holds them.
   With `TransferFidelity.Semantic` they are removed as `Remove` removes them, which for `[SoftDelete]` leaves a
   tombstone; `Warnings` says how many. A `Mirror` that deletes replica rows still deletes them as `Remove` does, so a
   soft-delete replica mirrors a deletion as a soft deletion.
4. **A short delete receipt is reported, not thrown.** When a delete batch of a `Move` or a `Mirror` removes fewer rows
   than asked, the shortfall is added to `Warnings` and the run continues with its remaining batches and returns its
   result. A receipt that reports more rows than asked is still an exact-receipt failure.
5. **A Move is not atomic, and its journal is not durable.** The copy completes before the first delete, and the
   journal of moved identities is a delete-on-close temporary file. A process that stops between the two leaves the
   rows in both places with no record. Running the same `Move` again is the recovery: the destination write is an
   upsert and only rows unchanged since the copy are deleted. A durable, resumable journal is not part of this
   decision.

## Consequences

- An exact `Move` of a `[SoftDelete]` Entity no longer leaves tombstones in the source. An application that relied on
  the source keeping them asks for `TransferFidelity.Semantic`, which keeps the earlier behaviour and now reports it.
- A `Copy` inside `WithDeleted()` keeps the destination's soft-deleted rows hidden.
- A `Move` or `Mirror` whose delete was short returns a result with a warning instead of throwing.

## Evidence

- `SoftDeleteTransferSpec` (Data.SoftDelete, SQLite): a `Copy` inside `WithDeleted()` keeps the tombstone hidden in the
  destination; a plain `Move` leaves no tombstone of the moved row and a `Move` inside `WithDeleted()` carries the
  tombstone as a tombstone and empties the source; a Semantic `Move` leaves the tombstone and reports it. All three fail
  before this change.
- `EntityTransferDslSpec` (Data.Core): `Move_ADeleteThatRemovesFewerRows_IsReportedAndTheRemainingBatchesRun` and
  `Mirror_ADeleteThatRemovesFewerRows_IsReported` fail before this change.
