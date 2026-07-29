---
type: REFERENCE
domain: data
title: "Data Adapter Portfolio Roster"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: verified
  scope: generated adapter and family inventory summary
---

# Data adapter roster

Generated from repository facts at `2026-07-28T01:37:38.0591742Z`. The machine-readable authority is [roster.json](roster.json).

- Adapters: 16 (9 Entity persistence, 7 Vector)
- Shared family seams: 5
- Source commit: `86c18819cf03160c20a001d91f3bd2f257fd1a0d` (working tree dirty: `true`)
- Docker: CLI `true`; daemon `true`

| ID | Plane | Package | Family seams | Dedicated tests | Claims |
|---|---|---|---|---:|---:|
| cockroach | entity-persistence | `Sylin.Koan.Data.Connector.Cockroach` | Koan.Data.Relational.Npgsql | 1 | 1 |
| couchbase | entity-persistence | `Sylin.Koan.Data.Connector.Couchbase` | Koan.Data.Core.Document | 1 | 1 |
| elasticsearch | vector | `Sylin.Koan.Data.Connector.ElasticSearch` | Koan.Data.SearchEngine | 1 | 1 |
| inmemory | entity-persistence | `Sylin.Koan.Data.Connector.InMemory` | Koan.Data.Core.KeyValue | 1 | 1 |
| json | entity-persistence | `Sylin.Koan.Data.Connector.Json` | Koan.Data.Core.KeyValue | 1 | 1 |
| mongodb | entity-persistence | `Sylin.Koan.Data.Connector.Mongo` | Koan.Data.Core.Document | 1 | 1 |
| opensearch | vector | `Sylin.Koan.Data.Connector.OpenSearch` | Koan.Data.SearchEngine | 1 | 1 |
| postgres | entity-persistence | `Sylin.Koan.Data.Connector.Postgres` | Koan.Data.Relational, Koan.Data.Relational.Npgsql | 1 | 1 |
| redis | entity-persistence | `Sylin.Koan.Data.Connector.Redis` | Koan.Data.Core.KeyValue | 1 | 1 |
| sqlite | entity-persistence | `Sylin.Koan.Data.Connector.Sqlite` | Koan.Data.Relational | 1 | 1 |
| sqlserver | entity-persistence | `Sylin.Koan.Data.Connector.SqlServer` | Koan.Data.Relational | 1 | 1 |
| vector-inmemory | vector | `Sylin.Koan.Data.Vector.Connector.InMemory` | — | 1 | 1 |
| milvus | vector | `Sylin.Koan.Data.Vector.Connector.Milvus` | — | 1 | 1 |
| qdrant | vector | `Sylin.Koan.Data.Vector.Connector.Qdrant` | — | 1 | 1 |
| sqlitevec | vector | `Sylin.Koan.Data.Vector.Connector.SqliteVec` | — | 1 | 1 |
| weaviate | vector | `Sylin.Koan.Data.Vector.Connector.Weaviate` | — | 1 | 1 |

Cache adapters and Koan.Data.AI were observed only as adjacent package families and are outside this roster.
