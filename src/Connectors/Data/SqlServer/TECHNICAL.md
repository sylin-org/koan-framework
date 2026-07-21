---
uid: reference.modules.Koan.data.sqlserver
title: Koan.Data.Connector.SqlServer - Technical Reference
description: SQL Server adapter for Koan data.
packages: [Sylin.Koan.Data.Connector.SqlServer]
source: src/Koan.Data.Connector.SqlServer/
---

## Contract

- The adapter declares `DataCaps.Query.ProviderBoundedPaging` and applies numbered pages in SQL Server
  before candidate rows are materialized into application memory.
- Relational translation and schema helpers remain in Koan.Data.Relational.

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
- Paging uses `OFFSET`/`FETCH`. Every caller-requested provider-bounded stream sort component must be a
  top-level, non-nullable `bool`, `byte`, `sbyte`, `short`, `ushort`, or `int` member. Every other
  caller sort, including an explicit Entity identifier sort, rejects before provider I/O. Data.Core
  appends the usual string Entity identifier only as an opaque provider-stable tie-breaker; that is not
  a CLR or cross-provider collation promise.

## Provider-bounded streaming

- `AllStream` and `QueryStream` are coordinated as lazy numbered pages by Data.Core.
- `batchSize` is the maximum Koan-visible candidate page, not a promise about opaque SQL client
  buffers.
- Deep or collection ordering rejects correctively before provider I/O rather than falling back to a
  complete-result sort.
- Offset paging is not snapshot isolation and does not provide mutation-safe traversal, resume tokens,
  or a public cursor.

## Error modes

- Provider errors (SqlException) surfaced; transient errors retried per options.
- Timeouts honor `CommandTimeoutSeconds`.
- Unsupported predicates surface explicitly; simplify the predicate or materialize a known-small page.
  Provider-bounded streams may apply supported pointwise residuals but never hide a full-source fallback.

## Operations

- Health: connection open/round-trip.
- Metrics: command duration, retries, timeouts; expose counters/timers via hosting platform.
- Logs: SQL text with parameter redaction; retry warnings with attempt counts.

## References

- [DATA-0107 provider-bounded Entity streams](../../../../docs/decisions/DATA-0107-provider-bounded-entity-streams.md)
- [Entity access and streaming](../../../../docs/guides/data/entity-access-and-streaming.md)

