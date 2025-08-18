# 0016: Entity extension naming and parity

Date: 2025-08-16

## Status
Accepted

## Context
We expose first-level conveniences as extensions over `IEntity<TKey>` and sequences of entities.
Suffixing with `Async` on these surface helpers adds noise and reduces readability in common call sites. We also aim to keep practical parity between domain static helpers (`Entity<TEntity, TKey>`) and `IEntity` extensions for consistency.

## Decision
- Use short, meaningful verbs for entity extensions:
  - `Upsert(model, ct)`, `Save(model, ct)` (alias), `UpsertId(model, ct)`, `Remove(model, ct)`.
  - For collections: `Save(models, ct)` (bulk upsert only of provided items), `SaveReplacing(models, ct)` (delete-all then upsert), and `Remove(models, ct)`.
  - Provide a string-key convenience overload: `Save<TEntity>(IEnumerable<TEntity> models, ct)` where `TEntity : IEntity<string>`.
- Retain `Async` suffix where it is part of a core repository or batch contract (e.g., `IBatchSet.SaveAsync`).
- Provide conversions: `IEnumerable<TEntity>.AsBatch()` and `IBatchSet.AddRange(models)`.
- Keep 1:1 parity where feasible between `Entity<TEntity, TKey>` static methods and `IEntity` extensions (e.g., `RemoveAll` vs `IEnumerable.Remove`).

## Consequences
- Caller code is simpler: `await todo.Save(ct)` and `await items.SaveReplacing(ct)`.
- No behavior change; only naming at the extension layer.
- Docs and samples should prefer the concise names; repository/adapter contracts remain unchanged.

## Alternatives considered
- Keeping `Async` suffix everywhere for uniformity—rejected for verbosity.
- Introducing sync wrappers—rejected; all I/O remains async under the hood.
