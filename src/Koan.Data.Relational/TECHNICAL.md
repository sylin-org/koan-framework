---
uid: reference.modules.Koan.data.relational
title: Koan.Data.Relational - Technical Reference
description: Shared relational helpers and conventions for relational adapters.
since: 0.2.x
packages: [Sylin.Koan.Data.Relational]
source: src/Koan.Data.Relational/
---

## Contract

- Inputs/Outputs: schema helpers, parameter conventions, paging integration.
- Error modes: provider exceptions; consistent mapping.

## Usage guidance

- Used by Sqlite/SqlServer/Postgres adapters; do not use directly in apps unless needed.

## Supported LINQ subset

- Equality/inequality: `==`, `!=`
- Comparisons on scalars: `<`, `<=`, `>`, `>=` (numeric, DateTime, string per collation)
- Logical composition: `&&`, `||`, with parentheses respected
- Null checks: `x == null`, `x != null`
- String functions: `StartsWith`, `EndsWith`, `Contains` → translated to `LIKE` with dialect-proper escaping; case sensitivity is dialect/collation specific
- Boolean members: `x.IsActive` and `!x.IsActive`
- Enum equality (stored as int or string per mapping)

Not supported (throw NotSupportedException):

- Arbitrary method calls, client-eval delegates, custom functions
- Subqueries/joins/navigation properties
- Complex collection operators (`Any/All` on in-memory lists), except where provider adds explicit support

Fallbacks:

- Callers should catch `NotSupportedException` and either change the predicate or apply filtering in-memory on a streamed/paged set when feasible (see DATA-0061). For HTTP, prefer emitting tighter server-side filters.

## Pushdown coverage

- Translator produces a WHERE clause and parameters; exact SQL (quoting, LIKE escaping, parameter names) is provided by `ILinqSqlDialect`.
- JSON columns: complex CLR types may be stored as JSON (TEXT); predicate pushdown against JSON fields is provider-specific (see provider TECHNICAL docs).
- Projection: select lists are cached per (entity, dialect) via `RelationalCommandCache`.

## References

- DATA-0046 SQLite DDL policy: `/docs/decisions/DATA-0046-sqlite-schema-governance-ddl-policy.md`
- DATA-0044 Paging guardrails: `/docs/decisions/DATA-0044-paging-guardrails-and-tracing-must.md`
