# Projection Policy Spec

This spec describes the default projection policy and how adapters consume it.

## Scope
- Central ProjectionResolver in Sora.Data.Core.
- Adapter behaviors for SQLite (generated columns + JSON1 pushdown).

## ProjectionResolver
- Input: entity type
- Output: list of projected properties with:
  - Column name (from [Column] or resolved via StorageNameResolver)
  - CLR type (with enum strategy: string by default)
  - Index hint (from [Index])
  - Opt-out via [NotMapped]

## Adapters
- SQLite
  - DDL: create generated columns, create indexes
  - Query: prefer columns, else json_extract(Json, '$.Prop') with ESCAPE for LIKE
  - No silent in-memory fallback

## Open Questions
- Global DI policy for enum-as-int.
- Diagnostics for large projection sets.
