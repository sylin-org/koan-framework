---
uid: reference.modules.Koan.data.json
title: Koan.Data.Connector.Json - Technical Reference
description: Bounded local JSON Entity persistence for Koan Data.
packages: [Sylin.Koan.Data.Connector.Json]
source: src/Connectors/Data/Json/
last_updated: 2026-08-13
---

## Contract

The connector is an automatic Data floor with provider identity `json` and priority `0`. Package presence makes it
available; source election or runtime use activates it. Application access remains the provider-neutral Entity API.

`JsonDataOptions` exposes three provider decisions:

- `DirectoryPath`, default `data`;
- `Layout`, default `Aggregate`; and
- `IndividualFilePath`, default `{storage}/{id}.json`.

- Global: `Koan:Data:Json:DirectoryPath`
- Per source: `Koan:Data:Sources:{source}:json:{setting}`
- Selection: `Koan:Data:Sources:{source}:Adapter=json`

The physical storage name is the Entity root plus Koan's standard partition token. `Aggregate` stores that Entity set
as one JSON array file. `IndividualFiles` renders one relative path per identity and stores one JSON object there.
Entity-family type identity and framework-managed fields use the same shared Data Core codecs in both layouts.

An individual path contains exactly one `{id}` and at most one `{storage}` token. Both render through a UTF-8
percent-style segment codec whose literal alphabet is lowercase ASCII, digits, `_`, and `-`; this distinguishes values
that would otherwise collide on case-insensitive filesystems and prevents separator/traversal injection. Templates are
relative `.json` paths, are lexically contained beneath `DirectoryPath`, and reject unknown tokens, empty segments,
and traversal. Omitting `{storage}` claims the template for one Entity root/key pair and rejects partition use.

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

Individual layout retains no record snapshot. A fixed 64-stripe host-owned gate pool coordinates point mutations
without memory growing with record count. Reads reopen the addressed file, so external edits completed before a read
are visible in the same host. Scans derive the narrowest fixed search root/file name from the rendered template,
enumerate matching paths, validate each document's identity back to its canonical path, and stop materialization at
the requested candidate bound. A same-directory temporary file replaces only the addressed record. Remove never
deletes parent directories or siblings.

## Bounds and persisted-input validation

- Maximum cached Aggregate files per host: 1,024. IndividualFiles retains only a fixed 64-stripe gate pool.
- Maximum UTF-8 bytes per Entity file: 64 MiB, enforced before read materialization and before write. In Aggregate
  this bounds the set; in IndividualFiles it bounds one record.
- Every array member must be an object assignable to the file's Entity root.
- Every persisted identity must be unique and, in IndividualFiles, must map back to the file containing it.
- One canonical file cannot be interpreted by conflicting Entity root/key pairs in the same host.

Violations throw corrective errors naming the affected path and the remediation. Corrupt or ambiguous storage is never
treated as empty.

## Source policy and health

Managed/read-write reads may observe absent storage as empty; the first write creates the required path. Aggregate
ensure creates its set file; IndividualFiles ensure creates the source directory. Read-only and External routes never
create storage and require their source directory to exist. An External individual write additionally requires its
addressed Entity file to exist.

Health is selection-aware. Inactive JSON reports `Unknown` and touches no disk. Active managed/read-write health creates
and probe-writes its directory. Active read-only/External health only verifies and enumerates an existing directory.

## Capabilities

The KeyValue family supplies LINQ/full-filter behavior and row/container/database isolation. Both layouts declare
bounded-candidate scan filter execution. Aggregate declares `BulkUpsert` and `BulkDelete` because each bulk request is
one physical replacement. IndividualFiles intentionally omits those optimization claims and uses the KeyValue
family's pointwise batch behavior. Neither layout declares atomic batch, fast remove, indexes, native string queries,
or provider-bounded paging.

`AllStream` and `QueryStream` therefore throw `QueryStreamRejectedException` before yielding. Required atomic batches
also reject before execution. Physical compatibility maps reject because the provider owns its file shape.

## Durability boundary

Same-directory replacement protects the last complete addressed file from ordinary serialization and write failures.
It is not an fsync/power-loss guarantee, transaction log, backup system, cross-process lock, compare-and-swap, or
recovery protocol. Aggregate's host cache does not observe concurrent external edits; IndividualFiles observes disk on
each read but does not make an external writer participate in its in-process gate. Select a database connector when
those guarantees or larger stores matter.

The real-file connector ledger covers the Aggregate compatibility contract and focused IndividualFiles evidence for
placement, CRUD, external-edit visibility, extension data, path safety, partitions, and capability truth.
