---
name: koan-media
description: Content-addressable media over Storage — MediaEntity<T> : StorageEntity<T>, Store (SHA-256 dedup) / Upload / Url, the fluent MediaRecipe transform pipeline served by GET /media/{id}/{recipe}, [MediaRecipe] named transforms with MutatorKind URL allowlists, and the [MediaAnalysis] vision/OCR → [Embedding] bridge
pillar: media
card: docs/reference/cards/media.md
status: current
last_validated: 2026-06-18
---

# Koan Media

## Trigger this skill when you see

- `MediaEntity<T>` / `Photo : MediaEntity<Photo>` (a media object layered over Storage)
- `Photo.Store(bytes, ...)` (content-addressable, SHA-256 dedup) / `.Upload(stream, name, ...)` / `media.Url(ttl)`
- `[MediaRecipe("name", Mutators = ...)]` on a `static MediaRecipe`-returning method, `MediaRecipe.New()` + `.Fit` / `.Crop` / `.Resize` / `.EncodeAs` / `.FlattenTo` / `.Overlay` / `.OverlayText` / `.Strip`
- `MutatorKind.Common` / `.Dimensions` / `.Format` / `.Quality`, `Quality.Web` / `.Thumbnail` / `.Print`, `Fit.Cover` / `.Contain`
- `[MediaAnalysis(Analysis = MediaAnalysis.Describe | MediaAnalysis.Ocr)]` feeding `[Embedding]`
- `stream.AsMedia(...).Apply(recipe).WriteToAsync(dest, ct)` — the ad-hoc transform escape hatch
- References to `Koan.Media.Core` / `Koan.Media.Web` / `Koan.Media.Abstractions`, the `MediaController`, `GET /media/{id}/{recipe}`, `GET /media/recipes`
- "thumbnail", "resize on the fly", "presigned media URL", "content-addressed image", "recipe", "OCR / describe an image on upload"

## Core principle

**Media builds on Storage; transforms are recipes served by URL.** A media object is `Photo : MediaEntity<Photo>`, which layers over `StorageEntity<T>` — the same `[StorageBinding(Profile, Container)]` placement and presigned-`Url` surface apply ([MEDIA-0001](../../../docs/decisions/MEDIA-0001-media-pillar-baseline-and-storage-integration.md)). `Upload` writes under a caller-chosen name; `Store` hashes the bytes (SHA-256), uses that as the storage key, and **dedupes** — re-storing identical content is an idempotent no-op. Reference `Koan.Media.Web` and one `MediaController` exposes `GET /media/{id}/{recipe}`: named **recipes** (declared with the fluent `MediaRecipe` builder, [DX-0047](../../../docs/decisions/DX-0047-fluent-media-transform-api.md)) resize/crop/encode on demand, the result is content-addressed by `(sourceHash, recipeFingerprint)` and written back as a lineage-stamped derivation ([MEDIA-0004](../../../docs/decisions/MEDIA-0004-recipe-pipeline.md)). This is Reference = Intent — no controller wiring, no transform service to register.

<!-- validate -->
```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.AI.Attributes;
using Koan.Media;
using Koan.Media.Abstractions.Recipes;
using Koan.Media.Core.Extensions;          // .Url (extension on IMediaObject)
using Koan.Storage;                         // [StorageBinding]

[StorageBinding(Profile = "cold", Container = "photos")]            // inherited from Storage — pins where bytes land
[MediaAnalysis(Analysis = MediaAnalysis.Describe | MediaAnalysis.Ocr, Async = true)]  // vision/OCR on upload
[Embedding]                                                         // analysis text → vector, cross-modal search
public sealed class Photo : MediaEntity<Photo>
{
    public string OwnerId { get; set; } = "";
    public string? AiDescription { get; set; }   // auto-populated by Describe
    public string? OcrText { get; set; }          // auto-populated by Ocr
    public float[]? Embedding { get; set; }       // auto-populated by [Embedding]
}

public static class PhotoRecipes
{
    [MediaRecipe("thumb", Mutators = MutatorKind.Common)]   // URL slug "thumb"; allows ?w= ?h= ?format= ?q=
    public static MediaRecipe Thumb() => MediaRecipe.New()
        .Fit(Fit.Cover, width: 200, height: 200)
        .EncodeAs("webp", Quality.Thumbnail);               // served at GET /media/{id}/thumb?w=400
}

public sealed class PhotoService
{
    public async Task<Uri> Ingest(byte[] bytes, CancellationToken ct = default)
    {
        var photo = await Photo.Store(bytes, name: "beach.jpg", contentType: "image/jpeg", ct: ct); // SHA-256 key, auto-dedup
        return await photo.Url(TimeSpan.FromMinutes(15), ct);   // presigned URL to the original
    }
}
```

