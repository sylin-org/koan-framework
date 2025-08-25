---
uid: reference.modules.sora.data.sqlserver
title: Sora.Data.SqlServer — Technical Reference
description: SQL Server adapter for Sora data.
since: 0.2.x
packages: [Sylin.Sora.Data.SqlServer]
source: src/Sora.Data.SqlServer/
---

## Contract
- Adapter implementing paging/streaming; uses Sora.Data.Relational helpers.

## Configuration
- Connection string, pooling, retry/backoff, command timeout.

### Options (typical keys)
- ConnectionStrings:Default (first-win)
- Sora:Data:Sources:Default:ConnectionString
- Sora:Data:SqlServer:ConnectionString
- Sora:Data:SqlServer:CommandTimeoutSeconds (default 30)
- Sora:Data:SqlServer:MaxRetryCount (default 3)
- Sora:Data:SqlServer:MaxRetryDelaySeconds (default 5)

## LINQ and pushdowns
- Supported expressions follow `Sora.Data.Relational` translator. See: `xref:reference.modules.sora.data.relational#supported-linq-subset`.
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
