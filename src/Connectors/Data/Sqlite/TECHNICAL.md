---
uid: reference.modules.Koan.data.sqlite
title: Koan.Data.Connector.Sqlite - Technical Reference
description: SQLite adapter for Koan data.
packages: [Sylin.Koan.Data.Connector.Sqlite]
source: src/Connectors/Data/Sqlite/
last_updated: 2026-07-17
---

## Contract

- The adapter declares `DataCaps.Query.ProviderBoundedPaging` and applies numbered pages in SQLite
  before candidate rows are materialized into application memory.
- Schema governance follows DATA-0046.

## Configuration

Reference the connector and call the application's normal `AddKoan()` bootstrap. With no configuration, SQLite
uses `.koan/data/Koan.sqlite`; the directory is created on first elected use, not while an available-but-unused
connector reports its boot facts.

The effective Default-source connection is selected in this order:

1. `Koan:Data:Sources:Default:ConnectionString`
2. `Koan:Data:Sources:Default:sqlite:ConnectionString`
3. `Koan:Data:Sqlite:ConnectionString`
4. `ConnectionStrings:Sqlite`
5. `ConnectionStrings:Default`, only when Default is unowned or owned by SQLite
6. autonomous discovery, then `.koan/data/Koan.sqlite`

Blank values are absent. Generic Default-source declarations are consumed only when SQLite owns that source; a
generic `auto` delegates to SQLite's provider path. At provider levels 2–4, the first present `auto` requests
discovery and does not fall through to a lower configuration key. Named sources use
`Koan:Data:Sources:{name}:{Adapter,ConnectionString}`. Use a complete
`Data Source=...` connection string; raw path shorthand is not supported.

`Data Source=:memory:` and explicit `Mode=Memory` targets are source-isolated and host-owned. The connector maps
each to a named shared-memory database, keeps it alive for the Koan host lifetime, and disables driver pooling for
that target. For file databases, `Microsoft.Data.Sqlite` owns process-wide pooling; Koan records the exact
connection-string pool groups observed by a host and clears those groups on host disposal. Two simultaneous hosts
using an identical connection string can therefore share a driver pool group; clearing it preserves correctness
but may make the other host reopen an idle connection. Live caller-owned direct connections must still be disposed
by their caller.

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

- Readiness is critical only when SQLite wins provider election or participates in a
  runtime repository or Direct connection request. Available-but-unused SQLite reports `Unknown` and does not
  touch disk.
- Active readiness resolves and probes every participating source through the same routing path repositories use.
- Host disposal closes its in-memory keepers and clears the driver pool groups it observed; a new host may recreate
  the same file path.
- Connection and discovery logs de-identify credentials and other connection-string secrets.

## References

- DATA-0046 SQLite DDL policy: `/docs/decisions/DATA-0046-sqlite-schema-governance-ddl-policy.md`
- [DATA-0107 provider-bounded Entity streams](../../../../docs/decisions/DATA-0107-provider-bounded-entity-streams.md)
- [Entity access and streaming](../../../../docs/guides/data/entity-access-and-streaming.md)

