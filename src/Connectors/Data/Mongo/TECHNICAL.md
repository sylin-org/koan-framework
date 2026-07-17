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

- A concrete Mongo connection string is authoritative.
- `auto` delegates to the shared health-checked discovery coordinator.
- The Mongo package declares a `mongo` → `mongodb` Zen Garden offering binding, but that metadata is
  inert unless the `Koan.ZenGarden` engine is referenced and activated.
- When active, Zen Garden contributes one automatic candidate. It does not short-circuit discovery;
  Mongo applies database/authentication parameters, validates reachability, and falls through when the
  candidate is unhealthy.
- Automatic precedence is Aspire, activated contributors, then runtime topology. All remain below
  concrete explicit configuration.
- The selected discovery method is emitted as a credential-redacted runtime fact; raw endpoints are not
  included in that fact.
- Availability is not readiness dependency: a referenced or configured named Mongo source stays non-critical until
  it wins default election or an Entity/Direct operation selects it. Active sources are probed through the same
  per-source client pool used by repositories; startup does not eagerly connect an unused optional source.

## References

- [DATA-0107 provider-bounded Entity streams](../../../../docs/decisions/DATA-0107-provider-bounded-entity-streams.md)
- [Entity access and streaming](../../../../docs/guides/data/entity-access-and-streaming.md)
- [ARCH-0114 layered capability activation](../../../../docs/decisions/ARCH-0114-layered-capability-activation.md)

