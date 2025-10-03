---
uid: reference.modules.Koan.data.sqlite
title: Koan.Data.Connector.Sqlite - Technical Reference
description: SQLite adapter for Koan data.
since: 0.2.x
packages: [Sylin.Koan.Data.Connector.Sqlite]
source: src/Koan.Data.Connector.Sqlite/
---

## Contract

- Adapter implementing paging/streaming per DATA-0061; schema governance per DATA-0046.

## Configuration

- Connection strings and file paths; follow options conventions.

### Options (typical keys)

- ConnectionStrings:Default (first-win)
- Koan:Data:Sources:Default:ConnectionString
- Koan:Data:Sqlite:ConnectionString (file path or `Data Source=:memory:`)
- Koan:Data:Sqlite:CommandTimeoutSeconds (default 30)

## LINQ and pushdowns

- Supported expressions follow `Koan.Data.Relational` translator. See: `xref:reference.modules.Koan.data.relational#supported-linq-subset`.
- String matching uses `LIKE`; case sensitivity depends on collation.
- Paging via LIMIT/OFFSET with stable `Id ASC` ordering.

## Error modes

- Provider errors (SqliteException) surfaced; retries are limited (single-node engine).
- NotSupportedException for unsupported predicates; prefer streaming + in-memory post-filtering for small windows.

## Operations

- Health: file reachable and simple query round-trip.
- Logs: SQL with parameter redaction.

## References

- DATA-0046 SQLite DDL policy: `/docs/decisions/DATA-0046-sqlite-schema-governance-ddl-policy.md`
- DATA-0061 paging/streaming: `/docs/decisions/DATA-0061-data-access-pagination-and-streaming.md`

