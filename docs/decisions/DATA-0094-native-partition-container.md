---
id: DATA-0094
slug: DATA-0094-native-partition-container
domain: DATA
status: Accepted
date: 2026-05-17
---

# ADR 0094: Native partition container — opt-in for adapters with first-class isolation primitives

## Context

Koan's partition concept (the `?set=` query parameter, `EntityContext.With(partition: ...)`, and `Entity<T>.Copy/Move/Mirror` cross-partition transfers) needs to map onto whatever isolation primitive each storage backend offers. Up to this ADR every adapter mapped partition the same way: append a `RepositorySeparator + partition` suffix to the entity's storage name. `NamingComposer.Compose` did the work, driven by `INamingProvider.RepositorySeparator` and `GetConcretePartition`.

This worked for the adapters where the storage name is "just a string" — table names in SQLite/Postgres/SqlServer, file/directory prefixes in Json, key prefixes in Redis (after we fixed the separator-vs-delimiter collision in 60a82a60), collection names in Mongo.

It collapsed on Couchbase. Couchbase's identifier rules forbid `#` in collection and scope names (alphanumeric, `_`, `-`, `%` only; max 30 chars). The "name#partition" suffix produced invalid collection names every time a partition was active — every partition write, every cross-partition transfer, every read with `?set=` failed at the SDK layer with `Couchbase.CouchbaseException`. The 0/48 partition+transfer pass rate held even after the rest of the Couchbase fixes from 47ad30c3.

The deeper issue is that Couchbase has a **better** native primitive for partition isolation than name suffixing. The container model is:

```
bucket
├── scope
│   └── collection (entity rows)
```

Scopes were designed for tenant/partition isolation inside a shared bucket — independent indexes, independent privileges, query namespaces stay clean. Forcing partition into the collection name throws this away and trips identifier-validation as a bonus.

The same shape applies to other adapters we'll add later. Mongo has database-per-tenant patterns. Future graph adapters may have graph-per-namespace primitives. The framework needs a way for an adapter to say "I'll handle partition routing myself — don't append the suffix" without breaking the legacy suffix model that every other adapter relies on.

## Decision

Add a single bool to `INamingProvider`:

```csharp
public interface INamingProvider
{
    // ... existing members ...

    bool UsesNativePartitionContainer => false;
}
```

`NamingComposer.Compose` honours it:

```csharp
public static string Compose(
    INamingProvider provider,
    Type entityType,
    string? partition,
    IServiceProvider services)
{
    var storageName = provider.GetStorageName(entityType, services).Trim();
    var trimmedPartition = partition?.Trim();
    if (string.IsNullOrEmpty(trimmedPartition))
        return storageName;

    if (provider.UsesNativePartitionContainer)
        return storageName;            // adapter handles partition out of band

    var concretePartition = provider.GetConcretePartition(trimmedPartition).Trim();
    return storageName + provider.RepositorySeparator + concretePartition;
}
```

Adapters that opt in (currently just Couchbase) are responsible for:

1. **Reading `EntityContext.Current.Partition`** wherever they need the partition value.
2. **Routing it onto the native primitive** — for Couchbase this means using the partition as the scope name in `bucket.scope.collection` instead of having it suffix the collection name.
3. **Creating the primitive on demand** — Couchbase's `EnsureCollection` already creates the scope if it doesn't exist, then the collection inside it, then the per-(scope, collection) primary index for N1QL.
4. **Sanitizing partition values** to the adapter's identifier rules. Couchbase's `GetConcretePartition` replaces every character outside `[A-Za-z0-9_\-%]` with `_` and truncates to 30 chars.

The flag defaults to `false`, so all existing adapters (SQLite, Postgres, SqlServer, Mongo, Redis, Json, InMemory) continue to compose `storage#partition` storage names exactly as before — no migration, no breaking change.

## Consequences

### Positive