## Reference = Intent activation

| Add this reference / declare this | Effect |
|---|---|
| `Koan.Media.Core` + `Photo : MediaEntity<Photo>` | The media model — `Store` / `Upload` / `Get` / `OpenRead` / `Url` statics over Storage, the media graph (`SourceMediaId` / `ThumbnailMediaId` / `DerivationKey`). |
| `[StorageBinding(Profile=…, Container=…)]` (inherited) | Pins where the bytes land (tier + container); read back on `OpenRead` / `Url`. |
| `+ Koan.Media.Web` | One `MediaController` lights up `GET /media/{id}/{recipe}` (recipe transforms, content-addressed, written back as a derivation) and `GET /media/recipes` (introspection). |
| `[MediaRecipe("name", Mutators=…, Eager=…)]` on a `static MediaRecipe` method | Registers a named transform; `name` is the URL slug, `Mutators` allowlists URL query overrides, `Eager=true` pre-warms at upload. |
| `+ Koan.Data.AI` + `[MediaAnalysis(Analysis=…)]` | On upload, runs `Describe` / `Ocr` / `Transcribe` / `Classify` / `Extract` into convention-named properties, then bridges into `[Embedding]` text — cross-modal search for free ([AI-0027](../../../docs/decisions/AI-0027-media-analysis-attribute.md)). |

`Store` vs `Upload`: use **`Store`** for opaque content with built-in dedup (key = SHA-256 of the bytes); use **`Upload`** when the caller owns the naming convention (e.g. `{parentId}_thumb.jpg` variants).

## Recipe grammar (DX-0047)

`MediaRecipe.New()` returns a builder; chain stage verbs, then a terminal encode. The recipe is immutable and content-hashable — `.Fingerprint()` is the recipe-side half of the `(sourceHash, recipeFingerprint)` cache key.

| Verb | What it does |
|---|---|
| `.Fit(Fit.Cover, width, height)` / `.Crop("16:9")` / `.Resize(width:, height:, dpr:)` | Shape + size — CSS-aligned `object-fit` (`Cover` / `Contain` / `Fill` / `ScaleDown` / `None`). |
| `.Overlay(mediaId, …)` / `.OverlayText(text, …)` | Composite media or text layers (append in declared order). |
| `.Strip(MetadataKinds.All)` / `.Freeze(at)` | Drop metadata before encode; collapse an animated source to one frame. |
| `.EncodeAs("webp", Quality.Web)` | Encode as the named format, keeping animation/alpha if supported. |
| `.FlattenTo("jpeg", Quality.Web)` | Destructive encode — drops animation/alpha the target can't hold. |
| `.PreserveFormat(Quality.Print)` | Encode in the source's own format (the default if no encode is declared). |

`Quality.Thumbnail` / `.Web` / `.Print` (60 / 80 / 95) keep recipe JSON human-readable. `MutatorKind.Common` (= `Dimensions | Format | Quality`) is the opt-in URL override set; a query outside the allowlist returns **400** with a hint naming the recipe.

## Anti-patterns to flag

