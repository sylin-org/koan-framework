---
type: SPEC
domain: framework
title: "R13-20 - Fleet zero-configuration guarantee"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-08-30
framework_version: v1.0
validation:
  status: passed
  scope: every data provider in the fleet, bare-package consumers, conventional containers, and the failure posture when no server exists
---

# R13-20 — Fleet zero-configuration guarantee

## Outcome

Every data provider in the fleet is held to one guarantee: **a bare package reference plus
`AddKoan()` runs against a conventionally started server with zero application configuration** —
no connection string, no endpoint keys, no environment setup on the application side. Embedded
floors (SQLite, JSON, InMemory, DuckDB, SqliteVec) need no server at all. Providers whose servers
cannot be started without operator secrets (SQL Server SA password, CouchDB admin user, MySQL root
password, PostgreSQL password) resolve them through a uniform layering: configuration keys → the
official image's own environment convention → the documented development default. Verified live for
every networked provider with bare consumers built from repository source.

## The credential-and-endpoint layering

One shape, per adapter: configuration keys → official-image environment convention → development
default. Defaults are never invented — each is the prior-art convention (Testcontainers modules,
official image docs, engine-shipped defaults):

| Provider | Conventional start (host side) | Discovered endpoint | Credential default |
|---|---|---|---|
| SQLite / DuckDB / JSON / InMemory / SqliteVec | none — embedded | — | — |
| PostgreSQL | `-e POSTGRES_PASSWORD=postgres` | `localhost:5432` | `postgres`/`postgres`, database `Koan` created on first write |
| MySQL | `-e MYSQL_ROOT_PASSWORD=mysql` | `localhost:3306` | `root`/`mysql`, database `Koan` created on first write |
| SQL Server | `-e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=Your_password123` (Microsoft docs default) | `127.0.0.1,1433` | `sa`/`Your_password123`, catalog `Koan` created on first write |
| CockroachDB | `start-single-node --insecure` | `localhost:26257` | `root`, no password, database `Koan` created on first write |
| MongoDB | `docker run mongo` (auth off by default) | `localhost:27017` + `MONGODB_URLS` env | none |
| Redis | `docker run redis` (auth off by default) | `localhost:6379` + `REDIS_URLS` env | none |
| Couchbase | init cluster with `Administrator`/`password`, node renamed to `127.0.0.1` | `couchbase://localhost` + `COUCHBASE_URLS` env | `Administrator`/`password` |
| CouchDB | `-e COUCHDB_USER=admin -e COUCHDB_PASSWORD=password` | `localhost:5984` | `admin`/`password`, then `COUCHDB_USER`/`COUCHDB_PASSWORD` env |
| Firebird | `-e FIREBIRD_ROOT_PASSWORD=masterkey` | `localhost:3050` | engine-shipped `SYSDBA`/`masterkey`, `koan.fdb` created on first write |
| Elasticsearch | single-node with security disabled (ES 9) | `localhost:9200` | none |
| OpenSearch | single-node with security plugin disabled | `localhost:9200` | none |
| Qdrant | `docker run -p 6333:6333` | `localhost:6333` | none |
| Weaviate | anonymous local | `localhost:8080` | none |
| Milvus | three-container standalone (etcd + minio + milvus) per official deploy guidance | `localhost:19530` | none |
| Chroma | `docker run -p 8000:8000 chromadb/chroma` | `localhost:8000` | none |
| PgVector | pgvector-enabled Postgres + PostgreSQL record connector | rides the Postgres source | Postgres layering |
| RedisVector | redis-stack-server (search module) + shared Redis connection | rides the Redis connection | Redis layering |
| MongoAtlasVector | cloud service | config-required by nature | config-required |

## Defects found and fixed (2026-08-30)

1. **Relational four refused fresh servers.** PostgreSQL, MySQL, SQL Server, and CockroachDB
   discovery health checks attached the `Koan` database, which a vanilla container does not have —
   the same defect class as Firebird. Two-part fix: (a) each discovery health check now treats
   "database does not exist" as healthy — the server answered and the credentials work, and managed
   lifecycle creates the database (`3D000` for Npgsql; MySQL error 1049; SqlClient 4060 or
   18456/State 38, which is how SQL Server surfaces the same refusal); (b) each repository's
   provisioning path opens through `OpenOrCreate`, creating the database against the server's
   always-present maintenance database (`postgres` / server default / `master`) before the first
   schema DDL.
2. **SQL Server discovery candidates stalled.** `localhost` candidates hit SqlClient's dual-stack
   resolution and hung in pre-login handshake (15s connect timeouts observed). Discovery now
   composes the numeric loopback `127.0.0.1,1433`, and the SQL Server discovery budget is 30s
   because login latency over Docker Desktop was observed between 150ms and 15s across attempts.
3. **Couchbase had no credential default and no options configurator.** Credentials now layer
   configuration keys → `COUCHBASE_USERNAME`/`COUCHBASE_PASSWORD` env → `Administrator`/`password`
   (the official docs convention, and the same defaults discovery already health-validated with).
   A `CouchbaseOptionsConfigurator` now resolves `auto` through discovery; discovery's normalize
   strips the web-console port (8091 is not an SDK bootstrap port — a port-qualified
   `couchbase://host:8091` never receives a config stream) and the documented single-node setup
   renames the node to `127.0.0.1` before cluster init.
4. **Milvus post-load upsert race.** On freshly provisioned standalone deployments the upsert can
   arrive while the collection is momentarily not serviceable (provider code 1804). The write path
   now retries within the visibility window by re-awaiting the load — the same bounded barrier the
   adapter's Sync/Ensure use — and surfaces the provider code on `MilvusRejectedException` for
   anything else.

## Zero-config live matrix (bare consumers, 2026-08-30)

Every networked provider ran a bare consumer built from repository source — package-reference
equivalent, no configuration, server on its conventional address:

`ZEROPOSTGRES|PASS` · `ZEROMYSQL|PASS` · `ZEROSQLSERVER|PASS` · `ZEROCOCKROACH|PASS` ·
`ZEROMONGO|PASS` · `ZEROREDIS|PASS` · `ZEROCOUCHBASE|PASS` · `ZEROELASTICSEARCH|PASS` (ES 9.4.3,
the suite-pinned line) · `ZEROOPENSEARCH|PASS` · `ZEROQDRANT|PASS` · `ZEROWEAVIATE|PASS` ·
`ZEROPGVECTOR|PASS` · `ZEROREDISVECTOR|PASS` (redis-stack) · `ZEROCHROMA|PASS` ·
`ZEROCOUCHDB|PASS` · `ZEROFIREBIRD|PASS`.

Embedded floors need no server. `Milvus` application zero-config holds; its resource is the
official three-container standalone topology (the suite's own pinned environment proves the
adapter against the real cluster). `MongoAtlasVector` is a cloud service — config-required by
nature. Not zero-config-proven here: linux/arm64 (PMC-051).

## Boundaries

- The guarantee is compose-and-run: an unreachable server fails correctively at the operation
  (concrete-default adapters) or at startup with a corrective naming the remedy (`auto` adapters
  whose discovery finds nothing) — silence and silent fallback are never the answer.
- `AUTO`-sentinel adapters refuse when discovery finds nothing; concrete-default adapters fail at
  the operation. Both postures are honest; mixing them is documented per adapter.
- Default credentials are development defaults, matching prior art; production deployments set
  credentials explicitly and the layering always prefers their configuration.
