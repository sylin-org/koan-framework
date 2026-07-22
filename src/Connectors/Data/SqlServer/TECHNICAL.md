---
uid: reference.modules.Koan.data.sqlserver
title: Koan.Data.Connector.SqlServer - Technical Reference
description: SQL Server adapter for Koan data.
packages: [Sylin.Koan.Data.Connector.SqlServer]
source: src/Connectors/Data/SqlServer/
---

## Contract

- The adapter declares `DataCaps.Query.ProviderBoundedPaging` and applies numbered pages in SQL Server
  before candidate rows are materialized into application memory.
- Relational translation and schema helpers remain in Koan.Data.Relational.

## Configuration

- Connection string, schema governance, JSON materialization, naming, discovery, and readiness use
  the existing `SqlServerOptions` and adapter-readiness contracts.

### Options (typical keys)

- ConnectionStrings:Default (first-win)
- ConnectionStrings:SqlServer
- Koan:Data:Sources:Default:sqlserver:ConnectionString
- Koan:Data:SqlServer:ConnectionString
- Koan:Data:SqlServer:DdlPolicy
- Koan:Data:SqlServer:SchemaMatchingMode
- Koan:Data:SqlServer:JsonCaseInsensitive
- Koan:Data:SqlServer:JsonWriteIndented
- Koan:Data:SqlServer:JsonIgnoreNullValues

`ConnectionString=auto` attempts orchestration discovery before using the localhost development
fallback. The connector does not currently expose provider-specific retry or command-timeout options.

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

- Provider errors (`SqlException`) surface to the caller; the connector does not add a retry policy.
- Unsupported predicates surface explicitly; simplify the predicate or materialize a known-small page.
  Provider-bounded streams may apply supported pointwise residuals but never hide a full-source fallback.
- An unreachable selected SQL Server reports unhealthy readiness and never falls back to a different
  data provider.

## Operations

- Health: connection open/round-trip.
- Tracing: connector operations use the `Koan.Data.Connector.SqlServer` activity source.
- Configuration/discovery logs avoid introducing a second provider-selection path.

## References

- [DATA-0107 provider-bounded Entity streams](../../../../docs/decisions/DATA-0107-provider-bounded-entity-streams.md)
- [Entity access and streaming](../../../../docs/guides/data/entity-access-and-streaming.md)

