# MEDIA-0007: Cache-as-Storage Unification

**Status**: Accepted
**Date**: 2026-05-31

Extends MEDIA-0005 (recipe pipeline contract). Orthogonal to MEDIA-0006 (storage-entity media model) but builds on the lineage fields it formalizes.

## Contract

| Aspect | Specification |
|---|---|
| **Inputs** | `sourceMediaId` (required string), `recipeName` (optional string; absent = original bytes) |
| **Outputs** | Byte stream from storage. Content-Type reflects original MIME for raw fetches or recipe-encoder MIME for derivations. ETag = `"{sourceHash12}-{recipeFingerprint}"` (no recipe -> `"{sourceHash12}-original"`). |
| **Error modes** | `SourceNotFound` (404 when `sourceMediaId` has no storage row); `RecipeNotFound` (400 when `recipeName` is not a registered recipe); pipeline errors propagate as 500 with diagnostic header. |
| **Success criteria** | One storage namespace for sources and derivations; orphan sweep reclaims derivations whose source was deleted; no duplicate URL surface for raw bytes; conditional-GET still short-circuits before storage I/O. |

## Context

The framework currently ships two parallel storage stories for the same conceptual asset:

1. **Source media** lives as a `StorageEntity<T>` row addressed by content hash, queryable, backed by a Koan.Storage profile, and surfaced via `StorageMediaController<T>` at host-defined routes (e.g. `/api/previews/{key}`).
2. **Rendered derivations** live in `IMediaOutputCache` — a side-channel keyed by `(sourceId, recipeFingerprint)`, persisted to a sharded filesystem layout (`{root}/{shard}/{id}-{fingerprint}.{ext}`), invisible to entity queries, the storage browser, backup tooling, and quota accounting.

This split has accumulated four concrete pains:

- **Two storage stories to reason about.** Maintainers must remember that "the preview at `/media/{id}/package-card`" lives on disk via `FileSystemMediaOutputCache`, not in the `PreviewImage` table. Schema migrations, retention policies, and disaster-recovery runbooks have to cover both, and reviewers regularly forget the cache exists when reasoning about an entity's lifecycle.
- **Two URL surfaces for raw bytes.** Downstream consumer exposes `/api/previews/{key}` and `/api/article-media/{key}` (raw, via `StorageMediaController`) alongside the framework's `/media/{id}` (also raw, via `MediaController` with no seed). Three routes, same job. Clients and ops dashboards have to learn which path serves what, and feature work has to update both surfaces in lockstep.
- **Cache invalidation requires cross-system coordination.** When a `PreviewImage` row is deleted, its derived renders sit in the cache directory until something else evicts them. The cache has no foreign-key relationship to the source — there's no entity to query, no tag to filter on, no `Deleted` event the cache subscribes to. Today the only honest answer is "wipe the cache root on deploy."
- **Derivations are invisible to operational tooling.** Storage browsers, S3 lifecycle policies, quota reports, and backup snapshots see source media but not the cache. A 20 GB cache directory doesn't show up in capacity dashboards until the disk fills.

MEDIA-0006 settled the data shape: `IMediaObject` already exposes `SourceMediaId`, `DerivationKey`, `RelationshipType`, and `ThumbnailMediaId`. Those fields are nullable, unused by current Downstream consumer entities, and ready to carry derivation lineage with zero schema migration. The framework's `Entity<T>.Query(Expression<Func<TEntity, bool>>)` infrastructure can already find "all derivations of source X with recipe fingerprint Y" as a two-field AND predicate. The atomic-write, sharding, and extension-encoded-MIME tricks from `FileSystemMediaOutputCache` are reproducible on top of Koan.Storage profiles. The gap is conceptual, not technical: we have a cache pretending to be storage.

This ADR closes that gap by deleting the cache as a separate concept.

## Decision

Drop `IMediaOutputCache` as a first-class abstraction. Derived blobs become ordinary storage entities with lineage stamped onto the fields MEDIA-0006 formalized. The recipe pipeline reads from and writes to storage directly; the orphan-sweep job replaces eviction; the framework collapses to a single URL surface for raw bytes.

### a. Derived storage key shape