| If you see | Suggest |
|---|---|
| `Photo : Entity<Photo>` + a hand-rolled `byte[] Content` column / blob field | `Photo : MediaEntity<Photo>` — bytes live in Storage, the entity carries metadata + the media graph. |
| `Photo.Upload(...)` then a manual "is this duplicate?" `Query` | `Photo.Store(bytes, …)` — SHA-256 key + dedup is the contract; re-storing identical content is a no-op. |
| `services.AddControllers()` + a custom `[HttpGet("/media/...")]` resize endpoint | Reference `Koan.Media.Web`; `MediaController` already serves `GET /media/{id}/{recipe}`. |
| An `ImageSharp` `Image.Load(...).Mutate(...)` block inline in a service | `MediaRecipe.New()….EncodeAs(...)` (named recipe) or `stream.AsMedia(logger).Apply(recipe).WriteToAsync(...)` (ad-hoc). |
| Pre-rendering & storing every thumbnail size at upload | A `[MediaRecipe]` served lazily on first request, content-addressed + cached as a derivation (use `Eager = true` only to pre-warm). |
| A magic-number quality like `.EncodeAs("webp", 80)` | `Quality.Web` / `.Thumbnail` / `.Print` — keeps the recipe introspection JSON readable. |
| A separate vision/OCR call wired into the upload handler | `[MediaAnalysis(Analysis = MediaAnalysis.Describe \| MediaAnalysis.Ocr)]` — auto-runs on upload and feeds `[Embedding]`. |
| `MutatorKind.All` on a production variant | Scope to `MutatorKind.Common` (or the precise flags) — `All` is for ad-hoc/open recipes; an open allowlist is an SSRF/abuse surface. |

## Escape hatches

- **Ad-hoc transform (no registered recipe)** — the same fluent grammar the controller runs, off any byte stream, terminated by a streaming write that never fully buffers the output:
  ```csharp
  using Koan.Media.Core.Pipeline;     // StreamExtensions.AsMedia
  using Koan.Media.Abstractions.Recipes;
  var output = await sourceStream
      .AsMedia(logger)
      .Apply(MediaRecipe.New().Resize(width: 800).FlattenTo("jpeg", Quality.Web))
      .WriteToAsync(destStream, ct);  // streaming encode; MediaOutput carries content-type, dims, fingerprint
  ```
- **One decode, many outputs**: `pipeline.MaterializeAsync(b => b.Add("thumb", p => p.ResizeFit(200,200)).Add("hero", p => p.ResizeFit(1600,900)), ct)` → a `MediaBundle` keyed by variant name (one decode, N encodes).
- **Direct byte access**: `await Photo.OpenRead(key, ct)` returns the raw stored stream (no transform); `media.Url(ttl)` returns a presigned URL straight from the storage provider.
- **Config-defined recipes**: ops can add/override recipes via the `Koan:Media:Recipes` config section (`RecipeSource.Config` wins over a same-named `RecipeSource.Code` recipe) — no redeploy. `GET /media/recipes` lists every registered recipe and its format shortcuts.

## See also

- [Reference card: media.md](../../../docs/reference/cards/media.md) — one-screen pillar map
- [Media recipes how-to](../../../docs/guides/media-recipes-howto.md) — the authoritative walkthrough (recipes, mutators, serving, AI)
- [SnapVault](../../../samples/applications/SnapVault/README.md) — `PhotoAsset : MediaEntity<PhotoAsset>` with `[StorageBinding]` + `[Embedding]`, recipes, and optional AI/vector enrichment
- [MEDIA-0001 — media pillar baseline + storage integration](../../../docs/decisions/MEDIA-0001-media-pillar-baseline-and-storage-integration.md)
- [MEDIA-0004 — recipe pipeline](../../../docs/decisions/MEDIA-0004-recipe-pipeline.md) · [DX-0047 — fluent media transform API](../../../docs/decisions/DX-0047-fluent-media-transform-api.md) · [AI-0027 — media analysis attribute](../../../docs/decisions/AI-0027-media-analysis-attribute.md)
