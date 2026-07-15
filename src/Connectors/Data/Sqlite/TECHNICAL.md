---
uid: reference.modules.Koan.data.sqlite
title: Koan.Data.Connector.Sqlite - Technical Reference
description: SQLite adapter for Koan data.
since: 0.2.x
packages: [Sylin.Koan.Data.Connector.Sqlite]
source: src/Koan.Data.Connector.Sqlite/
---

## Contract

- The adapter declares `DataCaps.Query.ProviderBoundedPaging` and applies numbered pages in SQLite
  before candidate rows are materialized into application memory.
- Schema governance follows DATA-0046.

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
- Paging uses `LIMIT`/`OFFSET`. Every caller-requested provider-bounded stream sort component must be a
  top-level, non-nullable `bool`, `byte`, `sbyte`, `short`, `ushort`, or `int` member. Every other
  caller sort, including an explicit Entity identifier sort, rejects before provider I/O. Data.Core
  appends the usual string Entity identifier only as an opaque provider-stable tie-breaker; that is not
  a CLR or cross-provider collation promise.

## Provider-bounded streaming

- `AllStream` and `QueryStream` are coordinated as lazy numbered pages by Data.Core.
- `batchSize` is the maximum Koan-visible candidate page, not a promise about opaque SQLite driver
  buffers.
- Deep or collection ordering rejects correctively before provider I/O rather than falling back to a
  complete-result sort.
- Offset paging is not snapshot isolation and does not provide mutation-safe traversal, resume tokens,
  or a public cursor.

## Error modes

- Provider errors (SqliteException) surfaced; retries are limited (single-node engine).
- Unsupported predicates surface explicitly; simplify the predicate or materialize a known-small page.
  Provider-bounded streams may apply supported pointwise residuals but never hide a full-source fallback.

## Operations

- Health: file reachable and simple query round-trip.
- Logs: SQL with parameter redaction.

## References

- DATA-0046 SQLite DDL policy: `/docs/decisions/DATA-0046-sqlite-schema-governance-ddl-policy.md`
- [DATA-0107 provider-bounded Entity streams](../../../../docs/decisions/DATA-0107-provider-bounded-entity-streams.md)
- [Entity access and streaming](../../../../docs/guides/data/entity-access-and-streaming.md)

