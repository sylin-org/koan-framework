---
uid: reference.modules.Koan.data.postgres
title: Koan.Data.Connector.Postgres - Technical Reference
description: PostgreSQL adapter for Koan data.
since: 0.2.x
packages: [Sylin.Koan.Data.Connector.Postgres]
source: src/Koan.Data.Connector.Postgres/
---

## Contract

- The adapter declares `DataCaps.Query.ProviderBoundedPaging` and applies numbered pages in PostgreSQL
  before candidate rows are materialized into application memory.
- JSON pushdown and projection behavior remain capability-dependent.

## Configuration

- Connection options, SSL, timeouts; naming conventions.

### Options (typical keys)

- ConnectionStrings:Default (first-win)
- Koan:Data:Sources:Default:ConnectionString
- Koan:Data:Postgres:ConnectionString
- Koan:Data:Postgres:CommandTimeoutSeconds (default 30)
- Koan:Data:Postgres:MaxRetryCount (default 3)
- Koan:Data:Postgres:MaxRetryDelaySeconds (default 5)
- Koan:Data:Postgres:JsonMapping (text|jsonb; default text)

## LINQ and pushdowns

- Supported expressions follow `Koan.Data.Relational` translator. See: `xref:reference.modules.Koan.data.relational#supported-linq-subset`.
- String contains/starts/ends use `LIKE` or `ILIKE` depending on collation/case needs.
- JSON fields: when mapped to `jsonb`, limited containment queries may be pushed down; otherwise filter in memory.
- Paging uses `OFFSET`/`LIMIT`. Every caller-requested provider-bounded stream sort component must be a
  top-level, non-nullable `bool`, `byte`, `sbyte`, `short`, `ushort`, or `int` member. Every other
  caller sort, including an explicit Entity identifier sort, rejects before provider I/O. Data.Core
  appends the usual string Entity identifier only as an opaque provider-stable tie-breaker; that is not
  a CLR or cross-provider collation promise.

## Provider-bounded streaming

- `AllStream` and `QueryStream` are coordinated as lazy numbered pages by Data.Core.
- `batchSize` is the maximum Koan-visible candidate page, not a promise about opaque Npgsql buffers.
- Deep or collection ordering rejects correctively before provider I/O rather than falling back to a
  complete-result sort.
- Offset paging is not snapshot isolation and does not provide mutation-safe traversal, resume tokens,
  or a public cursor.

## Error modes

- Provider errors (NpgsqlException) surfaced; transient failures retried per options.
- Timeouts honor `CommandTimeoutSeconds`.
- Unsupported predicates surface explicitly; simplify the predicate or materialize a known-small page.
  Provider-bounded streams may apply supported pointwise residuals but never hide a full-source fallback.

## Operations

- Health: an available PostgreSQL package stays non-critical until it wins default election or a runtime operation
  selects one of its sources. Active sources resolve the same source-specific connection as repositories, then open
  it and execute `SELECT 1`.
- Metrics: command duration, retries, timeouts; track server version for feature toggles.
- Logs: SQL with parameter redaction; note when in-memory filtering occurs.

## References

- DATA-0045 default projection policy: `/docs/decisions/DATA-0045-default-projection-policy-and-json-pushdown.md`
- [DATA-0107 provider-bounded Entity streams](../../../../docs/decisions/DATA-0107-provider-bounded-entity-streams.md)
- [Entity access and streaming](../../../../docs/guides/data/entity-access-and-streaming.md)

