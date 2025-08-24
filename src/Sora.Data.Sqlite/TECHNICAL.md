---
uid: reference.modules.sora.data.sqlite
title: Sora.Data.Sqlite — Technical Reference
description: SQLite adapter for Sora data.
since: 0.2.x
packages: [Sylin.Sora.Data.Sqlite]
source: src/Sora.Data.Sqlite/
---

## Contract
- Adapter implementing paging/streaming per DATA-0061; schema governance per DATA-0046.

## Configuration
- Connection strings and file paths; follow options conventions.

### Options (typical keys)
- ConnectionStrings:Default (first-win)
- Sora:Data:Sources:Default:ConnectionString
- Sora:Data:Sqlite:ConnectionString (file path or `Data Source=:memory:`)
- Sora:Data:Sqlite:CommandTimeoutSeconds (default 30)

## LINQ and pushdowns
- Supported expressions follow `Sora.Data.Relational` translator. See: `xref:reference.modules.sora.data.relational#supported-linq-subset`.
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
