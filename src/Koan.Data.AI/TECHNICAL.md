---
uid: reference.modules.Koan.data.ai
title: Koan.Data.AI - Technical Reference
description: Entity-oriented embedding, semantic search, and media-analysis runtime contracts.
since: 0.6.x
packages: [Sylin.Koan.Data.AI]
source: src/Koan.Data.AI/
---

## Contract

- `EmbeddingMetadata.Resolve<TEntity>()` is convention-first and always returns metadata.
- `[Embedding]` enables persistence lifecycle behavior; undecorated entities retain on-demand AI
  operations without automatic processing.
- Entity and vector operations resolve adapters, configuration, logging, and storage through the
  current `AppHost` execution context.

## Runtime ownership

- Immutable reflection and attribute metadata may remain in process-wide caches.
- Loggers, adapters, configuration, lifecycle registrations, and backend-dependent confirmations are
  host-owned and must not be captured in static initializers.
- Sequential hosts may reuse the same closed-generic Entity and metadata paths without retaining the
  previous provider or its storage selection.
- Parallel hosts or jobs use `AppHost.PushScope(provider)` around the flow that performs Entity work.

## Failure behavior

- A stopped host is not restored as an ambient fallback.
- Logging is best-effort and cannot poison an Entity metadata type initializer after host disposal.
- Vector model mismatches fail at the guarded write boundary; inspection remains diagnostic.

## References

- Entity semantics contract: `/docs/architecture/entity-semantics-contract.md`
- Host-scope decision: `/docs/decisions/DATA-0095-data-layer-simplification.md`
- AI integration guide: `/docs/guides/ai-integration.md`
