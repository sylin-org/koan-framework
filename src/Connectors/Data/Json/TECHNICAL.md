---
uid: reference.modules.Koan.data.json
title: Koan.Data.Connector.Json - Technical Reference
description: Bounded local JSON Entity persistence for Koan Data.
packages: [Sylin.Koan.Data.Connector.Json]
source: src/Connectors/Data/Json/
last_updated: 2026-07-29
---

## Contract

The connector is an automatic Data floor with provider identity `json` and priority `0`. Package presence makes it
available; source election or runtime use activates it. Application access remains the provider-neutral Entity API.

`JsonDataOptions` exposes one provider decision: `DirectoryPath`, defaulting to `data`.

- Global: `Koan:Data:Json:DirectoryPath`
- Per source: `Koan:Data:Sources:{source}:json:DirectoryPath`
- Selection: `Koan:Data:Sources:{source}:Adapter=json`

The physical name is the Entity root plus Koan's standard partition token. One file contains a JSON array of Entity
objects. Entity-family type identity and framework-managed fields are stored by the shared Data Core codecs.

## Runtime ownership

One DI-owned registry admits at most 1,024 canonical file paths per host. Each admitted path owns one immutable live
snapshot and one write gate, so different repositories or source aliases cannot hold divergent views of the same
file. Windows path identity is case-insensitive; other platforms use ordinal identity. Resolution is lexical and does
not attempt filesystem/symlink identity discovery.

Factory creation and pure scope diagnostics resolve plans only; they perform no filesystem I/O. Warm reads retrieve a
stored record string from the immutable snapshot and materialize a fresh Entity. Bounded scans stop dictionary
enumeration at the candidate ceiling rather than first snapshotting the whole collection.

Changed records are serialized once. A mutation copies the immutable key/string index, changes only the requested
entries, writes the complete array to a same-directory temporary file, checks cancellation, replaces the target, and
then publishes the candidate. Failed serialization, write, cancellation, or replacement leaves the published snapshot
and last complete target unchanged.

The file is intentionally aggregate-based: unchanged record strings are reused, but every successful mutation still
replaces the complete file.

## Bounds and persisted-input validation

- Maximum canonical files per host: 1,024.
- Maximum UTF-8 bytes per Entity file: 64 MiB, enforced before read materialization and before write.
- Every array member must be an object assignable to the file's Entity root.
- Every persisted identity must be unique.
- One canonical file cannot be interpreted by conflicting Entity root/key pairs in the same host.

Violations throw corrective errors naming the affected path and the remediation. Corrupt or ambiguous storage is never
treated as empty.

## Source policy and health

Managed/read-write reads may observe an absent file as empty; the first write or explicit ensure creates the directory
and file. Read-only and External routes never create storage. A read-only route requires its directory to exist;
External additionally requires the addressed Entity file.

Health is selection-aware. Inactive JSON reports `Unknown` and touches no disk. Active managed/read-write health creates
and probe-writes its directory. Active read-only/External health only verifies and enumerates an existing directory.

## Capabilities

The KeyValue family supplies LINQ/full-filter behavior and row/container/database isolation. JSON declares scan filter
execution plus `BulkUpsert` and `BulkDelete` because each bulk request is one physical replacement. It does not declare
atomic batch, fast remove, indexes, native string queries, or provider-bounded paging.

`AllStream` and `QueryStream` therefore throw `QueryStreamRejectedException` before yielding. Required atomic batches
also reject before execution. Physical compatibility maps reject because the provider owns its file shape.

## Durability boundary

Same-directory replacement protects the last complete file from ordinary serialization and write failures. It is not
an fsync/power-loss guarantee, transaction log, backup system, cross-process lock, or recovery protocol. The host cache
does not observe concurrent external edits. Select a database connector when those guarantees or larger stores matter.

The real-file connector ledger passes 34/34 across CRUD, bulk, detached writes, restart, polymorphism, corruption,
duplicate identity, byte/file bounds, canonical aliases, concurrent writes, policy, health, routing, partitions,
managed isolation, mapping decline, instructions, atomic decline, and streaming decline.
