---
type: REFERENCE
domain: storage
title: "Entity cache"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-23
framework_version: v1.0.0
validation:
  status: not-yet-tested
  scope: docs/capabilities/state/cache.md
---

# Entity cache

Serve repeated Entity reads from a selected cache tier while `Get`, `Save`, and `Remove` remain the
application vocabulary and the Data store remains true.

## You need

| Piece | Package | Note |
|---|---|---|
| Cache policy and process-memory floor | `Sylin.Koan.Cache` | annotate the Entity with `[Cacheable(...)]` |
| Durable local tier (optional) | `Sylin.Koan.Cache.Adapter.Sqlite` | replaces the memory Local election and survives restart |
| Shared Redis tier (optional) | `Sylin.Koan.Cache.Adapter.Redis` (not assessed) | adds a remote tier and peer invalidation |

## The constraint box

> **The constraint:** Cache never becomes the source of truth. Fresh-or-miss is the default; stale
> serving must be bounded explicitly, and any value influenced by tenant or caller identity must
> carry that boundary in its key. A cache cannot repair a missing index or an unbounded query.

## Choose by topology

| Need | Variant | What it means |
|---|---|---|
| Cheapest repeated reads in one process | built-in memory floor | nothing else to operate; restart clears it |
| Local cache that survives restart | SQLite adapter | one durable local tier, still single-node |
| Coherence across several application nodes | Redis adapter (not assessed) | another service plus bounded best-effort peer invalidation |

## Leaves

- **Build and invalidation proof:** [make repeated reads fast](../../recipes/make-repeated-reads-fast.md)
- **Policy contract:** [cache reference](../../reference/data/cache.md)
- **Package contract:** runtime mechanics and honest limits:
  [Cache README](https://github.com/sylin-org/koan-framework/blob/main/src/Koan.Cache/README.md)

Measure first. If the underlying read is unbounded or poorly indexed, fix that before adding a stale
copy of the problem.
