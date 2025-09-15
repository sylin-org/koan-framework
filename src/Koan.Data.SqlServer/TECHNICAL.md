---
uid: reference.modules.Koan.data.sqlserver
title: Koan.Data.SqlServer — Technical Reference
description: SQL Server adapter for Koan data.
since: 0.2.x
packages: [Sylin.Koan.Data.SqlServer]
source: src/Koan.Data.SqlServer/
---

## Contract
- Adapter implementing paging/streaming; uses Koan.Data.Relational helpers.

## Configuration
- Connection string, pooling, retry/backoff, command timeout.

### Options (typical keys)
- ConnectionStrings:Default (first-win)
- Koan:Data:Sources:Default:ConnectionString
- Koan:Data:SqlServer:ConnectionString
- Koan:Data:SqlServer:CommandTimeoutSeconds (default 30)
- Koan:Data:SqlServer:MaxRetryCount (default 3)
- Koan:Data:SqlServer:MaxRetryDelaySeconds (default 5)

## LINQ and pushdowns
- Supported expressions follow `Koan.Data.Relational` translator. See: `xref:reference.modules.Koan.data.relational#supported-linq-subset`.
- Paging is translated to OFFSET/FETCH; stable order is by `Id ASC`.

## Error modes
- Provider errors (SqlException) surfaced; transient errors retried per options.
- Timeouts honor `CommandTimeoutSeconds`.
- NotSupportedException thrown for unsupported predicates; callers should adjust filters or fallback to streaming + in-memory filtering for small windows.

## Operations
- Health: connection open/round-trip.
- Metrics: command duration, retries, timeouts; expose counters/timers via hosting platform.
- Logs: SQL text with parameter redaction; retry warnings with attempt counts.

## References
- DATA-0044 paging guardrails: `/docs/decisions/DATA-0044-paging-guardrails-and-tracing-must.md`
- DATA-0061 paging/streaming: `/docs/decisions/DATA-0061-data-access-pagination-and-streaming.md`
