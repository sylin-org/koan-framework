---
type: REFERENCE
domain: media
title: "Media Pillar Reference"
audience: [developers, architects, ai-agents]
last_updated: 2026-05-27
framework_version: v0.11.1
status: current
validation:
  status: not-yet-tested
  scope: docs/reference/media/index.md
---

# Media Pillar Reference

**Document Type**: REFERENCE
**Target Audience**: Developers, Architects
**Last Updated**: 2026-05-27
**Framework Version**: v0.11.1

For the conceptual walkthrough, see the [Media Recipes How-To](../../guides/media-recipes-howto.md).
For the design rationale, see [MEDIA-0004](../../decisions/MEDIA-0004-recipe-pipeline.md).

---

## Installation

```bash
dotnet add package Koan.Media.Abstractions
dotnet add package Koan.Media.Core
dotnet add package Koan.Media.Web
```

```csharp
// Program.cs
builder.Services.AddKoan();
builder.Services.AddControllers();
builder.Services.AddSingleton<IMediaSource, YourMediaSource>();
app.MapControllers();
```

---

## Pipeline Entry

| Call | Returns | Behaviour |
|---|---|---|
| `stream.AsMedia()` | `IMediaPipeline` | Lifts a stream into a lazy pipeline. The pipeline disposes the stream at the first terminal call. |
| `stream.AsMedia(logger)` | `IMediaPipeline` | Same, with an `ILogger` for destructive-verb diagnostics. |

---

## Builder Verbs

### Non-destructive (preserve format / animation / alpha / color depth)

| Verb | Stage | Description |
|---|---|---|
| `AutoOrient(keep: false)` | Orient | EXIF-based; default-on. Pass `keep: true` to skip. |
| `Rotate(int degrees)` | Rotate | Explicit clockwise rotation. |
| `FlipHorizontal()` / `FlipVertical()` | Rotate | Mirror axis. |
| `Crop(string spec)` / `Crop(CropSpec)` | Shape | Sugar over `Shape(crop: ...)`. |
| `Shape(crop?, fit?, position?, background?)` | Shape | CSS-aligned shape step. Single slot. |
| `Resize(width?, height?, dpr = 1.0)` | Size | Single-axis preserves aspect; dual-axis honours the shape step's `Fit`. |
| `ResizeFit(int maxW, int maxH)` | Shape + Size | Convenience: contain within bounds. |
| `ResizeCover(int w, int h, position?)` | Shape + Size | Convenience: cover-crop to exact dimensions. |
| `Strip(MetadataKinds)` | Metadata | EXIF / ICC / XMP removal. |
| `PreserveFormat(quality = Quality.Web)` | Encode | Explicit "encode in source's format" (implicit if no encode declared). |
| `EncodeAs(string format, int quality = Quality.Web)` | Encode | Encode in named format; animation / alpha survive if target supports them. |

### Destructive (explicit caller intent; logged at Information)

| Verb | Stage | Description |
|---|---|---|
| `ExtractFrame(int index = 0)` | Frame | Animated → still. No-op on static sources. Out-of-range throws. |
| `FlattenTo(string format, int quality = Quality.Web)` | Encode | Format change that may drop animation / alpha / color depth. |

### Inspection

| Verb | Returns | Description |
|---|---|---|
| `ProbeAsync(ct)` | `Task<MediaInfo>` | Format, dimensions, frames, alpha, color depth, EXIF orientation, ICC presence. Consumes the pipeline. |
| `ToBytesAsync(ct)` | `Task<MediaOutput>` | Single-output materialisation. Disposes source. |
| `MaterializeAsync(builder, ct)` | `Task<MediaBundle>` | Multi-variant materialisation: one decode, N encodes. |
| `Apply(MediaRecipe recipe)` | `IMediaPipeline` | Apply every step from a recipe in one call. |

---

## Canonical Pipeline Stages

Steps always execute in stage order, regardless of declaration or
URL-param order:

```
1. Decode    (implicit)
2. Orient    (AutoOrient)
3. Frame     (ExtractFrame)
4. Timeline  (reserved for Koan.Media.Video)
5. Rotate    (Rotate, FlipHorizontal/Vertical)
6. Shape     (Crop + Fit + Position + Background)
7. Size      (Resize)
8. Overlay   (composition — media + text layers)
9. Metadata  (Strip)
10. Audio    (reserved for Koan.Media.Video)
11. Encode   (PreserveFormat / EncodeAs / FlattenTo) — always terminal
```

---

## Value Types

### `Quality` presets

