---
id: DATA-0003
slug: DATA-0003-write-capabilities-and-bulk-markers
domain: DATA
status: Accepted
date: 2025-08-16
---

# 0003: Write capabilities and bulk markers

## Context
We want providers to declare support for native bulk operations (upsert/delete) and optionally atomic batches. Controllers and domain helpers should remain simple and defer to the data layer to choose the optimal path based on provider capabilities.

## Decision
- Introduce `WriteCapabilities` flags and `IWriteCapabilities` to expose provider write features.
- Add optional marker interfaces `IBulkUpsert<TKey>` and `IBulkDelete<TKey>` for explicit detection.
- Surface capabilities through `RepositoryFacade` and `Data<TEntity, TKey>` so callers can branch when needed.
- Keep `EntityController` unaware of capabilities; always call `UpsertManyAsync/DeleteManyAsync` or `Batch()` and let the data layer optimize.

## Consequences
- Providers can advertise native bulk support enabling optimized paths without controller logic changes.
- Callers can inspect `Data<TEntity, TKey>.WriteCaps.Writes` or check marker interfaces for diagnostics or optional behaviors (e.g., `AtomicBatch`).
- Backwards compatibility preserved; providers without declarations default to `None` and existing fallbacks continue to work.

## Implementation notes (2025-08-16)
- JSON adapter: advertises `BulkUpsert | BulkDelete` and implements bulk paths in-memory.
- SQLite adapter: now advertises `BulkUpsert | BulkDelete`. Bulk methods are functionally correct and currently loop per-item; native batching may be added later without surfacing changes.
- Discovery: The `/.well-known/sora/capabilities` endpoint aggregates `QueryCapabilities` and `WriteCapabilities` per aggregate and provider, so clients can branch or feature-detect.
