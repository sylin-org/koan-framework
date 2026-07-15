---
uid: reference.modules.Koan.data.mongo
title: Koan.Data.Connector.Mongo - Technical Reference
description: MongoDB adapter for Koan data.
since: 0.2.x
packages: [Sylin.Koan.Data.Connector.Mongo]
source: src/Koan.Data.Connector.Mongo/
---

## Contract

- The document adapter declares `DataCaps.Query.ProviderBoundedPaging` and applies numbered pages with
  MongoDB `Skip`/`Limit` before candidate documents are materialized into application memory.

## Provider-bounded streaming

- `AllStream` and `QueryStream` are coordinated as lazy numbered pages by Data.Core.
- `batchSize` is the maximum Koan-visible candidate page, not a promise about opaque MongoDB driver
  buffers.
- Every caller-requested stream sort component must be a top-level, non-nullable `bool`, `byte`,
  `sbyte`, `short`, `ushort`, or `int` member. Every other caller sort, including an explicit Entity
  identifier sort, rejects before provider I/O. Data.Core appends the usual string Entity identifier
  only as an opaque provider-stable tie-breaker; that is not a CLR or cross-provider collation promise.
- Offset paging is not snapshot isolation and does not provide mutation-safe traversal, resume tokens,
  or a public cursor.

## Configuration

- Connection URI, database/collection naming per conventions.

## References

- [DATA-0107 provider-bounded Entity streams](../../../../docs/decisions/DATA-0107-provider-bounded-entity-streams.md)
- [Entity access and streaming](../../../../docs/guides/data/entity-access-and-streaming.md)