| Preset | Value | Notes |
|---|---|---|
| `Quality.Thumbnail` | 60 | Small thumbnails, low-stakes previews |
| `Quality.Web` | 80 | Default for hero / content surfaces |
| `Quality.Print` | 95 | Archival / print-ready |
| `Quality.Lossless` | -1 | Sentinel; WebP picks lossless mode; PNG ignores; JPEG falls back to Print |

### `Fit` modes (CSS `object-fit` aligned)

| Mode | Behaviour |
|---|---|
| `Fit.Cover` | Default when `crop` set or both `w`/`h` given. Fills shape, crops overflow. |
| `Fit.Contain` | Fits source inside shape, leaves bg-filled space. |
| `Fit.Fill` | Stretches to fill, aspect broken. |
| `Fit.ScaleDown` | Like Contain but never upscales. |
| `Fit.None` | No resize; honours source dimensions. |

### `Position` anchors

| Form | Meaning |
|---|---|
| `Position.Center` (default) | Centered |
| `Position.Top`, `Bottom`, `Left`, `Right` | Edge anchors |
| `Position.TopLeft`, `TopRight`, `BottomLeft`, `BottomRight` | Corner anchors |
| `Position.Percent(0.33, 0.5)` | Per-axis fraction in [0,1] |
| `Position.Focus` | Use source media's stored focus point |

### `Background` kinds

| Factory | Behaviour |
|---|---|
| `Background.Transparent(fallback)` | Default. Preserved on alpha-capable outputs; falls back to `fallback` (default white) on JPEG. Composer is a no-op — no canvas extension. |
| `Background.Solid(BackgroundColor)` | Named (`Black`, `White`, `Red`, `Green`, `Blue`, `Gray`, `Silver`) or hex (`new BackgroundColor(0x1a, 0x1a, 0x1a, 0xff)`) or `rgba:r,g,b,a`. |
| `Background.Auto(fallback)` | Border-strip average on a 16×16 down-sample of the source. Computed per request (no cache today). Best for images with a deliberate frame/border. |
| `Background.Dominant(fallback)` | 1×1 box-resample of the source — fast area-average that reads as "dominant" for photos and covers. Computed per request. |
| `Background.Blur(radius)` | Cover-resize a clone of the source to fill the canvas, then Gaussian-blur. `radius:0` picks a sensible default (~4% of canvas short edge). |

Smart backgrounds (`Auto`, `Dominant`, `Blur`) only fire when paired with `Fit.Contain` and a fully-defined target canvas (both `width` and `height` resolvable from the recipe). With `bg=transparent` (the default), the composer is skipped entirely — preserving the v0.10 behavior where `Fit.Contain` produced a proportionally-sized image without padding.

### `CropSpec` parsers

| String form | Constructor | Notes |
|---|---|---|
| `"square"` | `CropSpec.Square` | 1:1 aspect |
| `"16:9"`, `"4:3"`, `"21:9"` | `CropSpec.Aspect(w, h)` | Aspect ratio |
| `"400x200"` | `CropSpec.Pixels(w, h)` | Literal pixel dimensions, anchor-respecting |
| `"400x200+100,50"` | `CropSpec.PixelsAt(w, h, x, y)` | Literal crop with explicit offset (ignores `position`) |

---

## Recipe Registration

### Code-side: `[MediaRecipe]`

```csharp
public static class MyRecipes
{
    [MediaRecipe(
        name: "poster",
        Description = "...",
        Version = 1,
        Mutators = MutatorKind.Common | MutatorKind.Frame,
        Eager = false)]
    public static MediaRecipe Poster() => MediaRecipe.New()
        .ExtractFrame(0)
        .Resize(width: 800).Name("size").Primary()
        .EncodeAs("webp", Quality.Web);
}
```

- Method must be `static`, parameterless, return `MediaRecipe` or `MediaRecipeBuilder`.
- Discovered via `AppDomain.GetAssemblies()` excluding framework / test infrastructure.
- Boot fails fast on duplicate names, reserved-name collisions, invalid step grammar.

### Config-side: `Koan:Media:Recipes`

```jsonc
{
  "Koan": {
    "Media": {
      "Recipes": {
        "poster": {
          "description": "...",
          "version": 1,
          "steps": [
            { "op": "extractFrame", "index": 0 },
            { "op": "resize", "width": 800, "name": "size", "primary": true },
            { "op": "encodeAs", "format": "webp", "quality": 80 }
          ],
          "mutators": ["common", "frame"]
        }
      }
    }
  }
}
```

- Schema mirrors the JSON `/media/recipes/{name}` emits.
- Hot reload via `IOptionsMonitor<RecipesOptions>`.
- Config **wins** over code on name collision (single Info log line at boot).

### `MutatorKind` flags

