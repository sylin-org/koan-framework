---
type: GUIDE
domain: core
title: "R04-02 - Make Runtime State Host-Scoped"
audience: [maintainers, framework-authors, ai-agents]
status: draft
last_updated: 2026-07-13
framework_version: v0.17.0
---

# R04-02 — Make runtime state host-scoped

- Priority: P0
- Status: `in-progress`
- Depends on: R04-01
- Owner: Core hosting with Data.Core/AI consumers

## User-visible failure

Before R04-02, repeated integration hosts and some static Entity paths could retain a disposed service
provider. The original Data/AI lifecycle process failed with `ObjectDisposedException: IServiceProvider`;
static lifecycle registries and relationship metadata retain the same risk shape until audited.

## Personas

Developers see flaky tests or failures after restart; agents may diagnose the wrong host; operators
cannot trust lifecycle isolation in workers or reload scenarios.

## Current evidence

- Before the first repair, the self-executing Data.AI suite reported 79 tests: 48 passed and 31 failed.
  The root failure was `EmbeddingMetadata` resolving a logger from a disposed `AppHost.Current` in its
  static initializer; the poisoned type initializer caused the remaining cascade.
- `AppHost.Current`, static closed-generic registries, and cached relationship metadata are reachable
  from Entity paths.

## First increment — host binding and Data.AI capture repair

- The generic-host binder now owns a disposable `AppHost` lease from start through stop.
- A newer host replaces the process default; an older host cannot clear it; releasing the newest host
  never resurrects a predecessor.
- `AppHost.PushScope` remains the explicit provider selector for parallel execution flows.
- `EmbeddingMetadata` and `EntityAi` retain only immutable metadata statically and resolve logging at
  operation time.
- A real two-host in-memory probe reuses the same closed-generic Entity path and proves provider and
  storage isolation.
- The repaired Data.AI suite passes 80/80 as one process; the complete Core suite passes 195/195.

At that checkpoint, `VectorModelGuard`'s confirmation cache, runtime registration sets,
relationship/lifecycle metadata, `AppHost.Identity`, and the non-hosted `StartKoan()` binding path
still required owner-specific classification and proof.

## Second increment — durable vector-model confirmation

- Removed `VectorModelGuard`'s process-wide `(entity, partition, model)` confirmation cache.
- Every guarded write now reads the current host's keyed `VectorModelRegistry<TEntity>` record before
  deciding or recording.
- A red/green two-host probe proved the defect: before removal, host B started with an empty backend,
  inherited host A's confirmation, and left its own registry empty.
- The repeated-host fixture now proves host B records the same entity/model in its own backend.
- Data.AI builds with zero errors and its complete self-executing suite passes 81/81.

The durable registry is authoritative for completed writes but does not provide atomic compare-and-set
across simultaneous first writers. R04-02 removes host/process leakage; a provider-negotiated concurrent
write guarantee must be earned separately.

R04-02 remains active for runtime registration sets, relationship/lifecycle metadata,
`AppHost.Identity`, and the non-hosted `StartKoan()` path.

## Smallest meaningful fix

Define one host/runtime lease and make service/configuration-backed registries resolve through it.
Immutable reflection/type metadata may remain static. First repair the failing lifecycle path, then add
repeat-create/dispose probes before broad migration.

## Failure behavior

A missing/disposed host throws one Koan host-context error naming the attempted operation and how to
establish a valid host. It never falls back to an earlier provider.

## Verification

- failing Data/AI lifecycle test passes repeatedly;
- N sequential and parallel host create/use/dispose cycles show no provider/registry residue;
- core Entity operations, lifecycle registration, relationship metadata, and AI registry have focused
  ownership tests;
- no new process-global mutable service/configuration cache is introduced.

## Compatibility and rollback

Preserve public call shapes while changing ownership underneath. If an API relied on cross-host static
registration, document and deprecate it rather than silently preserving leakage. Land migration in
small owner-specific commits behind the host lease.

## Stop condition

Split by owner if one host abstraction cannot be reviewed without changing unrelated module semantics.
