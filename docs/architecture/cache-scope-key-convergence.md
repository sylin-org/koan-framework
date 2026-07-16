---
type: ARCHITECTURE
domain: cache
title: "Cache Entity identity convergence"
audience: [architects, maintainers, ai-agents]
status: current
last_updated: 2026-07-16
framework_version: v0.19.0
validation:
  date_last_tested: 2026-07-16
  status: passed
  scope: repository and explicit Entity eviction policy/template/context/scope identity
---

# Cache Entity identity convergence

## Why this boundary exists

A cache entry is not identified by `Entity type + id` alone. The active Entity policy may choose a
custom template; Data routing contributes partition/source values; managed equality axes such as
tenant add an isolation suffix. If repository reads/writes and out-of-band eviction independently
reconstruct that identity, a plausible successful removal can target a key that never existed.

That failure occurred in two generations:

1. the original eviction path omitted partition and managed tenant scope; and
2. the repaired path shared only the managed-scope suffix while explicit eviction still forced the
   default template and silently missed custom Entity policies.

The second design documented tag flush as a workaround. R07-16 removes the split rather than retaining
the limitation.

## Current decision

`Koan.Cache` owns one host-level `EntityCachePlan`. It is consumed by both
`CacheRepositoryDecorator`/`CachedRepository` and `EntityCacheEvictionCoordinator`. The plan owns:

- selection of the first active Entity-scoped policy;
- exclusion of transformed storage representations and non-equality read scopes;
- parsing/rendering the selected key template;
- canonical Entity type identity;
- `{Partition}` and `{Source}` values from captured `EntityContext`; and
- equality managed-axis folding through `ScopedEntityCacheKey` and the shared
  `AmbientAxisComposer`.

`ScopedEntityCacheKey` is now an internal suffix renderer, not a second public full-key builder. No
explicit eviction API can bypass policy/template selection.

## Public semantics

```csharp
await note.Cache.Evict();
await notes.Cache.Evict();
await Note.QueryStream(x => x.Expired).Cache.Evict();

await Note.Cache.Flush(); // separate type/tag control plane
```

Ordinary Entity `Save` and `Delete` already maintain cache state. `Evict()` exists for an out-of-band
write that deliberately bypassed the repository. Scalar, finite, and async-stream forms mean one
pointwise entry removal per Entity; they preserve source order/multiplicity and apply sequential
backpressure.

The operation captures Data routing state and all registered Core context carriers before its first
await, then restores them around deferred source enumeration and removal. Calling under tenant A can
only compute tenant A's key; there is no unscoped fallback.

`EntityCacheEviction` distinguishes:

- `Removed`: the selected topology reported a present entry removed;
- `Absent`: removal completed and peer invalidation was still requested, but no selected-tier entry
  was present;
- `Skipped`: a default identifier could never have been cached; and
- `Failed`: the current item did not reach a confirmed removal result.

Source/removal exceptions and cancellation carry the confirmed fixed-size prefix. Store-tier removal
and peer publication are separate awaits and are not atomic; a failing item may be partly removed even
though it is not counted as confirmed.

## Preserved isolation laws

- Managed equality axes partition cache identity. Off/no applicable axis returns the base key
  unchanged.
- A non-equality axis or pure predicate read contributor excludes the Entity type entirely; a viewer
  visibility predicate is never misrepresented as a scalar key segment.
- Storage-transformed Entities remain excluded so provider payloads cannot become a second cached
  representation outside Data's reversal boundary.
- Entity type names use `CacheKey.EntityTypeName`, including closed-generic type arguments.
- Template values render invariantly through the shared template implementation.

Custom Entity key templates must remain derivable at every operation that needs them. An id-based read
normally uses `{TypeName}`, `{Partition}`, `{Source}`, `{Id}`, and `{Key}`; a template that requires an
unavailable Entity property cannot satisfy a keyed read and is not made safe merely by explicit
eviction.

## Deliberate separation

`EntityType.Cache.Explain/Flush/Count/Any` is the type/tag control plane. Tag flush is appropriate for
broad maintenance and emits normal per-key invalidations. It is not an alias for pointwise source
eviction. The governed MCP cache tools remain an audited operational surface and do not automatically
project `entity.Cache.Evict()` as an agent mutation.

## Proof

- Cache topology specs prove custom-template rendering, captured partition, sequential no-read-ahead,
  default-key skip, and fixed-size source/removal/cancellation outcomes.
- The real Tenancy + SQLite suite proves same-tenant entry removal, cross-tenant isolation, finite
  eviction, host-scoped partition identity, and a custom template shared by repository write and
  explicit eviction.
- Entity-language consumer cells prove scalar/set/stream discovery, module removal, invalid receiver
  rejection, and coexistence with all current module facets.

The former `Uncache()`, generic `EntityCacheExtensions.Cache<TEntity,TKey>()`, typed handle, and public
default-template-only `ScopedEntityCacheKey.For` path are deleted without compatibility aliases.
