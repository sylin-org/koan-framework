---
id: DATA-0148
slug: every-store-holds-a-koan-store-marker
domain: Data
status: Accepted
date: 2026-10-21
title: Every store holds a __koan_store marker, and two endpoints are one store exactly when they read the same one
related:
  - DATA-0125
  - DATA-0143
  - DATA-0147
  - DATA-0111
---

# DATA-0148: Every store holds a `__koan_store` marker, and two endpoints are one store exactly when they read the same one

## Context

DATA-0143 made a `Move` prove its endpoints are different stores, through locks and server-reported identity, and
proposed a store marker for what those cannot reach: a JSON directory, a URI or shared-cache SQLite database, a sharded
MongoDB cluster behind two routers, the adapters without a probe. Cutover's physical-distinctness check compared
connection identities only, so a target whose connection spelled the active database another way (a `\\?\` path
prefix, `localhost` for `127.0.0.1`) was accepted and the promotion copied the database onto itself. Ledger NK-305.

## Decision

Owner decision, 2026-10-21: option A of DATA-0143.

1. **One marker per store.** Each store holds one reserved `__koan_store` object in the namespace the source addresses,
   holding one random id (32 hex characters) written on first use. `StoreMarker.Name` in `Koan.Data.Abstractions`
   reserves the name; KoanRS reserves the same one.
2. **Two endpoints are one store exactly when they read the same marker.** `IDataAdapterFactory.ReadStoreMarker(services,
   source, create, ct)` returns the id, creating it first when `create` is set and the source may create a table.
   `null` (the default) means no marker is readable.
3. **Creating it is DDL.** It is created under exactly the consent tables are: a Managed + ReadWrite source, and for the
   relational adapters `DdlPolicy=AutoCreate` and the production DDL gate (`AllowProductionDdl`,
   `RelationalDdlGate.MayCreate`). A ReadOnly or External source, or one whose policy forbids DDL, reads the marker and
   never creates it, nor the file, directory, schema or collection that would hold it.
4. **First use is the first question.** A store gains its marker the first time a `Move` or a cutover plan asks for it.
   Copy and Mirror delete nothing they wrote and do not ask.
5. **Move and cutover ask the same question** (`StoreMarkers.Compare`). Each endpoint is asked with creation allowed;
   if only the second could create it, the first is read again, so one store reached by a writable and a read-only
   source still reads as one.
   - Proven the same store: a `Move` is refused unless the containers differ (two partitions of one store remain
     distinct, as DATA-0125 says); a cutover is refused with the target-policy blocker.
   - Proven different: the `Move` runs, without asking the adapter's lock or server proof.
   - No marker readable on either side: the existing proofs decide. A `Move` falls back to `ProveDistinctStores` and is
     otherwise refused; a cutover keeps comparing connection identities (DATA-0147), as it did before.
   Different providers are never one store, so they are not asked.
6. **Where it lives.**
   - **SQLite**: table `__koan_store (slot INTEGER PRIMARY KEY CHECK (slot = 1), id TEXT NOT NULL)` in the database the
     connection opens, written with `CREATE TABLE IF NOT EXISTS` and `INSERT OR IGNORE`, so concurrent writers agree. An
     in-memory database keeps one per source; an empty Data Source (a private temporary database) has none.
   - **PostgreSQL**: the same table in the route's schema (`SearchPath`), with `CREATE SCHEMA IF NOT EXISTS` and
     `ON CONFLICT DO NOTHING`, so one database under two schemas holds two markers, as it holds two sets of tables.
   - **MongoDB**: the document `{ _id: "store", id }` in the `__koan_store` collection of the route's database, written
     by an upsert with `$setOnInsert`. A read never creates the collection.
   - **JSON**: the file `__koan_store` (no `.json` extension) in the source's directory, created with `CreateNew`.
   - **InMemory**: an id held beside the source's stores for the host's lifetime.
7. **The marker is never an entity set.** The SQLite, PostgreSQL and MongoDB inspectors leave it out of container
   listings and refuse to resolve it, so source integration, admin and diagnostics surfaces and cutover's target and
   source inventories never see it. JSON layouts read only `.json` files, so no entity set contains it. Health and
   readiness do not list containers, and backup inventories entity types, not stores, so neither can report it.

## Adapters without a marker

SQL Server, MySQL, CockroachDB, DuckDB, Firebird, Redis, Couchbase, CouchDB, and every vector adapter keep the default
(`null`). Their behaviour is unchanged: a `Move` between two of their stores still needs `ProveDistinctStores`, which
none of them implement, so it is refused. CockroachDB could reuse `NpgsqlStoreMarker` and DuckDB the SQLite shape; they
were left out because no server or suite here exercises a cross-store Move for them.

## Compatibility

Behavioural, and nothing that worked stops working:

- Koan now creates one small object in stores where it may create tables: a `__koan_store` table in SQLite and in the
  PostgreSQL schema, a collection in MongoDB, a file in a JSON directory. It appears in native tools, not in Koan's
  listings. It is created only when a `Move` or a cutover plan asks, never by an ordinary read or write.
- A `Move` between two JSON directories that both exist, or between SQLite files named through a URI data source, now
  runs when the markers differ; before, it was refused as unproven.
- A cutover whose target reaches the active database through another spelling is now refused before any copy; before,
  it was accepted.
- A `Move` refused as unproven now names the marker in its message and how to let each source create it.
- No existing signature changes. `IDataAdapterFactory` gains an optional member with a default; `RelationalDdlGate`
  gains `MayCreate`.

## Evidence

All suites are real hosts. Every spec that writes a marker failed before this change; the ReadOnly specs guard the
consent rule and could not fail before, since nothing created a marker then.

- `SqliteStoreMarkerSpec`: a Move to a URI naming another file runs (refused before); a Move to a URI naming the same
  file is refused with every row kept; `\\?\`, URI and plain spellings read one marker and another file reads its own;
  a ReadOnly source creates neither marker nor file; `DdlPolicy=Validate` creates nothing; the inspector neither lists
  nor resolves the marker.
- `JsonStoreMarkerSpec`: a Move between two existing directories runs (refused before) and leaves a marker in each; a
  `\\?\` spelling of one directory is refused; a ReadOnly source creates neither marker nor directory.
- `PostgresStoreMarkerSpec` (PostgreSQL 18.6) and `MongoStoreMarkerSpec` (MongoDB 8.3): `localhost` and `127.0.0.1`
  read one marker, another schema or database reads its own, a ReadOnly source creates nothing, the inspector neither
  lists nor resolves it.
- `InMemoryStoreMarkerSpec`: one marker per source, none for a ReadOnly source.
- `CutoverStoreMarkerSpec`: a target spelled `\\?\` plus the active database's path is refused by the marker (accepted
  before); planning a promotion between two files writes a marker in each and the plan still runs, so neither inventory
  counts it.
- The DATA-0125 and DATA-0143 transfer specs and the cutover suites pass unchanged.
