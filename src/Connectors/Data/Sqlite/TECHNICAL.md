---
uid: reference.modules.Koan.data.sqlite
title: Koan.Data.Connector.Sqlite - Technical Reference
description: SQLite gold-reference adapter for managed and externally mapped data.
packages: [Sylin.Koan.Data.Connector.Sqlite]
source: src/Connectors/Data/Sqlite/
last_updated: 2026-07-28
---

## Execution model

SQLite has two deliberate repository paths selected once at repository construction:

- managed compatibility storage uses `Id + Json`, including framework-managed fields and polymorphic Entity JSON;
- an explicit `Map<TEntity>` uses the immutable compiled mapping plan and the declared physical container.

There is no legacy/V2 bridge. Both paths share one connection manager, query receipt contract, capability
declaration, atomic batch boundary, and source route. Warm mapping accessors and mapping-use decisions are compiled
and cached by the host; repository operations do not reflect over mapped members.

## Source integration

The adapter exposes registered SQL record/scalar operations and provider-neutral inspection of tables and views.
Container continuations are bounded, opaque after Data wraps them, and resumable. Sampling reads at most `take + 1`
native rows so it reports `Complete` versus `ProviderLimit` truthfully.

`SqlOperationBinding` is opaque. A registered SQL operation therefore selects a configured read lane. SQLite opens
that lane's connection and enables native `query_only` before executing the command; an effective write fails at
the provider boundary. Connection strings remain outside public plans and diagnostics.

## Mapping

An explicit map supports:

- one scalar or composite identity;
- provider-generated single-column identity;
- scalar physical names;
- structured object values stored as SQLite JSON text;
- scalar logical values at nested JSON paths; and
- symmetric codecs compiled by Data's mapping plan.

The same physical binding is used for hydration, parameters, filters, order expressions, conditional replace, and
writes. Sibling nested-path writes use `json_set` against the existing value, preserving unrelated legacy fields.
Whole-object bindings replace the whole declared object by design.

SQLite accepts an empty mapping namespace or `main`. Explicit mappings reject framework-managed row fields and
ambient partitions until those concerns have explicit physical bindings; this is a fail-closed boundary, not a
fallback to a shared container.

## Query guarantees

Supported Filter AST operations are lowered through `Koan.Data.Relational`. Filter, fully handled sort, numbered
page, and exact-count receipts reflect only provider-executed work. Provider-bounded candidate reads request one
extra row to prove whether the bound was exceeded. Collection operations use SQLite JSON functions.

Raw Entity queries are an explicit escape hatch. Registered named SQL is preferred for stable application intent
and is independently bounded by Source Integration limits.

## Schema and policy

Managed sources with `AutoCreate` create the required table on first use. External sources perform validation only
and never create, alter, or drop a physical object. `Access: ReadOnly` is enforced by Data before repository
construction or provider I/O. The two decisions are independent.

## Connection ownership

File connections use Microsoft.Data.Sqlite pooling. The host records only the exact pool groups it uses and clears
them on disposal. Private and named memory targets receive source-isolated shared-memory identities plus one
host-owned keeper connection, so per-operation connections observe the same database for that host lifetime.
Connection and provenance output are redacted.

## Limits

- Repository operations are buffered; Entity streaming is coordinated as bounded numbered pages.
- Offset paging is not snapshot isolation or a resumable cursor.
- Explicit mapped containers do not combine with ambient partitions or managed row fields.
- SQLite attached databases are not inferred from mapping namespaces.
- Provider exceptions surface directly; commands are not replayed after failure.
