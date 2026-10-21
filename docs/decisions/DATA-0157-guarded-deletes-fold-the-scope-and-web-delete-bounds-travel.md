---
id: DATA-0157
slug: guarded-deletes-fold-the-scope-and-web-delete-bounds-travel
domain: Data
status: Accepted
date: 2026-10-21
title: A guarded delete folds the active scope, and a Web delete bound travels with the delete
related:
  - DATA-0141
  - DATA-0156
  - SEC-0004
  - SEC-0013
  - SEC-0019
---

# DATA-0157: A guarded delete folds the active scope, and a Web delete bound travels with the delete

Amends [DATA-0156](DATA-0156-scoped-deletes-carry-the-scope-to-the-store.md).

## Context

`ConditionalDeleteAsync` (`Data<T,K>.DeleteIf`) refused under any managed or read scope, because the guard it sent the
store carried no scope and an unscoped conditional delete could remove another scope's row. DATA-0156 made the store's
conditional delete the carrier of the read scope, so that refusal protects nothing any more and blocks the one caller
that needs a guarded delete under a scope: the Web endpoint.

The Web delete verbs (`DELETE /{id}`, `/bulk`, by query, all) loaded the rows inside the principal's delete bound
(SEC-0013, SEC-0019), ran the delete hooks, then deleted by identity through `Data<T,K>`. A row reassigned to another
owner between the check and the delete was still deleted. Ledger NK-293. Measured on the InMemory Web host with a
decorator that reassigns the row right after the bound check reads it: the single delete answered 200 and the bulk
delete reported 1, and the other owner's row was gone.

## Decision

1. **A guarded delete folds the scope.** Under an active read scope, the facade's `ConditionalDeleteAsync` ANDs the
   scope into the caller's guard and runs one guarded statement, the DATA-0156 path. A scope that holds a counterpart
   predicate cannot travel and still refuses.
2. **A row-only Web delete bound travels with the delete.** When the bound has no counterpart, the adapter declares
   `Write.ConditionalDelete` and pushes the bound completely, and no deferred transaction is open, the endpoint
   deletes with `ConditionalDeleteAsync(id, bound)` or `ConditionalDeleteMany(ids, bound)`. A row that left the bound
   after the check is not removed: a single delete answers 404, a bulk delete does not count it. Data refuses a guard
   it cannot honour before it writes, and the endpoint then keeps the checked delete.
3. **Updates are unchanged.** Update and Patch still check the update bound on the loaded row and then save. A
   guarded replace under a managed scope must also keep the row's managed stamps, which the adapters' conditional
   replace does not yet prove, so this record does not change it.

## Compatibility

- `Data<T,K>.DeleteIf` and `IConditionalDeleteRepository` through the facade now run under a scope instead of
  throwing `NotSupportedException`.
- A Web delete racing an owner change answers 404 where it used to delete the row. Hooks are unchanged: they still
  run for the rows the check loaded.

## Evidence

- `ScopedDeleteRace.AssertConditionalDeleteCarriesScopeAsync` (SQLite, PostgreSQL, MongoDB, InMemory): under scope A,
  `DeleteIf` on scope B's row returns false and the row survives, on A's own row it returns true. It threw before.
- `DeleteBoundRaceSpec` (Web.Extensions, InMemory): an owner change between the bound check and the delete leaves the
  row for its new owner; 404 by id, `deleted: 0` by id list. Both failed before.