Derivations are written into the **same storage profile and entity type as their source**, with a deterministic key derived from `(sourceMediaId, recipeFingerprint)`:

```
{sourceMediaId}:{recipeFingerprint}
```

The colon separator is chosen because both halves are already constrained to URL-safe characters (ASCII alphanumeric + `-_`, max 256 chars each — the same charset `FileSystemMediaOutputCache` validates today), and `:` is reserved in URL path segments without being a path-traversal vector. Storage providers that disallow colons in object keys (none of Koan's current providers — filesystem, S3, Azure all accept them) substitute `__` at the provider layer.

The source row keeps its existing key shape (SHA-256 hex for content-addressed entities, caller-named for `Upload()` entities). The derivation key references the source key verbatim, so foreign-key reasoning is purely string-prefix.

Rejected alternatives:

- **Separate `MediaDerivation<T>` entity type per source type.** Doubles the schema surface, forces every consumer entity (`PreviewImage`, `ArticleMedia`, future types) to ship a paired derivation type, and adds a join to every read path. The lineage fields on `IMediaObject` exist precisely to avoid this.
- **UUID per derivation with `SourceMediaId` foreign key only.** Loses the determinism: two cache probes for the same `(source, recipe)` tuple would need an entity query, not a direct `Head(key)`. The pipeline's hot path becomes a database round-trip instead of a single storage stat.

### b. IMediaObject lineage stamping at write time

When the pipeline writes a derivation, it stamps the entity row before upload:

- `SourceMediaId` -> source's storage key (unchanged from MEDIA-0006).
- `DerivationKey` -> the recipe fingerprint (`MediaRecipe.Fingerprint()`, post-override, normalized).
- `RelationshipType` -> the recipe name (e.g. `"package-card"`, `"article-hero"`) when invoked by name, or `"derivation"` for ad-hoc renders.
- `ContentType` -> the encoder's output MIME (already round-trips through `StorageEntity.Head()` per MEDIA-0006 lines 191-198).
- `Tags["recipe-version"]` -> the recipe's `Version` field, so a fingerprint-stable but semantics-changed recipe can be swept selectively.

This is purely additive: source entities continue to leave these fields null, and queries that filter `m.SourceMediaId == null` find originals. The pipeline never mutates the source row.

### c. MediaController check/write flow

The cache probe at `MediaController.RenderAsync` line 180 and the write-through at line 225 are replaced by direct storage calls against the source entity's storage type. Pseudocode for the replacement (where `TMedia` is the concrete `MediaEntity<T>` resolved from the source):

```csharp
// REMOVE: var cacheHit = await _outputCache.TryGetAsync(id, fingerprint, ct);
var derivedKey = $"{id}:{fingerprint}";
var stat = await TMedia.Head(derivedKey, ct);
if (stat is not null)
{
    var stream = await TMedia.OpenRead(derivedKey, ct);
    return BuildResponse(stream, stat.ContentType ?? FallbackContentType(fingerprint), etag);
}

// ... existing pipeline execution (lines 191-204 unchanged) ...

// REPLACE: await _outputCache.SetAsync(id, fingerprint, output, ct);
var derivation = TMedia.Create(derivedKey, output.Bytes, output.ContentType);
derivation.SourceMediaId   = id;
derivation.DerivationKey   = fingerprint;
derivation.RelationshipType = recipeName ?? "derivation";
derivation.Tags = new Dictionary<string, string>
{
    ["recipe-version"] = recipe.Version ?? "1",
};
try { await derivation.Upload(ct); }
catch (Exception ex) { _logger.LogWarning(ex, "derivation write-through failed"); }
```

The write is best-effort (matches today's `_outputCache.SetAsync` semantics — pipeline succeeds even when persistence fails). Atomic-write guarantees come from the storage provider; the filesystem provider already does temp-file-plus-rename, and S3/Azure are atomic per-object by contract.

Conditional-GET handling at `MediaController` lines 166-175 is untouched: `If-None-Match` short-circuits before either the storage probe or pipeline, so the unification adds zero overhead on validated requests.

`StorageMediaController<T>` continues to serve raw bytes via the source key; nothing in that controller's contract changes because derivations live in the same storage namespace and answer to the same `Head()`/`OpenRead()` calls.

### d. GC sweep service

A scheduled background task — `MediaDerivationSweepService`, registered through Koan's hosted-service convention — walks each registered `MediaEntity<T>` type on a configurable cadence (default: nightly):

```csharp
var derivations = await TMedia.Query(m => m.SourceMediaId != null, ct);
foreach (var d in derivations)
{
    var sourceExists = await TMedia.Head(d.SourceMediaId!, ct) is not null;
    if (!sourceExists)
    {
        await TMedia.Get(d.Key).Delete(ct);
    }
}
```

The sweep is idempotent, resumable, and bounded by entity-page size. For large catalogs the predicate can be windowed by `CreatedAt` to spread work across runs. Manual triggers (e.g. immediately after a bulk source delete) call the same service method.

Eviction beyond orphan-cleanup — recipe-version rotation, LRU, quota-driven trim — composes from `Tags["recipe-version"]` queries and `UpdatedAt` ordering. None of those are required for parity with today's cache (which has no eviction at all) and are deferred to a follow-up.

### e. URL unification

The recommended path, consistent with the dogfeeding principle, is **deletion**:

- `PreviewsController` and `ArticleMediaController` are removed from Downstream consumer.
- The SPA's five hard-coded call sites (recon: `src/api/articles.ts:41`, `src/components/toCardModel.ts:96`, `src/pages/PackageDetail.tsx:287,376`, `src/pages/admin/ArticleEdit.tsx:873`) already use `/media/{id}/{recipe}`. No SPA churn is required for derivation routes.
- The two call sites that fetched raw bytes via `/api/previews/{key}` and `/api/article-media/{key}` migrate to `/media/{id}` (no seed = original bytes).
- The admin endpoints under `/api/admin/article-media` for upload/list/delete are CRUD on entity metadata, not delivery, and stay on their existing routes.

If a host needs legacy compatibility (Downstream consumer does not), the deprecated controllers can be retained for one release as 301-redirect shims to `/media/{id}`. The framework does not ship that shim; hosts opt in.

## Migration

The framework removes `IMediaOutputCache` from the public surface across two releases:

- **This release (MEDIA-0007).** `IMediaOutputCache` is marked `[Obsolete("Use Koan.Storage; see MEDIA-0007", error: false)]`. `FileSystemMediaOutputCache` is reimplemented as a thin adapter that delegates `TryGetAsync`/`SetAsync` to a Koan.Storage profile under the hood, so existing DI registrations keep working. `NullMediaOutputCache` stays as a no-op for hosts that disable the cache entirely. `MediaController` no longer takes `IMediaOutputCache` in its constructor; the storage path is the only path.
- **Next release (MEDIA-0008).** `IMediaOutputCache`, `FileSystemMediaOutputCache`, `NullMediaOutputCache`, `MediaCacheHit`, and `MediaOutputCacheOptions` are deleted. Hosts that still register them get a clear DI-resolution failure pointing at MEDIA-0007.

Downstream consumer migration in this release:

1. Delete `PreviewsController` and `ArticleMediaController`.
2. Add the two SPA call sites that fetched raw bytes via the legacy routes (article-media library list thumbnails, preview byte fetches outside the recipe path) to use `/media/{id}` directly. The recon shows these are limited and centralized.
3. Drop the `Koan:Media:Web:OutputCache` configuration block — the cache root directory can be wiped post-deploy; derivations rewarm on first request.
4. Add `MediaDerivationSweepService` to the host's service registration. Run once manually to clear any orphans accumulated before the migration.

No data migration is required. Existing cache-directory contents are abandoned (they were never authoritative) and the cache root is removed from deploy artifacts. This matches the greenfield-no-persistence-scaffolding posture: derivations are by definition recoverable from source plus recipe, so reseeding them on first access is the cheapest correct option.

## What we explicitly DON'T do

- **No new entity type for derivations.** `PreviewImage` and `ArticleMedia` (and any future `MediaEntity<T>`) carry both originals and derivations in the same table, distinguished by `SourceMediaId != null`. Adding a `Derivation<T>` schema would double the entity count for zero queryability win.
- **No background pre-warming.** Derivations are still generated on first request. Hosts that want eager warming can call the recipe pipeline at write-time of the source; the framework does not orchestrate it.
- **No multi-tier cache.** Memory-tier caching in front of storage is out of scope. Storage providers handle their own page cache (filesystem) or CDN integration (S3/Azure); the framework does not stack a second tier in the process.
- **No streaming encoders.** Per-recipe streaming output is MEDIA-0008's domain. This ADR assumes the encoder produces a complete byte buffer per render, matching today's pipeline.
- **No format negotiation contract.** Content negotiation (`Vary: Accept`, AVIF/WebP fallback) is MEDIA-0009. This ADR preserves the existing `Vary` logic at `MediaController` lines 230-237 untouched.

## Consequences

**Positive.**

- One storage namespace. Backup, quota, browser tooling, and disaster-recovery runbooks cover originals and derivations uniformly.
- Foreign-key reasoning works: `SourceMediaId` is queryable, and the GC sweep is a plain entity query plus storage delete.
- The URL surface collapses from three to one for raw bytes (`/media/{id}`) and one for derivations (`/media/{id}/{recipe}`). Clients and ops dashboards learn one pattern.
- Storage providers' atomic-write, retention, and replication guarantees apply to derivations for free.
- The `IMediaObject` lineage fields earn their keep instead of sitting unused, as MEDIA-0006 anticipated.

**Negative.**

- The write path is now storage-I/O plus an entity-row write, where the cache was a single sharded-disk write. Net cost is comparable on the filesystem provider (same disk, one extra entity insert) and slightly higher on S3/Azure (one PUT for bytes, one row write for metadata). The cost is paid once per `(source, recipe)` tuple and amortized across all subsequent reads.
- More typing at the call site than the old two-method interface — `Head()`/`OpenRead()`/`Upload()` instead of `TryGetAsync()`/`SetAsync()`. The verbosity is intentional: it makes the storage roundtrip visible in the pipeline code where reviewers can see it.
- Hosts that disabled the cache entirely (`NullMediaOutputCache`) now pay the storage probe cost on every request. The mitigation is to disable the recipe route entirely or to front it with a CDN; "cache disabled" was always a debug-only configuration.

## Out of scope

- **Cross-region replication** of derivations. Handled by the storage provider's replication settings.
- **Signed URLs** for derivation access control. Future ADR if needed.
- **Streaming encoders** that produce derivations as the response body streams to the client. MEDIA-0008.
- **Format negotiation contract** including AVIF/WebP fallback and `Accept` header parsing. MEDIA-0009.
- **Quota enforcement** at write time. The storage provider rejects oversized writes; the framework does not pre-check.

## Test coverage requirements

- **Storage-backed cache hit.** Render a recipe twice; assert the second render returns the same bytes without invoking the pipeline (verify via a counter in a test `IMediaPipeline`).
- **Storage-backed cache miss.** First render with no prior derivation: assert pipeline ran, derivation row written with correct `SourceMediaId`/`DerivationKey`/`RelationshipType`/`ContentType`/`Tags["recipe-version"]`.
- **GC sweep deletes orphans.** Create source + derivation, delete source, run sweep, assert derivation is gone and unrelated derivations are untouched.
- **GC sweep is idempotent.** Run sweep twice on the same state; assert no spurious deletes and no exceptions on the second pass.
- **ETag stability across the unification.** Same `(source, recipe)` tuple produces the same ETag whether served from a fresh render or a stored derivation (the formula `"{sourceHash12}-{fingerprint}"` is independent of cache state).
- **Conditional-GET short-circuits storage probe.** `If-None-Match` matching ETag returns 304 without calling `Head()` on the derivation key (verify via a storage-call counter).
- **Lineage fields persist round-trip.** Write a derivation, read it back via `Query(m => m.SourceMediaId == sourceId)`, assert all four lineage fields plus `Tags["recipe-version"]` survive.
- **Best-effort write-through swallows storage failures.** Inject a faulting storage provider on `Upload()`; assert pipeline still returns 200 with bytes and the failure is logged at warning level.
- **Legacy obsolete adapter delegates correctly.** During the migration release, `FileSystemMediaOutputCache.TryGetAsync` and `SetAsync` must produce the same observable behavior as direct storage calls (same bytes, same content-type round-trip).
