---
type: REF
domain: media
title: "Media — pillar map"
audience: [developers, ai-agents]
status: current
last_updated: 2026-06-18
framework_version: v0.17.0
validation:
  date_last_tested: 2026-06-18
  status: verified
  scope: docs/reference/cards/media.md
---

# Media — pillar map

> One-screen map of the Media pillar — content-addressable media entities over Storage, with a recipe-driven transform pipeline served by URL. Full detail: [media-recipes-howto.md](../../guides/media-recipes-howto.md).

**What it does** — A media object is `Photo : MediaEntity<Photo>`, which layers over `StorageEntity<T>` so the same `[StorageBinding(Profile, Container)]` placement and presigned-URL surface apply ([MEDIA-0001](../../decisions/MEDIA-0001-media-pillar-baseline-and-storage-integration.md)). `Upload` writes under a caller-chosen name; `Store` hashes the bytes (SHA-256) and dedupes — re-storing identical content is an idempotent no-op. Reference `Koan.Media.Web` and a single `MediaController` exposes `GET /media/{id}/{recipe}`: named **recipes** (declared with the fluent `MediaRecipe` builder, [DX-0047](../../decisions/DX-0047-fluent-media-transform-api.md)) resize/crop/encode on demand, the result is content-addressed by `(sourceHash, recipeFingerprint)` and written back as a lineage-stamped derivation ([MEDIA-0004](../../decisions/MEDIA-0004-recipe-pipeline.md)/[MEDIA-0007](../../decisions/MEDIA-0007-cache-as-storage-unification.md)). Add `Koan.Data.AI` and `[MediaAnalysis]` runs vision/OCR on upload and feeds the result into `[Embedding]` text — cross-modal search for free ([AI-0027](../../decisions/AI-0027-media-analysis-attribute.md)).

## The one canonical pattern

Derive from `MediaEntity<T>`, bind storage placement, `Store` content-addressably, then serve transforms via a registered `[MediaRecipe]`.

```csharp
[StorageBinding(Profile = "cold", Container = "photos")]
public class Photo : MediaEntity<Photo> { public string OwnerId { get; set; } = ""; }

var photo = await Photo.Store(bytes, name: "beach.jpg", contentType: "image/jpeg"); // SHA-256 key, auto-dedup
var url   = await photo.Url(ttl: TimeSpan.FromMinutes(15));                          // presigned original

public static class PhotoRecipes
{
    [MediaRecipe("thumb", Mutators = MutatorKind.Common)]   // allows ?w= ?h= ?format= ?q=
    public static MediaRecipe Thumb() => MediaRecipe.New()
        .Fit(Fit.Cover, width: 200, height: 200)
        .EncodeAs("webp", Quality.Thumbnail);              // GET /media/{id}/thumb?w=400
}
```

## ≤5 attributes you'll use

| Attribute / option | What it does |
|---|---|
| `[StorageBinding(Profile=…, Container=…)]` | Inherited from Storage — pins where the media bytes land (tier + container); read back on `OpenRead`/`Url`. |
| `[MediaRecipe("name", Mutators=…, Eager=…)]` | Registers a `static MediaRecipe`-returning method as a named transform; `name` is the URL slug, `Mutators` allowlists URL query overrides, `Eager` pre-warms at upload. |
| `MutatorKind.Common` | The opt-in override set a recipe accepts on the URL: `Dimensions \| Format \| Quality` (`?w/h/dpr`, `?format`, `?q`); anything outside the allowlist is a 400. |
| `Quality.Web` / `.Thumbnail` / `.Print` | Named quality presets (80 / 60 / 95) passed to `.EncodeAs`/`.PreserveFormat` so recipe JSON stays human-readable. |
| `[MediaAnalysis(Analysis = MediaAnalysis.Describe \| MediaAnalysis.Ocr, Async = true)]` | On upload, runs vision/OCR (`Describe`, `Ocr`, `Transcribe`, `Classify`, `Extract`) into convention-named properties, then bridges into `[Embedding]`. |

## The escape hatch

Build an ad-hoc transform off any byte stream without registering a recipe — the same fluent grammar the controller runs, terminated by a streaming `WriteToAsync`:

```csharp
var output = await sourceStream
    .AsMedia(logger)                                    // StreamExtensions.AsMedia
    .Apply(MediaRecipe.New().Resize(width: 800).FlattenTo("jpeg", Quality.Web))
    .WriteToAsync(destStream, ct);                      // streaming encode, no full buffer
```

`MediaRecipe.New()` plus the builder verbs (`.Fit`, `.Crop`, `.Resize`, `.Overlay`, `.OverlayText`, `.Strip`, `.Freeze`, `.EncodeAs`/`.FlattenTo`) compose any pipeline; `.Fingerprint()` is the recipe-side cache key. For introspection, `GET /media/recipes` lists every registered recipe and its format shortcuts.

## The sample that shows it

[`samples/S6.SnapVault`](../../../samples/S6.SnapVault/README.md) — `PhotoAsset : MediaEntity<PhotoAsset>` with `[StorageBinding(Profile="cold")]` + `[Embedding]`, derivative `PhotoThumbnail`/`PhotoMasonryThumbnail`/`PhotoRetinaThumbnail` linked through the media graph (`SourceMediaId`/`ThumbnailMediaId`), and AI-populated description/tags feeding semantic photo search.
