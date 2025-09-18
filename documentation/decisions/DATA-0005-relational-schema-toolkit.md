---
id: DATA-0005
slug: DATA-0005-relational-schema-toolkit
domain: DATA
status: Accepted
date: 2025-08-16
---

# 0005: Relational schema toolkit (model, dialect, synchronizer)

## Context
- We want relational adapters to share a consistent way to generate and apply schema from entity metadata without polluting the agnostic core.
- Decisions already made: property-anchored Index, Identifier → PK/unique (no duplicate id index), auto-JSON for complex types in relational providers.

## Decision
- Create a relational-only toolkit with clear contracts:
  - Model: RelationalTable/RelationalColumn/RelationalIndex via RelationalModelBuilder.FromEntity(Type), honoring Storage/StorageName/Identifier/Index/IgnoreStorage/DataAnnotations and TypeClassification.
  - Dialect: IRelationalDialect (quote, MapType, CreateTable, CreateIndexes) for vendor differences.
  - Synchronizer: IRelationalSchemaSynchronizer with EnsureCreated (v1 add-only). Future: diff/plan with CreateOrUpdate/Rebuild modes.
- Keep IDataRepository free of schema concerns; adapters opt-in via toolkit.

## Consequences
- Consistent schema creation across SQLite/SQL Server/Postgres with minimal adapter code.
- Core remains provider-agnostic.
- Additive by default; destructive migrations are an explicit future opt-in.

## Implementation notes
- Koan.Data.Relational provides the contracts and a simple synchronizer.
- SQLite adapter consumes the toolkit now; others can follow with their dialects.
- Provider-specific dialect implementations live in the provider packages (e.g., `Koan.Data.Sqlite.SqliteDialect`). The relational toolkit must remain adapter-agnostic.
