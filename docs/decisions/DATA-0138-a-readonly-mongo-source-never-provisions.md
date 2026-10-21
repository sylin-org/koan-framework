---
id: DATA-0138
slug: a-readonly-mongo-source-never-provisions
domain: Data
status: Accepted
date: 2026-10-21
title: A ReadOnly MongoDB source never creates a collection or an index
related:
  - DATA-0137
---

# DATA-0138: A ReadOnly MongoDB source never creates a collection or an index

## Context

`DataSourcePlan` says a ReadOnly source forbids `SchemaOrAdmin` effects, and the MongoDB README says ReadOnly rejects
every mutation before provider I/O. `MongoSchema.EnsureCore` read only the storage lifecycle: on a Managed source it
created a missing collection and every declared index, and `MongoRepository` ensures the schema before every operation,
reads included. A reporting source configured ReadOnly therefore changed the database on its first read; with
read-only credentials, the usual reason to choose ReadOnly, `createIndexes` failed and every operation on the entity
failed with it. PostgreSQL never provisions under ReadOnly; it only validates. Measured against a local MongoDB 8.3
before this change: a `Get` on a ReadOnly source over a missing collection created it, and over an existing collection
created the declared `code_1` index. Ledger NK-030, NK-131.

## Decision

On a ReadOnly source, `MongoSchema` performs no lifecycle mutation:

1. A missing collection throws `InvalidOperationException` naming the collection, the source and the remedy (create it
   outside Koan, or provision through a ReadWrite source). Nothing is created.
2. An existing collection is used as it is; no index is created or checked.

Managed + ReadWrite and External behave as before.

## Compatibility

Behavioural on MongoDB ReadOnly sources: the first operation over a missing collection now fails instead of creating
it, and declared indexes are no longer created through a ReadOnly source. Provision them through a ReadWrite source or
outside Koan. No public signature or stored data changes.

## Evidence

`MongoReadOnlySourceSpec` (Connector.Mongo, against a local `mongod` 8.3): a missing collection is refused and the
database stays empty; an existing collection is read and keeps only `_id_`. Both failed before this change.