| Flag | Accepts override |
|---|---|
| `Dimensions` | `?w=`, `?h=`, `?width=`, `?height=`, `?dpr=` |
| `Format` | `?format=`, `?f=` |
| `Quality` | `?q=`, `?quality=` |
| `Frame` | `?frame=` |
| `Position` | `?position=` (rejected without crop) |
| `Background` | `?bg=` (named color, hex, `auto`, `dominant`, `blur`). Rejected without `crop=`/`aspect=` — there's nothing to fill without a target shape. |
| `Crop` | `?crop=`, `?aspect=` (replaces recipe's shape slot) |
| `Fit` | `?fit=` |
| `Rotate` | `?rotate=`, `?flip=` |
| `Strip` | `?strip=` |
| `Overlay` | `?overlay={mediaId}` plus `?overlay.size=`, `?overlay.position=`, `?overlay.padding=`, `?overlay.opacity=`, `?overlay.rotate=`, `?overlay.recipe=` |
| `Common` | Sugar for `Dimensions | Format | Quality` |
| `All` | Every kind |

---

## HTTP Surface

### Routes

| Route | Behaviour |
|---|---|
| `GET /media/{id}` | Original bytes, format-preserved |
| `GET /media/{id}/{seed}` | Named recipe or format shortcut |
| `GET /media/{id}/{seed}?param=value` | Recipe + query overrides (allowlist-checked) |
| `GET /media/recipes` | Introspection: all recipes + global metadata |
| `GET /media/recipes/{name}` | Single recipe + canonical JSON |
| `GET /media/recipes/{name}?as=appsettings` | Wrapped in `Koan:Media:Recipes` for paste |

### Seed resolution order

1. Named recipe (case-insensitive)
2. Format shortcut (`png`, `jpg`, `jpeg`, `webp`, `gif`, `bmp`, `tiff`, `avif`)
3. 404

Recipe names cannot collide with format shortcuts. Boot fails fast.

### Parameter aliases

| Short | Canonical |
|---|---|
| `w` | `width` |
| `h` | `height` |
| `q` | `quality` |
| `f` | `format` |
| `aspect` | `crop` |

### Diagnostic response headers

| Header | Meaning |
|---|---|
| `X-Koan-Media-Recipe` | Effective recipe name (or `ad-hoc`) |
| `X-Koan-Media-RecipeHash` | Canonical fingerprint |
| `X-Koan-Media-SourceFormat` | Source format |
| `X-Koan-Media-OutputFormat` | Output format |
| `X-Koan-Media-FrameCount` | Output frame count |
| `X-Koan-Media-FromCache` | `hit` / `miss` / `stale-fallback` |
| `X-Koan-Media-IgnoredParams` | Unknown params (relaxed mode) |
| `X-Koan-Media-LimitExceeded` | Set when an output dimension exceeds policy |
| `ETag` | `"{sourceShortHash}-{recipeFingerprint}"` |
| `Vary` | `Accept` (emitted when format isn't pinned) |

### Application-side wiring

```csharp
public interface IMediaSource
{
    Task<MediaSourceHandle?> OpenAsync(string id, CancellationToken ct = default);
}

public sealed record MediaSourceHandle(
    string Id,
    Stream Bytes,
    string ContentHashHex,
    DateTimeOffset? LastModified) : IAsyncDisposable;
```

The application registers one `IMediaSource` implementation; the
MediaController is auto-discovered by ASP.NET's controller scan.

---

## Per-Entity Streaming: `StorageMediaController<TEntity>`

For routes that serve raw stored bytes per-entity (not via the
recipe pipeline), inherit `StorageMediaController<TEntity>`:

```csharp
[Route("api/photos")]
public sealed class PhotosController : StorageMediaController<Photo>
{
    public PhotosController(IOptions<StorageMediaOptions> options) : base(options) { }
}
```

Provides: HEAD, GET, Range, If-None-Match, If-Modified-Since,
Content-Disposition, Cache-Control. The recipe pipeline at
`/media/{id}` is orthogonal — apps typically host both.

Options under `Koan:Media:Web:Storage`:

| Key | Default | Description |
|---|---|---|
| `EnableCacheControl` | `true` | Emit `Cache-Control` on raw responses |
| `Public` | `true` | Public vs private cache visibility |
| `MaxAge` | `1h` | Max-age for the header |

---

## Configuration

### `Koan:Media:Web` (MediaWebOptions)

| Key | Default | Description |
|---|---|---|
| `MaxOutputEdge` | `4096` | Hard cap on output dimension (px). Excess returns 400 with `X-Koan-Media-LimitExceeded: maxOutputEdge`. |
| `MaxSourceMegapixels` | `0` (disabled) | Pre-decode header-only check via `Image.IdentifyAsync`. Excess returns 400 with `X-Koan-Media-LimitExceeded: maxSourceMegapixels` before allocating the full decoded buffer. |
| `MaxFrameCount` | `0` (disabled) | Pre-decode frame-count cap; same diagnostic header. Protects against animation-bomb sources. |
| `StrictUnknownParams` | `false` | Unknown params return 400 (true) or surface in `X-Koan-Media-IgnoredParams` (false). |
| `AllowAdHoc` | `true` | Allow URLs with no recipe seed (false → 400 on bare param requests). |
| `RoutePrefix` | `/media` | Controller base path. |
| `DefaultCacheControl` | `public, max-age=3600, stale-while-revalidate=86400` | Variant cache header. |
| `ImmutableCacheControl` | `public, immutable, max-age=31536000` | Reserved for content-addressable URLs. |
| `OutputCache` | disabled | Persistent render cache; skips the pipeline on repeat requests. See below. |

### `Koan:Media:Web:OutputCache` (MediaOutputCacheOptions)

Persistent cache of rendered recipe output. Disabled by default; when
enabled, `MediaController` serves a stored render and skips the
resize/re-encode pipeline on a repeat request.

| Key | Default | Description |
|---|---|---|
| `Enabled` | `false` | Turn the render cache on. |
| `Path` | `null` | Cache directory. Relative paths resolve under `ContentRootPath`; the cache no-ops when empty. |

Behavior:

- **Key** is `(media id, recipe fingerprint)` — the same content-identity
  the `ETag` carries. A recipe edit bumps the fingerprint, so the key
  rotates and old entries orphan rather than serve stale.
- **Read-through / write-through.** A hit returns the stored bytes with
  `X-Koan-Media-FromCache: hit`; a miss renders, then persists
  write-through before responding.
- The default implementation is filesystem-backed: one file per render
  (`{shard}/{id}-{fingerprint}.{ext}`, the format slug as the extension so
  the content type round-trips without a sidecar), written via temp-file +
  atomic rename, with all IO best-effort so a cache fault never faults a
  response. It performs **no eviction** — reclaim space by deleting the
  directory (orphans accrue only on recipe edits / removed sources).
- `IMediaOutputCache` is a public, swappable service. Register your own
  (for example, a storage-profile backing) before `AddKoan()` to replace
  the filesystem default.

### `Koan:Media:Web:Storage` (StorageMediaOptions)

See `StorageMediaController<TEntity>` section above.

### `Koan:Media:Recipes`

Per-recipe declarations. Schema mirrors the
`/media/recipes/{name}` JSON output.

---

## Error Modes

| Condition | HTTP | Body / Header |
|---|---|---|
| Unknown media id | 404 | `{ "error": "Media '...' not found." }` |
| Unknown recipe / format shortcut seed | 404 | `{ "error": "Unknown recipe or format shortcut '...'." }` |
| Override outside recipe's `Mutators` allowlist | 400 | `{ "error", "rejected": [...], "recipe", "allowedMutators": [...] }` |
| Output dimension > `MaxOutputEdge` | 400 | `X-Koan-Media-LimitExceeded` header set |
| Source bytes don't decode | 422 | `{ "error": "Source bytes did not match any registered image format." }` |
| Boot — reserved-name collision | startup throw | `MediaRecipeBindingException` |
| Boot — config recipe invalid grammar | startup throw | `MediaRecipeBindingException` with offending path |

---

## Future / Reserved

- `PipelineStage.Timeline` and `PipelineStage.Audio` are reserved
  slots for the future `Koan.Media.Video` module. Image pipelines
  ignore them; video pipelines populate them.
- `BlurHash` / dominant-color placeholders for the SPA loading state
  are designed in MEDIA-0004 but not yet on the API surface.
- Per-source-hash caching of `Background.Auto` / `Background.Dominant`
  samples is not implemented today; each render recomputes. Adequate
  for catalogs where the response cache absorbs the cost; revisit
  when profiling shows the sample step is hot.

---

## See Also

- [Media Recipes How-To](../../guides/media-recipes-howto.md) — conceptual walkthrough
- [Migration: Media v0.8 → v0.9](../../migration/v0.8-to-v0.9-media.md) — upgrade from `StreamTransformExtensions`
- [MEDIA-0004 — Recipe pipeline](../../decisions/MEDIA-0004-recipe-pipeline.md) — design rationale
- [MEDIA-0003 — Variant routing + canonical signature](../../decisions/MEDIA-0003-media-variant-routing-and-transforms.md) — addressing model
- [MEDIA-0001 — Pillar baseline + storage integration](../../decisions/MEDIA-0001-media-pillar-baseline-and-storage-integration.md) — foundation
