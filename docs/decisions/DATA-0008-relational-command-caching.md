# 0008: Relational command caching (dialect templates + per-entity rendered commands)

Date: 2025-08-16

## Status
Accepted

## Context
Rendering SQL repeatedly (SELECT lists, WHERE scaffolds, DELETE/UPSERT statements) incurs overhead. We want predictable performance without adopting a heavy ORM. A small, layered cache keeps rendering costs near-zero after warmup and stays simple to reason about.

## Decision
- Introduce a two-tier cache:
  1. Dialect-level templates (adapter scope): SQL skeletons that depend only on schema/dialect (e.g., SELECT projection list, UPSERT/DELETE forms). Owned by the provider/dialect.
  2. Per-entity, per-dialect rendered commands (type configuration scope): final SQL strings (SelectAll, SelectById, DeleteById, Upsert) and compiled binders. Stored alongside the entity’s aggregate configuration and shared across repositories.

## Consequences
- Eliminates repeated string composition and reflection in hot paths.
- Keeps ownership clear: grammar remains in the provider; entity-specific rendering stays with type configuration.
- Simple invalidation: keys include (entityType, keyType, dialectType, optionsHash, version).

## Implementation
- Added `RelationalCommandCache` (SELECT list cache) and wired SQLite to use a per-entity bag via `AggregateBags`.
- Extended `AggregateConfig` with an internal per-entity bag and a public helper `AggregateBags.GetOrAdd` for safe access.
- SQLite repository now caches: SelectList, SelectAll, SelectById, DeleteById.

## Next
- Add Upsert and binder caching (compiled expressions) for insert/update parameters.
- Include dialect options (case-insensitive LIKE, parameter prefix) in optionsHash.
- Golden tests for cache correctness and SQL shape.
