---
uid: reference.modules.Koan.data.redis
title: Koan.Data.Redis - Technical Reference
description: Redis adapter for Koan data.
since: 0.2.x
packages: [Sylin.Koan.Data.Redis]
source: src/Koan.Data.Redis/
---

## Contract

- Key-value/document patterns; limited query semantics; paging where meaningful.

## Configuration

- Endpoints, SSL, timeouts, naming and TTL policies.

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

- Health: `PING` round-trip for readiness; simple get/set smoke checks.
- Metrics: connection pool size, operation latency, error rates.
- Logs: key patterns only (no raw values); redact sensitive data.

## References

- DATA-0061 paging/streaming: `/docs/decisions/DATA-0061-data-access-pagination-and-streaming.md`
