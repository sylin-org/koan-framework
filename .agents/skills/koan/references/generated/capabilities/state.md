---
type: REFERENCE
domain: storage
title: "State and content"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-23
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-23
  status: passed
  scope: docs/capabilities/state.md - route table verified against leaf targets
---

# State and content

Cache, durable bytes, and media derivatives make three different promises. Cache promises speed
with an invalidation truth; files promise durable bytes with a lifecycle; media promises one
original with named derivatives. Annotate the Entity - do not architect storage beside it.

## Route by need

| The request says | Fetch |
|---|---|
| "this query is slow" / "cache this" | [cache](state/cache.md) |
| "let users upload files" / "store a document per Entity" | [Entity-owned files](state/entity-files.md) |
| "thumbnails / resized variants / format conversion" | [media derivatives](state/media-derivatives.md) |

## Standing constraints

- Cache invalidation truth is Entity-owned: ordinary Entity verbs keep the cache correct, so
  side-door writes are what break it.
- Media keeps one original and names its derivatives; the original is never overwritten.
- Entity-owned files ride the same Entity grammar - metadata beside your other entities, bytes in
  storage.

## Do not, at this level

- Do not store bytes in the Entity table when a storage connector is referenced.
- Do not hand-write thumbnail or conversion pipelines beside named recipes.

For the one-screen maturity view, see
[State and content in the capability map](../reference/capability-map.md#state-and-content).
