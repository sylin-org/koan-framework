---
uid: reference.modules.Koan.data.redis
title: Koan.Data.Connector.Redis - Technical Reference
description: Redis adapter for Koan data.
since: 0.2.x
packages: [Sylin.Koan.Data.Connector.Redis]
source: src/Koan.Data.Connector.Redis/
---

## Contract

- Key-value/document patterns with limited query semantics and caller-visible numbered paging.
- The adapter does not declare `DataCaps.Query.ProviderBoundedPaging`; its current scan path cannot
  prove that a requested page is applied before the keyspace is traversed.

## Streaming boundary

- `AllStream` and `QueryStream` fail correctively with `QueryStreamRejectedException` before yielding;
  there is no complete-result materializing fallback.
- Use `All`/`Query` only for known-small sets. Use `FirstPage`/`Page` to limit the result returned to
  application code, without inferring a provider-side scan bound.
- A later incremental Redis implementation must earn a separate capability claim through shared
  conformance before these Entity streams become available.

## Configuration

- Endpoints, SSL, timeouts, naming and TTL policies.
- A named source may select its own endpoint and logical database. Koan shares one connection multiplexer per
  distinct endpoint for the host lifetime; the default DI multiplexer remains the shared transport for cache and
  coherence modules.

## Key conventions

- Keys should be prefixed by area and entity type (e.g., `app:{tenant}:Cart:{id}`) when multi-tenant.
- Keep separators consistent; include a version segment when value formats evolve.

## Serialization

- Default JSON with a version field for evolution; consider compression for large payloads.
- Backward compatibility: support older versions during rolling upgrades; add non-breaking fields.

## TTL and eviction

- Configure per-entity TTLs when appropriate; avoid global expirations for critical data.
- Monitor eviction via metrics; ensure memory headroom under expected load.

## Operations

- Health: an available Redis package stays non-critical until it wins default election or a runtime operation selects
  one of its sources. Active sources receive a `PING` round-trip through the same pooled endpoint used by Entity work.
- Metrics: connection pool size, operation latency, error rates.
- Logs: key patterns only (no raw values); redact sensitive data.

## References

- [DATA-0107 provider-bounded Entity streams](../../../../docs/decisions/DATA-0107-provider-bounded-entity-streams.md)
- [Entity access and streaming](../../../../docs/guides/data/entity-access-and-streaming.md)

