---
id: DATA-0130
slug: variant-views-and-the-cache-honour-deletes
domain: Data
status: Accepted
date: 2026-10-21
title: A variant view deletes only its own variant, and a mass delete retires the entity's cache entries
related:
  - DATA-0124
  - DATA-0129
---

# DATA-0130: A variant view deletes only its own variant, and a mass delete retires the entity's cache entries

## Context

Two delete paths disagreed with the reads beside them.

- **Variant views (ledger NK-015).** `EntityVariantRepository` forwarded `Delete`, `DeleteMany` and the batch `Delete` to
  the root repository by identity. `Get` through the same view refuses a row of another variant, so reads and deletes
  disagreed: `Data<Anime,string>.Delete(mangaId)` returned `true` and the Manga was gone. Measured before this change on
  InMemory.
- **The Entity cache (NK-040).** `CachedRepository.DeleteAll` and `RemoveAll` only logged, and instructions passed
  through, so a `[Cacheable]` entity kept serving deleted rows until their TTL after `DeleteAll`, any `RemoveAll`
  strategy or a `Clear`. Measured before this change on InMemory for all five verbs.

## Decision

1. **A variant view proves membership before it deletes.** `Delete` and `DeleteMany` read the requested ids through
   the root under the variant's materialization target and delete only ids whose visible row is that variant. The
   view's batch queues its operations and replays them into the root batch at `Save`, dispatching a delete only for a
   member; another variant's id reports `Missing` in its builder position, as a missing id does (DATA-0124). The read
   goes through the root facade, so it also carries the root's read scope. Check-then-delete is not atomic; that
   residual is shared with every keyed facade delete.
2. **A mass delete retires the entity's cached entries.** Every entry the Entity cache decorator writes carries one
   entity tag, bound to the active segmentation like its key. `DeleteAll`, every `RemoveAll` strategy and every
   instruction that is neither a schema instruction nor labelled `Read` flush that tag after the store call, and after a
   failed one.

## Compatibility

- A delete through a variant view of another variant's id now returns `false` (or counts as not deleted, or reports
  `Missing`) and leaves the row. Variant deletes and batches with deletes take one extra read.
- A partition-scoped mass delete invalidates the entity's cached entries in every partition: a cache-miss cost, never a
  stale read. Entries written before this change carry no tag and age out by TTL.
- No public signature, cache key or stored format changes.

## Evidence

- `EntityVariantMembershipSpec` (Koan.Tests.Data.Core, InMemory): `Delete`, `DeleteMany` and the batch `Delete` through
  the Anime view leave a Manga row in place and delete the Anime rows. Failed before this change (`Delete` returned true).
- `EntityCacheMassDeleteSpec` (Koan.Tests.Cache.Topology, InMemory): after `DeleteAll`, `RemoveAll` Safe, Optimized and
  Fast, and `Clear`, `Get` of a cached row returns null. All five failed before this change.
