---
id: DATA-0143
slug: a-move-needs-proof-its-endpoints-are-different-stores
domain: Data
status: Accepted
date: 2026-10-21
title: A Move needs proof that its endpoints are different stores; a store marker is proposed
related:
  - DATA-0125
---

# DATA-0143: A Move needs proof that its endpoints are different stores; a store marker is proposed

## Context

DATA-0125 refuses a transfer whose endpoints resolve to one container. Store identity comes from
`IDataAdapterFactory.ResolveStoreIdentity`, which every adapter derives from connection text: SQLite from the anchored,
upper-cased file path, PostgreSQL from host, port, database and search path, MongoDB from the server list and database,
JSON from the directory. Text can spell one store two ways that no normalization joins: `localhost` and `127.0.0.1`, a
`\\?\` path prefix, a directory link or junction, a short file name, a proxy port. The two identities then differ, the
places compare as distinct, and a `Move` copies every row onto itself and deletes it. Measured before this change: on
SQLite a Move to the same file spelled with a `\\?\` prefix, and on a local MongoDB 8.3 a Move to the same database
spelled `localhost` instead of `127.0.0.1`, each deleted every row. Ledger NK-305, confirmed in source
(`TransferPlace.IsSameAs`, d5fb846ae).

## Decision

1. **Different text is not proof.** A `Move` runs only when its endpoints are proven to be different containers:
   different providers, one store with different containers (DATA-0125), or two stores the adapter proves apart.
   Otherwise the `Move` throws `InvalidOperationException` before the lease and before any read, naming both sources
   and the remedy. `Copy` and `Mirror` delete nothing they wrote and keep running on an unproven pair.
2. **The adapter proves it from the store, without writing.** `IDataAdapterFactory.ProveDistinctStores(services,
   source, other, ct)` is a new member whose default is false. Any exception counts as not proven.
   - **SQLite**: an in-memory database is one per source and connection, which Koan keeps itself; a file that does not
     exist yet cannot be one that does; two existing files are asked through SQLite's own locking, `BEGIN IMMEDIATE` on
     each, which one file refuses to the second connection and two files grant to both. Both roll back. A URI data
     source is not proven.
   - **PostgreSQL**: `pg_try_advisory_xact_lock` on one key from each route, inside a transaction on each. Advisory
     locks are keyed by database, so the second is refused on one database and granted on another, whatever host,
     address or port reached it. It needs no privilege, works through transaction pooling and writes nothing. One
     database under different search paths is proven apart, since tables are schema-qualified.
   - **MongoDB**: `hello` on each route, which needs no privilege. Two writable primaries with different
     `topologyVersion.processId` values are different deployments; one process is one deployment whose databases
     differ by name. A router (`mongos`) or a member that is not the writable primary is not proof.
   - **InMemory**: each source keys its own store, so two identities are two stores.
   - **JSON**: a directory that does not exist yet is proven apart; two existing directories are not.
   - Every other adapter keeps the default, so a `Move` between two of its stores is refused.

## Proposed, needs an owner decision: a store marker

Locks and server-reported identities cover SQLite files, PostgreSQL and MongoDB replica sets and standalones. They
cannot cover a JSON directory, a URI or shared-cache SQLite database, a sharded MongoDB cluster behind two routers, or
the adapters without a probe (SQL Server, MySQL, Redis, Couchbase, CouchDB, DuckDB, Firebird, CockroachDB). The general
answer is a marker the store holds: on first use, Koan writes one random id into a reserved table, collection, key or
file of each store (for example `__koan_store` with a single row), and two endpoints are one store exactly when they
read the same marker. It is robust to every spelling, needs only the write access a Move already requires, and turns
"cannot tell" into "proven" for every adapter. It also creates an object in user databases that Koan does not create
today, so it is not implemented here. The owner decides whether to adopt it, which name it reserves per adapter, and
whether creating it falls under the same DDL consent as tables (`DdlPolicy`, `AllowProductionDdl`).

## Compatibility

Behavioural: a `Move` between two stores of one provider is refused unless the adapter proves them apart. On SQLite,
PostgreSQL, MongoDB and InMemory ordinary Moves between different files, databases or sources keep working; a JSON Move
into an existing directory, and a Move between two stores of any adapter without a probe, is now refused with a message
that points to Copy. The probes cost one round trip to each store per Move. A new optional interface member; no
existing signature or stored data changes.

## Evidence

- `SqliteTransferPlaceSpec`: a Move to the same file spelled with a `\\?\` prefix (a directory link off Windows) kept
  no rows before this change and is refused with every row kept after; a Move between two existing files is proven
  apart and runs; the earlier DATA-0125 legs pass.
- `MongoTransferStoreProofSpec` (local `mongod` 8.3): `localhost` against `127.0.0.1` lost every row before and is
  refused after; a Move to another database of the same server runs.
- `PostgresTransferStoreProofSpec` (spelling refused, another schema runs) compiles; it needs a server and was not run.
