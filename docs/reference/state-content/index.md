---
type: REFERENCE
domain: storage
title: "State and content"
audience: [developers, operators, architects, ai-agents]
status: current
last_updated: 2026-07-22
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-22
  status: verified
  scope: cache, storage, and media capability entry points
---

# State and content

Use this pillar when Entity-backed application state needs faster reads, durable byte storage, or
named media derivatives. Cache, Storage, and Media have distinct semantics; sharing this navigation
does not make them interchangeable.

## Capability map

| Need | First expression | Canonical contract |
|---|---|---|
| Cache Entity reads while preserving ordinary Entity verbs | `[Cacheable(...)]` | [Cache](../data/cache.md) |
| Bind an Entity to durable byte storage | `[StorageBinding(...)]` on a `StorageEntity<T>` | [Storage](../storage/index.md) |
| Render and serve named image transformations | `[MediaRecipe(...)]` with a `MediaEntity<T>` | [Media](../media/index.md) |

References make providers available and `AddKoan()` compiles their plans. Configuration selects
profiles and explicit provider intent. An unavailable required provider, unsupported topology, or
invalid recipe rejects with a correction; Koan does not silently downgrade durability, replication,
or media behavior.

## Inspect and operate

- Startup reports available providers, selected cache/storage topology, and media recipes.
- `/health/ready` reflects participating dependencies rather than every referenced package.
- Runtime facts expose redacted provider, profile, and recipe decisions.
- Package READMEs own provider-specific durability, consistency, streaming, range, and deployment
  limits.

The supported 0.20 surface includes local Storage and Media. S3 and Data Backup are not supported 0.20
claims; check the [generated product surface](../product-surface.md) before choosing them.

## Deeper contracts

- [Cache package](../../../src/Koan.Cache/README.md)
- [Storage package](../../../src/Koan.Storage/README.md)
- [Media package](../../../src/Koan.Media.Core/README.md)
- [Product and package surface](../product-surface.md)
