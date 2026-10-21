---
id: DATA-0125
slug: transfer-endpoints-are-compared-as-physical-places
domain: Data
status: Accepted
date: 2026-10-21
title: Transfer endpoints are compared as the physical places they reach
related:
  - DATA-0077
  - DATA-0079
  - DATA-0094
  - DATA-0113
  - ARCH-0101
---

# DATA-0125: Transfer endpoints are compared as the physical places they reach

## Context

`Copy`, `Move` and `Mirror` take two endpoints, `From(source | adapter, partition)` and `To(...)`. Each axis left
unset inherits the ambient `EntityContext`. The guard that stopped a transfer onto itself compared the two declared
triples as ordinal strings, after both routes had been resolved and their bindings thrown away.

Many pairs differ as written and are one container:

- `From(source: "Default").To()`, or `From(adapter: "sqlite").To()` when Default is SQLite: both route to the Default
  plan;
- `From(source: "preview").To(source: "Preview")`: source names compare without case;
- `To(source: "Archive")` where `Archive` declares an adapter but no connection: the adapter falls back to the default
  connection (NK-010), and the record plane adds no source particle to the table name;
- two sources whose connection strings spell one file or database differently;
- `using (EntityContext.Partition("hot")) Move().From(partition: "hot").To()`: `To()` inherits `hot`;
- `From(partition: "Archive").To(partition: "archive")`: one table on SQLite, SQL Server and MySQL on Windows, on
  PostgreSQL after its token policy lower-cases partitions, and one file for JSON on a case-insensitive file system;
- `From(source: "Documents").To()` after `Documents` was promoted to the default route.

A `Move` over any of them upserted every selected row onto itself and then deleted it: the selection was gone and the
result reported `CopiedCount == DeletedCount`. Measured before this change on SQLite (seven endpoint spellings), and
through the adapter-surface kit on SQLite, Json and InMemory for the ambient-partition case: every row was deleted. A
`Mirror` over such a pair reported success after rewriting every row onto itself. Ledger NK-006 and NK-048.

## Decision

**A transfer resolves both endpoints to the physical place they reach before any provider work, and treats two
endpoints that reach one container as one place.**

1. **A place is provider, store and container.** Under each endpoint's applied context, Data resolves the provider
   election (`AdapterResolver`, the same decision the repository uses), the store the routed source opens, and the
   container the provider names for the Entity and the effective partition (`ResolveStorage`, the same name the
   repository uses).
2. **The adapter names the store.** `IDataAdapterFactory.ResolveStoreIdentity(services, source)` is a new optional
   member that returns a one-way identity of the physical store the source opens, derived from the route the
   repository opens. SQLite returns the anchored file path (case-folded) or its in-memory key; PostgreSQL the host,
   port, database and search path; MongoDB the servers and routed database; JSON the resolved directory; InMemory the
   source name its state is keyed by. Credentials and pool options are not part of it. It performs no I/O.
   `DataSourceRegistry.IdentityOf` reduces the value the way plan connection identities are reduced, so no raw
   connection material is kept.
3. **Unknown is assumed shared.** An adapter that does not implement the member falls back to the source plan's
   connection identity, which compares declarations: two sources with no connection of their own (`none`) are one
   store, a declared connection and `none` are different stores.
4. **Containers compare without case.** Several stores resolve names that differ only by case to one object, so Data
   refuses such a pair on every adapter. On a store that keeps them apart (InMemory, MongoDB) that refuses a transfer
   that would have been safe; the refusal names the reason.
5. **Same place, by verb.** A `Move` or `Mirror` throws `InvalidOperationException` before the source is read and
   before the mutation lease is taken. A `Copy` writes nothing and returns a zero result whose `Warnings` say so. The
   message names both declared endpoints, the shared container and provider, and the ways two spellings meet.

A declared-identical `Move` or `Mirror` (`From(partition: "a").To(partition: "a")`) used to return a zero result; it
now throws, like every other same-place pair. A declared-identical `Copy` still returns zero, now with a warning.

## Why the adapter, not a connection-string comparison

The plan's `ConnectionIdentity` hashes the source definition's own connection string. It is `none` for the Default
source when the application configures the adapter-level key, and for any source that relies on the adapter's
default; it differs for two spellings of one database; and it omits the MongoDB database and the PostgreSQL search
path, which decide where a container lives. The repository's route is the only place that knows which connection
actually opens, so the adapter that builds the route answers. Adapters other than SQLite, PostgreSQL, MongoDB, JSON
and InMemory keep the plan fallback until they implement the member.

## Guarantee and correction

- A `Move` cannot delete rows it wrote onto their own container, through any spelling of source, adapter, connection
  or partition that the adapter resolves to one store and the provider names as one container.
- A `Mirror` cannot report a reconciliation that rewrote a container onto itself.
- The correction: name a destination that reaches a different store or partition. An axis left unset inherits the
  ambient context; a source without its own connection shares its adapter's default store.

## Concepts

One adapter-facing member, `ResolveStoreIdentity`, with a default of `null`. Application code gains nothing new.

## Evidence

- `SqliteTransferPlaceSpec` (Connector.Sqlite): explicit Default, adapter spelling, source name in another case, a
  source whose connection spells the same file differently, a source without a connection, inherited ambient
  partition, partition case variant. Each Move left zero rows before this change and now throws with every row kept.
  Mirror throws; Copy returns zero with a warning; a Move between two files still moves and drains the origin.
- `AdapterTransferSpecsBase` gains same-partition, ambient-partition, case-variant, Mirror and Copy legs. They fail
  before the change on SQLite, InMemory and Json and pass after. PostgreSQL, MongoDB, Redis, SQL Server and Couchbase
  inherit them; they were compiled, not run, because no container runtime was reachable.
- `EntityTransferDslSpec` (Data.Core): the ambient-partition Move is refused before any provider query, and a
  declared-identical Move and bidirectional Mirror throw.
