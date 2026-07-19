---
uid: reference.modules.Koan.data.json
title: Koan.Data.Connector.Json - Technical Reference
description: JSON file storage adapter for Koan data.
packages: [Sylin.Koan.Data.Connector.Json]
source: src/Connectors/Data/Json/
last_updated: 2026-07-17
---

## Contract

- Local file storage semantics; simple filtering; limited concurrency.
- The repository creates its configured directory on first use.
- Package presence means provider availability, not provider selection.
- The adapter does not declare `DataCaps.Query.ProviderBoundedPaging`; current reads materialize the
  file-backed source before caller-visible paging is applied.

## Streaming boundary

- `AllStream` and `QueryStream` fail correctively with `QueryStreamRejectedException` before yielding;
  there is no complete-result materializing fallback.
- Use `All`/`Query` only for known-small files. Use `FirstPage`/`Page` to limit the result returned to
  application code, without inferring a provider-side read bound.
- A later incremental file implementation must earn a separate capability claim through shared
  conformance before these Entity streams become available.

## Persistence and corruption behavior

- Writes are serialized per physical aggregate file inside one Koan process.
- A complete snapshot is written beside the target and then moved over it; cancellation or serialization failure does
  not deliberately truncate the last complete file.
- Invalid JSON throws a corrective `InvalidDataException` containing the affected path. The repository does not
  reinterpret corrupt persisted state as an empty aggregate.
- There is no cross-process writer coordination, transaction log, crash recovery protocol, or incremental update path.
  Use a database connector when those guarantees matter.

## Configuration

- Adapter default: `Koan:Data:Json:DirectoryPath`.
- Per source: `Koan:Data:Sources:{source}:json:DirectoryPath`.
- A configured source selects JSON with `Koan:Data:Sources:{source}:Adapter=json`.

## Health and readiness

`JsonHealthContributor` uses the data pillar's selection-aware health base. JSON participates in
readiness when it wins default adapter election or appears in
an observed entity configuration. Otherwise it reports `Unknown`, is non-critical, and performs no
filesystem work.

For every active source, the contributor resolves the source-specific directory through
`AdapterConnectionResolver`, creates it as `JsonRepository` would, and verifies write/delete access.
Probe files use unique names and are removed when the probe closes. A selected source that cannot be
provisioned or written reports `Unhealthy`; Koan does not substitute another adapter.

## References

- [DATA-0107 — provider-bounded Entity streams](https://github.com/sylin-org/Koan-framework/blob/main/docs/decisions/DATA-0107-provider-bounded-entity-streams.md)
- [Entity access and streaming](https://github.com/sylin-org/Koan-framework/blob/main/docs/guides/data/entity-access-and-streaming.md)

