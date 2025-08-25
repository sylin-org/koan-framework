---
uid: reference.modules.sora.data.postgres
title: Sora.Data.Postgres — Technical Reference
description: PostgreSQL adapter for Sora data.
since: 0.2.x
packages: [Sylin.Sora.Data.Postgres]
source: src/Sora.Data.Postgres/
---

## Contract
- Adapter implementing paging/streaming; JSON pushdown and projection defaults.

## Configuration
- Connection options, SSL, timeouts; naming conventions.

### Options (typical keys)
- ConnectionStrings:Default (first-win)
- Sora:Data:Sources:Default:ConnectionString
- Sora:Data:Postgres:ConnectionString
- Sora:Data:Postgres:CommandTimeoutSeconds (default 30)
- Sora:Data:Postgres:MaxRetryCount (default 3)
- Sora:Data:Postgres:MaxRetryDelaySeconds (default 5)
- Sora:Data:Postgres:JsonMapping (text|jsonb; default text)

## LINQ and pushdowns
- Supported expressions follow `Sora.Data.Relational` translator. See: `xref:reference.modules.sora.data.relational#supported-linq-subset`.
- String contains/starts/ends use `LIKE` or `ILIKE` depending on collation/case needs.
- JSON fields: when mapped to `jsonb`, limited containment queries may be pushed down; otherwise filter in memory.
- Paging via OFFSET/LIMIT with stable `Id ASC` ordering.

## Error modes
- Provider errors (NpgsqlException) surfaced; transient failures retried per options.
- Timeouts honor `CommandTimeoutSeconds`.
- NotSupportedException for unsupported predicates; fallback to streaming + in-memory filtering for small windows.

## Operations
- Health: TCP connect and simple `SELECT 1`.
- Metrics: command duration, retries, timeouts; track server version for feature toggles.
- Logs: SQL with parameter redaction; note when in-memory filtering occurs.

## References
- DATA-0045 default projection policy: `/docs/decisions/DATA-0045-default-projection-policy-and-json-pushdown.md`
- DATA-0061 paging/streaming: `/docs/decisions/DATA-0061-data-access-pagination-and-streaming.md`
