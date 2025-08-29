---
id: DATA-0062
slug: DATA-0062-instance-save-set-first-class
domain: DATA
status: Accepted
date: 2025-08-28
title: Instance Save(set) is first-class; Data<TEntity,TKey>.Upsert(..., set) is second-class
---

# ADR DATA-0062 — Instance Save(set) as first-class DX

## Context

- Sora already supports logical entity "sets" that route to parallel physical stores by suffixing the storage name with `#<set>` (see DATA-0030 and `StorageNameRegistry`).
- Today, set-targeted operations are exposed primarily via the generic facade (`Data<TEntity,TKey>.UpsertAsync(entity, set)`, `GetAsync(id, set)`, etc.). While functional, it isn't the most ergonomic path for application code that works with instances.
- We want a natural, entity-centric developer experience: `model.Save("Audit")`, `models.Save("Moderation")` — without forcing consumers to hop to the static facade.

## Decision

- Make instance-level Save/Upsert overloads that accept a set the first-class API for writing to parallel sets.
  - Provide generic-key and string-key conveniences:
    - `Task<TEntity> Save<TEntity, TKey>(this TEntity model, string set, CancellationToken ct = default)`
    - `Task<TEntity> Upsert<TEntity, TKey>(this TEntity model, string set, CancellationToken ct = default)`
    - `Task<TEntity> Save<TEntity>(this TEntity model, string set, CancellationToken ct = default)` (string key)
    - `Task<TEntity> Upsert<TEntity>(this TEntity model, string set, CancellationToken ct = default)` (string key)
- Internally, these overloads scope the ambient `DataSetContext` and call the configured repository. Storage names will resolve to `Type#<set>` via `StorageNameRegistry`.
- Treat `Data<TEntity,TKey>.UpsertAsync(entity, set)` and other set-aware statics as second-class escape hatches. They remain available and supported.

Optional (recommended) enhancements
- Introduce a small `DataSetKey` typed wrapper to avoid magic strings, with well-known constants: `Primary`, `Audit`, `DeleteFlags`, `Moderation`, `Outbox`. Add overloads that accept `DataSetKey` in addition to `string`.

## Scope

- In scope: instance Save/Upsert(set) for single and bulk saves; no change to read/query APIs in this ADR.
- Out of scope: controller parameter shapes, header names, and pipeline behaviors (moderation, audit). Those are covered by separate decisions.

## Consequences

- DX improves: developers can redirect writes to specific sets with a single, discoverable call on the instance.
- Consistency with existing set routing: storage names resolve to `Base + "#" + set` (except for root/null which has no suffix).
- Backward compatibility is preserved; second-class statics continue to work.

## Implementation notes

- Add overloads in `src/Sora.Data.Core/AggregateExtensions.cs`:
  - Each overload should `using var _ = Data<TEntity,TKey>.WithSet(set);` then call the corresponding repository method.
  - Provide bulk variants: `Save(this IEnumerable<TEntity> models, string set, ...)` for both generic and string-keyed entities.
- Do not change `Entity<TEntity,TKey>` static helpers in this ADR; keep the stance that instance Save(set) is the first-class path for writes. Static set-aware operations remain available on `Data<TEntity,TKey>`.
- Edge cases:
  - `null`, empty, or `"root"` set -> use the base storage name (no suffix).
  - AsyncLocal scoping via `DataSetContext` must always be disposed promptly (use `using`).

## Follow-ups

- Consider adding typed `DataSetKey` and overloads to reduce magic strings (DX + safety).
- Update samples (`samples/S2.Api`, etc.) to demonstrate `model.Save("Audit")` and bulk `models.Save("Moderation")`.
- Optional: add `GetFrom/SaveTo` statics on `Entity<TEntity,TKey>` if we want parity for reads (separate ADR if pursued).

## References

- DATA-0030 — Entity sets routing and storage suffixing
- ARCH-0040 — Config and constants naming
- `Sora.Data.Core.DataSetContext`, `Sora.Data.Core.Configuration.StorageNameRegistry`
