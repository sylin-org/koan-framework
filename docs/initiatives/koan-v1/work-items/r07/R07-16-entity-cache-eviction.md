---
type: SPEC
domain: framework
title: "R07-16 - Entity Cache Eviction Convergence"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-16
framework_version: v0.19.0
validation:
  date_last_tested: 2026-07-16
  status: passed
  scope: Entity cache-entry identity, scalar/set/stream eviction, context, and control-plane separation
---

# R07-16 — Entity Cache eviction convergence

- Tranche: `T6 — semantic capability ring`
- Status: `passed`
- Depends on: R07-15
- Unlocks: the Media capability inventory
- Owner: Cache policy, Entity entry identity, explicit eviction execution, and partial outcomes

## Meaningful outcome

Explicit cache-entry eviction now reads as Entity intent and preserves its meaning across cardinality:

```csharp
await todo.Cache.Evict(ct);
await todos.Cache.Evict(ct);
await Todo.QueryStream(x => x.Done).Cache.Evict(ct);
```

Ordinary `Save` and `Delete` still maintain cache state automatically. The explicit terminal is for an
out-of-band write that bypassed the Entity repository. `Todo.Cache.Explain/Flush/Count/Any` remains the
distinct type/tag control plane.

## Architecture

- `EntityCachePlan` is the one host-owned policy/template/context/scope decision. The repository
  decorator and explicit eviction consume it instead of reconstructing identity independently.
- The plan selects an active Entity policy, excludes unsafe storage-transform/non-equality-read types,
  formats the selected custom or default template, supplies partition/source tokens, and folds managed
  equality axes through the existing shared composer.
- The scalar, finite, and async facets normalize through Data.Core `EntityCardinality` and one
  Cache-owned coordinator.
- The coordinator captures `EntityContext` plus registered Core context carriers before the first
  await, restores them around deferred enumeration and each writer call, preserves order/multiplicity,
  applies sequential backpressure, and retains only counters.
- `EntityCacheEviction` distinguishes `Removed`, idempotently `Absent`, unset-id `Skipped`, `Failed`,
  `Confirmed`, and `SourceCompleted`. Typed failure/cancellation carry the confirmed prefix.
- A selected-tier removal and peer invalidation request are separate awaits, not an atomic transaction;
  a failing current item may already be partly removed without entering the confirmed count.

## Principal deletion

- public `Uncache()`;
- `EntityCacheExtensions.Cache<TEntity,TKey>()` and `EntityCacheHandle`;
- the public default-template-only `ScopedEntityCacheKey.For` bypass;
- duplicated policy selection, exclusion, template, and ambient-key construction inside
  `CachedRepository`; and
- the documented custom-template eviction miss and its tag-flush workaround.

No compatibility aliases are retained. `Koan.Cache` advances from 0.18 to 0.19; Cache Abstractions do
not move because their public contract did not change.

## Delight contract

- Developers read one operation at the object, set, or query stream and do not choose a key builder,
  generic handle, provider, or loop.
- Coding agents see one regular Entity grammar and one fixed-size result rather than retaining
  per-item state or guessing whether absence failed.
- Operators keep topology/coherence health and type/tag maintenance; broad flush is not disguised as a
  business Entity terminal.
- Tenant and other module context stay opaque to Cache policy code but are sealed once for the whole
  operation, so tenant-A eviction cannot drift into tenant B.

## Acceptance

- static `Todo.Cache` and instance/set/stream `.Cache` coexist and disappear with the module reference;
- repository caching and explicit eviction share custom/default policy templates and managed scope;
- a real tenant host proves same-tenant removal and cross-tenant non-interference;
- sources are sequential, constant-memory, context-stable, and carry fixed-size partial outcomes;
- missing/inapplicable policy rejects before source enumeration;
- type/tag and governed agent operations remain separate control planes;
- startup facts report the effective per-Entity entry plan or cache-safety exclusion from the same
  host-owned decision;
- Cache ships package README/technical companions and a truthful current skill/reference; and
- focused module, tenancy, package, docs, diff, stale-surface, and privacy gates pass without release
  certification.

## Evidence

- Cache topology passes 57/57, including seven eviction-plan/source cases and one real-host proof that
  startup reports the resolved Entity plan from an initialized policy registry.
- Cache cross-engine passes 14/14 across the Memory and SQLite store floors.
- Entity Language passes 25/25, including scalar/set/stream Cache discovery, removal, invalid receiver,
  and all-module coexistence.
- The real Tenancy + SQLite convergence class passes 6/6: same-tenant removal, cross-tenant isolation,
  finite eviction, default-key skip, host-scoped partition identity, and custom-template parity.
- `Koan.Cache` builds warning-as-error with zero warnings/errors.
- `Sylin.Koan.Cache` packs at 0.19.0 with DLL, XML documentation, new package README, and bounded exact
  Koan dependency ranges; package inventory remains 112 independently versioned owners.
- Docs lint reports 0 errors / 1581 historical or front-matter warnings; skills lint passes 20/20
  with zero warnings; changed marked examples compile 2/2. Diff and stale-source checks pass.
- No full-solution or public-release certification suite ran.

## Explicit non-claims

- batch or per-item atomicity across cache tiers and peer carriage;
- durable invalidation replay, catch-up, automatic removal retry, or remote settlement;
- provider guarantees beyond the elected topology's reported facts;
- automatic runtime-agent exposure of pointwise eviction;
- a global-clear wire command; or
- production-maturity promotion from this framework proof alone.

## Acceptance result

- Outcome: PASS
- Date: 2026-07-16
- Follow-up: inventory Media derivative/prewarm semantics; add no facet or source lift without a real
  contract and consumer.
- Reviewer: Codex implementation under maintainer standing approval.