- **Couchbase actually works end-to-end.** All 48 surface specs pass, including 8 partition specs and 9 cross-partition transfer specs, against a real Testcontainers Couchbase cluster (commit 56929107).
- **No special-casing in core.** `NamingComposer` stays adapter-agnostic; the per-adapter logic lives in the adapter. The flag is a single boolean default-implemented in the interface — adding a new adapter that opts in is one line of code + the adapter-side routing.
- **Native query semantics.** N1QL queries against `bucket.scope.collection` parse cleanly. Indexes per (scope, collection) match how Couchbase recommends sizing GSI. Scope-level operations (drop, list collections, future Couchbase-side capabilities) get applied naturally.
- **Sets the pattern for future adapters.** A Mongo database-per-partition mode, a graph adapter that uses one graph per namespace, or a Postgres schema-per-partition mode would all just set `UsesNativePartitionContainer = true` and route inside their cluster/connection provider.

### Negative

- **Adapter-side complexity moves up.** Adapters that opt in have to read `EntityContext.Current.Partition` in more places than before. Couchbase reads it in `GetCollectionContext`, which is the single chokepoint for repository operations — so the impact is contained, but it's a real new responsibility.
- **Hard to share helpers across modes.** A SQL adapter that wanted to support both modes (suffix in dev, native schema in prod) would need separate code paths. Not a real-world concern today but worth flagging.

### Neutral

- `INamingProvider.RepositorySeparator` and `GetConcretePartition` are still required even when `UsesNativePartitionContainer = true`. They're used for partition-name sanitization in the adapter and stay meaningful for adapter-side composition needs (e.g. Couchbase still uses `GetConcretePartition` to clean up partition values before they become scope names).

## Implementation

| Change | File | Commit |
|---|---|---|
| Add `UsesNativePartitionContainer` default-`false` member to interface | `src/Koan.Data.Abstractions/Naming/INamingProvider.cs` | 56929107 |
| Skip suffix composition when flag is true | `src/Koan.Data.Abstractions/Naming/NamingComposer.cs` | 56929107 |
| Couchbase: opt in, sanitize identifier | `src/Connectors/Data/Couchbase/CouchbaseAdapterFactory.cs` | 56929107 |
| Couchbase: route partition to scope | `src/Connectors/Data/Couchbase/CouchbaseClusterProvider.cs` | 56929107 |
| Couchbase: dispatch `Query(object?)` on `Expression<>` predicate | `src/Connectors/Data/Couchbase/CouchbaseRepository.cs` | 56929107 |

Tests: `Koan.Web.AdapterSurface.Couchbase.Tests` — full 48/48 pass against a `couchbase:community` Testcontainers image, including:

- `CouchbasePartitionSpecs.PostUpsert_in_two_partitions_keeps_them_isolated` — writes to two scopes, reads only from each
- `CouchbaseTransferSpecs.EntityMove_transfers_rows_and_clears_source_partition` — `Entity<Widget>.Move().From(partition: "src").To(partition: "dst")`
- `CouchbaseTransferSpecs.EntityCopy_with_predicate_transfers_only_matching_rows` — predicate-filtered cross-scope copy via the N1QL pushdown

## Alternatives considered

**Alt 1 — Sanitize the `#` suffix in Couchbase.** `RepositorySeparator = "_"` (or `-`), partition becomes a sanitized suffix on the collection name. Works around the Couchbase identifier rules but throws away the scope-as-isolation primitive; primary indexes can't be shared across partitions cleanly, and N1QL queries against `bucket._default.widgets_surface_alpha` are uglier and harder to debug than `bucket.alpha.widgets_surface`. **Rejected** — solves the symptom, not the design mismatch.

**Alt 2 — Couchbase-only `INamingProvider` extension interface.** Add `ICouchbasePartitionRouting` or similar, queried via `services.GetService<...>()`. Avoids changing the cross-adapter interface but turns partition routing into a hidden side-channel the core has to know about. **Rejected** — the choice is genuinely cross-cutting (Mongo and others will hit the same fork), so it belongs on the base interface as a default-false flag.

**Alt 3 — Make every adapter take responsibility for partition routing.** Drop the suffix composition from `NamingComposer` entirely and require every adapter to read partition out of `EntityContext`. The cleanest abstractly but a massive breaking change for the seven existing adapters with no payoff — they're all happy with the suffix model. **Rejected** — opt-in is the right default.
