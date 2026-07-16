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
- `EmbeddingRegistry` and `MediaAnalysisRegistry` contain only process-wide entity `Type` discovery
  facts. Generated module initialization and loaded-assembly discovery populate them additively; their
  public registration entry points are framework infrastructure, not per-host runtime extension APIs.
- Loggers, adapters, configuration, lifecycle registrations, and backend-dependent confirmations are
  host-owned and must not be captured in static initializers.
- Sequential hosts may reuse the same closed-generic Entity and metadata paths without retaining the
  previous provider or its storage selection.
- Vector model confirmations are never cached process-wide. Each guarded write reads the current
  host's keyed durable registry record before deciding whether the model is safe.
- Parallel hosts or jobs use `AppHost.PushScope(provider)` around the flow that performs Entity work.
- Async embedding enqueue captures the host-owned `KoanContextCarrierRegistry`; the worker restores
  the opaque bag with `ContextIngressTrust.HostTrusted` before loading the entity. The worker names no
  module axis, and absent registered axes are explicitly suppressed.
- Synchronous lifecycle indexing, deferred worker indexing, and explicit migration converge on one
  internal embedding writer. It owns embedding-category source/model resolution, mixed-space guard,
  provenance, vector-only persistence, and state confirmation. Its callers retain their different
  intents and outcomes; none may re-save the domain Entity.
- `EmbedJob<TEntity>` is host-scoped storage infrastructure but deliberately exempt from tenant Data
  filtering so the global worker can claim it. Its scoped durable id therefore folds Entity identity
  plus the complete captured bag through `KoanContextFingerprint`; equal Entity ids in different
  contexts cannot collide, and the id never embeds raw carrier values. Queue rows do not persist the
  embedding text, provider selection, per-row retry limits, or unused priority knobs; the restored
  current Entity and current type declaration are authoritative.

The discovery registries retain strong `Type` references for the process lifetime. Collectible plugin
unloading is therefore not a supported scenario. Host activation derived from those facts—including
Entity lifecycle handlers—must still have host-safe ownership; a process-wide type inventory does not
make process-wide runtime registrations safe.

Embedding and media-analysis lifecycle hooks are static, host-independent behavior definitions. If
repeated host composition discovers the same closed-generic hook again, Entity lifecycle registration
is idempotent by delegate equality. Hook execution resolves adapters, storage, logging, and options from
the active host; the hook definition does not capture them.

## Failure behavior

- A stopped host is not restored as an ambient fallback.
- Logging is best-effort and cannot poison an Entity metadata type initializer after host disposal.
- Vector model mismatches fail at the guarded write boundary; inspection remains diagnostic.
- A vector write may succeed before its embedding-state confirmation fails; the operation then fails
  and the durable worker retries. Collection atomicity and cross-store rollback are not claimed.
- The registry decision covers completed write sequences. Atomic exclusion between simultaneous first
  writers requires a provider transaction or conditional-write contract and is not currently claimed.

## References

- Entity semantics contract: `/docs/architecture/entity-semantics-contract.md`
- Host-scope decision: `/docs/decisions/DATA-0095-data-layer-simplification.md`
- AI integration guide: `/docs/guides/ai-integration.md`
