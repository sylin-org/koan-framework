---
type: GUIDE
domain: media
title: "Media Recipes How-To"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2026-05-27
framework_version: v0.11.1
validation:
  status: not-yet-tested
  scope: docs/guides/media-recipes-howto.md
related_guides:
  - building-apis.md
  - entity-capabilities-howto.md
related_adrs:
  - MEDIA-0001
  - MEDIA-0003
  - MEDIA-0004
---

# Koan Media Recipes: From Stream to Served Bytes

This guide walks you through Koan's media pipeline, from your first
`stream.AsMedia()` to a production catalog with named variants, the
HTTP grammar, and a typed SPA client. We'll start with a single
preview and grow into multi-variant bundles, format-shortcut URLs,
and ops-tunable recipes in `appsettings.json`.

Each section follows the same rhythm: **Concepts** (what is this?),
**Recipe** (how do I set it up?), **Sample** (show me the code), and
**Usage Scenarios** (when would I use this?). Stop at any section
and you'll have something working.

**Related Guides:**
- Need controller-level patterns? → [Building APIs](building-apis.md)
- Entity-first storage layer underneath? → [Entity Capabilities](entity-capabilities-howto.md)

**Related Decisions:**
- [MEDIA-0001 — Pillar baseline + storage integration](../decisions/MEDIA-0001-media-pillar-baseline-and-storage-integration.md)
- [MEDIA-0003 — Variant routing + canonical signature](../decisions/MEDIA-0003-media-variant-routing-and-transforms.md)
- [MEDIA-0004 — Recipe pipeline + format preservation](../decisions/MEDIA-0004-recipe-pipeline.md)

---

## 0. Prerequisites

Add the Media packages alongside the Koan baseline. You'll consume
the core types from `Koan.Media.Abstractions` and the engine + HTTP
surface from `Koan.Media.Core` + `Koan.Media.Web`:

```xml
<PackageReference Include="Koan.Core" Version="*" />
<PackageReference Include="Koan.Media.Abstractions" Version="*" />
<PackageReference Include="Koan.Media.Core" Version="*" />
<PackageReference Include="Koan.Media.Web" Version="*" />
```

Boot the runtime in `Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
builder.Services.AddControllers();
var app = builder.Build();
app.MapControllers();
app.Run();
```

The recipe registry and pipeline engine register automatically. You
do need to provide one application-side service: an `IMediaSource`
that returns bytes for a given media id. We'll wire that in §5 when
we get to the HTTP surface.

---

## 1. Foundations: Your First Pipeline

**Concepts**

`Stream.AsMedia()` is the entry point. Everything else is a fluent
chain that's lazy until you call a terminal method like
`ToBytesAsync()`, `MaterializeAsync()`, or `ProbeAsync()`. The
pipeline takes ownership of your stream and disposes it for you.

The headline promise: **format is preserved unless you explicitly
change it**. An animated WebP stays animated; a transparent PNG
keeps its alpha; a wide-gamut JPEG stays in its color space.

**Recipe**

Nothing to register — `stream.AsMedia()` is a pure extension method.

**Sample**

The simplest possible round-trip:

```csharp
using Koan.Media.Core.Pipeline;

await using var source = File.OpenRead("photo.jpg");
var output = await source.AsMedia()
    .ResizeFit(800, 600)
    .ToBytesAsync();

Console.WriteLine($"{output.Format} {output.Width}x{output.Height}, {output.Bytes.Length} bytes");
// jpeg 800x600, 84321 bytes
```

Try the same call against an animated WebP:

```csharp
await using var source = File.OpenRead("dance.webp");
var output = await source.AsMedia()
    .ResizeFit(400, 400)
    .ToBytesAsync();

Console.WriteLine($"{output.Format}, {output.FrameCount} frames");
// webp, 24 frames
```

**Why this works:** The pipeline detects the source's encoded format
and routes the resize through ImageSharp's per-frame Mutate, then
selects the matching encoder. The 24 frames survive because the WebP
encoder writes them back; the same `ResizeFit` against a static
source produces a single-frame output without you doing anything
different.

**Usage Scenarios**

- Server-side thumbnail generation from uploads.
- Pre-warming previews for a catalog.
- One-off byte transformations inside a background job.

---

## 2. Format Operations: Preserving vs. Destroying

**Concepts**

Koan splits transforms into two categories that you mix freely in a
single pipeline:

- **Non-destructive verbs** preserve format, animation, alpha, and
  color depth. They're the default for everything in §1: `Resize`,
  `ResizeFit`, `Crop`, `Shape`, `AutoOrient`, `Rotate`,
  `FlipHorizontal/Vertical`, `Strip`, `PreserveFormat`, `EncodeAs`
  (when the target supports the source's properties).
- **Destructive verbs** are explicit caller intent. They drop
  animation, alpha, or color depth when the target format requires
  it: `FlattenTo`, `ExtractFrame`, `Quantize`, `DropAlpha`. The
  engine logs at Information when these run so the destruction is
  visible.

**Recipe**

No setup required.

**Sample**

```csharp
// Format-preserving — animation survives, alpha survives
await source.AsMedia()
    .ResizeFit(800, 600)
    .ToBytesAsync();

// Encode-as is non-destructive when the target supports the source's properties
await source.AsMedia()
    .EncodeAs("webp", quality: 80)
    .ToBytesAsync();        // animated WebP -> WebP, frames preserved

// FlattenTo is explicit destruction: animated source -> single JPEG frame
await source.AsMedia()
    .FlattenTo("jpeg", quality: 85)
    .ToBytesAsync();

// ExtractFrame is explicit destruction: pick frame 0 from animation
await source.AsMedia()
    .ExtractFrame(0)
    .EncodeAs("png")
    .ToBytesAsync();
```

**Why this works:** `EncodeAs` consults the encoder selector — when
you ask for `webp` against an animated WebP source, the WebP encoder
writes all frames. When the target format can't carry the source's
properties (animated → JPEG), you have to ask for `FlattenTo` and
accept the loss.

**Usage Scenarios**

- Preserve animation by default for hero images.
- `FlattenTo` for thumbnails where motion would distract.
- `ExtractFrame(0)` to capture a poster from animated content.

---

## 3. Shape, Fit, Position, Background

**Concepts**

Koan's shape vocabulary mirrors CSS `object-fit` so frontend devs
already know it. The shape step is a single slot keyed by four
orthogonal params:

| Param | Role |
|---|---|
| `crop` | Output shape — `square`, `16:9`, `4:3`, `400x200`, `400x200+100,50` |
| `fit` | How source maps into the shape — `cover` (default when `crop` set), `contain`, `fill`, `scale-down`, `none` |
| `position` | Anchor when there's freedom — `center` (default), `top`, `top-left`, `33%`, `x:33,y:50`, `focus` |
| `bg` | Fills blank pixels (letterbox / off-bounds crops / non-orthogonal rotation) — `transparent`, named colors, bare hex, `auto`, `dominant`, `blur` |

The shape stage runs before resize, so combining the two is "shape
first, then size."

**Recipe**

No setup required.

**Sample**

```csharp
// Square center-crop, source's native format
await source.AsMedia()
    .Crop("square")
    .ToBytesAsync();

// 16:9 hero, downsized to 1280x720, anchored at top
await source.AsMedia()
    .Shape(
        crop: CropSpec.Aspect(16, 9),
        position: Position.Top)
    .Resize(width: 1280)
    .ToBytesAsync();

// Contain inside 600x600 with a black letterbox
await source.AsMedia()
    .Shape(
        crop: CropSpec.Pixels(600, 600),
        fit: Fit.Contain,
        background: Background.Solid(BackgroundColor.Black))
    .ToBytesAsync();

// Transparent background for output formats that support alpha,
// white fallback for JPEG
await source.AsMedia()
    .Shape(
        crop: CropSpec.Square,
        fit: Fit.Contain,
        background: Background.Transparent(fallback: BackgroundColor.White))
    .EncodeAs("webp")
    .ToBytesAsync();

// Smart backgrounds: when you don't want letterboxing to be a
// distraction, derive the fill from the source itself. All three
// modes only fire with Fit.Contain + a fully-defined target canvas
// (both axes resolvable from the resize step).

// Spotify / YouTube hero-card aesthetic: cover-resize a blurred
// copy of the source behind the contained image. radius:0 (or the
// no-arg overload) lets the composer pick a sensible default
// (~4% of the canvas short edge).
await source.AsMedia()
    .Shape(
        crop: CropSpec.Pixels(800, 600),
        fit: Fit.Contain,
        background: Background.Blur())
    .Resize(600, 600)
    .ToBytesAsync();

// Single-color fill sampled from the source via 1×1 box-resample.
// Fast and visually reasonable for photos and album covers.
await source.AsMedia()
    .Shape(
        crop: CropSpec.Pixels(800, 600),
        fit: Fit.Contain,
        background: Background.Dominant())
    .Resize(600, 600)
    .ToBytesAsync();

// Border-strip average on a 16×16 down-sample. Best when the source
// has a deliberate background or frame (flat-colored title cards,
// product shots on a clean backdrop).
await source.AsMedia()
    .Shape(
        crop: CropSpec.Pixels(800, 600),
        fit: Fit.Contain,
        background: Background.Auto())
    .Resize(600, 600)
    .ToBytesAsync();
```

The same modes work in the URL grammar — pair with a recipe that
allows the `Background` mutator, or use ad-hoc:

```
GET /media/{id}?crop=800x600&fit=contain&bg=blur&w=600&h=600
GET /media/{id}?crop=800x600&fit=contain&bg=dominant&w=600&h=600
GET /media/{id}?crop=800x600&fit=contain&bg=auto&w=600&h=600
GET /media/{id}?crop=square&fit=contain&bg=1a1a1aff&w=400&h=400
```

`?bg=` without `?crop=`/`?aspect=` returns 400 — there's nothing to
fill without a target shape, and silently no-op'ing would mask typos.

**Why this works:** A 1200×800 source under `Crop("square")`
produces an 800×800 centered window. `Shape(crop: 16:9)` finds the
largest fitting 16:9 region. The order is fixed (shape → resize),
which means `?crop=square&w=300` produces a 300×300 output
regardless of how the caller spelled the URL.

For smart backgrounds, the composer runs after shape+resize: it
allocates a new Rgba32 canvas at the target size, paints the fill
(solid color / dominant / auto / cover-blur), then composites the
shaped image at the requested `Position`. The original image is
never modified — the canvas is what gets encoded.

**Usage Scenarios**

- Catalog tiles where every card is the same aspect.
- Hero banners cropped for a particular focal area.
- Letterboxed OG images that work in any aspect-ratio container.

---

## 4. Named Recipes: Code + Config

**Concepts**

A **recipe** is a named, immutable, content-hashable pipeline. You
define recipes once, reference them everywhere — code, HTTP URL,
SPA client. The same recipe ID maps to the same bytes for a given
source.

Two sources feed the registry:

1. **Code recipes**: a `[MediaRecipe]` attribute on a `static`
   method that returns `MediaRecipe`. Discovered automatically at
   boot.
2. **Config recipes**: a `Koan:Media:Recipes` section in
   `appsettings.json`. Ops can override code recipes here without
   redeploying — same JSON shape the `/media/recipes` endpoint
   emits, so you can paste an introspection response into config and
   it just works.

When both sources define the same name, **config wins** (with a
single Info log line at startup). When a code recipe collides with a
reserved format shortcut (`png`, `jpeg`, `webp`, `gif`, `avif`,
`bmp`, `tiff`), boot fails fast.

**Recipe**

Define a static class anywhere in your app (Koan scans every loaded
assembly):

```csharp
using Koan.Media.Abstractions.Recipes;

public static class CatalogRecipes
{
    [MediaRecipe("poster",
        Description = "Single-frame still, fits 800x800, WebP q80",
        Mutators = MutatorKind.Common | MutatorKind.Frame)]
    public static MediaRecipe Poster() => MediaRecipe.New()
        .ExtractFrame(0)
        .Crop("1:1")
        .Resize(width: 800).Name("size").Primary()
        .EncodeAs("webp", Quality.Web);

    [MediaRecipe("og",
        Description = "OpenGraph 1200x630, JPEG q85",
        Mutators = MutatorKind.Quality)]
    public static MediaRecipe OpenGraph() => MediaRecipe.New()
        .Crop("1200:630")
        .Resize(1200, 630).Primary()
        .EncodeAs("jpeg", 85);
}
```

To override the OG recipe in production without a deploy, ops drops
this into `appsettings.json`:

```jsonc
{
  "Koan": {
    "Media": {
      "Recipes": {
        "og": {
          "description": "OpenGraph 1200x630, WebP q80 (ops override)",
          "steps": [
            { "op": "crop", "aspect": "1200:630" },
            { "op": "resize", "width": 1200, "height": 630, "primary": true },
            { "op": "encodeAs", "format": "webp", "quality": 80 }
          ],
          "mutators": ["quality"]
        }
      }
    }
  }
}
```

**Sample**

Apply a recipe in code:

```csharp
var output = await source.AsMedia()
    .Apply(CatalogRecipes.Poster())
    .ToBytesAsync();
```

Or via the registry (typically what the controller does):

```csharp
var registry = serviceProvider.GetRequiredService<IMediaRecipeRegistry>();
var recipe = registry.Find("poster") ?? throw new InvalidOperationException();
var output = await source.AsMedia().Apply(recipe).ToBytesAsync();
```

**Why this works:** Each recipe carries a stable `Fingerprint()`
(SHA-256 over the canonicalised step list). Cache keys, ETags, and
CDN behaviour all derive from `(sourceHash, recipeFingerprint)`. The
allowlist of URL overrides comes from `Mutators` — `MutatorKind.Common`
covers `?w=`, `?h=`, `?format=`, `?q=` plus their aliases.

**Usage Scenarios**

- Pin the visual contract for a recipe used across thousands of cards.
- Hotfix the JPEG → WebP swap via ops config without a deploy.
- Audit which recipes the app exposes via the introspection endpoint
  (§5).

---

## 5. The HTTP Surface

**Concepts**

The controller exposes three GET shapes:

```
GET /media/{id}                            # original bytes, format-preserved
GET /media/{id}/{seed}                     # named recipe or format shortcut
GET /media/{id}/{seed}?w=600&format=png    # recipe with query-string overrides
GET /media/recipes                         # introspection: list all recipes
GET /media/recipes/{name}                  # single recipe + canonical JSON
GET /media/recipes/{name}?as=appsettings   # ready-to-paste config form
```

**Seed resolution order**: registered recipe → format shortcut
(`png`, `jpeg`, `webp`, `gif`, etc.) → 404. Recipe names cannot
collide with format shortcuts; the registry enforces this at boot.

**Override layering**: query params layer on top of the seed
according to the recipe's `Mutators` flags. Without a seed, the
controller builds an ad-hoc recipe from the params alone. Override
classes outside the recipe's allowlist return 400 with a hint
naming the recipe.

**Recipe**

You must provide an `IMediaSource` implementation. The controller
depends on this abstraction rather than `MediaEntity<T>` directly,
so tests and alternative storage backends drop in cleanly:

```csharp
using Koan.Media.Web.Routing;

public sealed class StorageMediaSource : IMediaSource
{
    public async Task<MediaSourceHandle?> OpenAsync(string id, CancellationToken ct)
    {
        var entity = await Photo.Get(id, ct);
        if (entity is null) return null;
        var stream = await entity.OpenReadAsync(ct);
        return new MediaSourceHandle(
            Id: id,
            Bytes: stream,
            ContentHashHex: entity.Sha256 ?? string.Empty,
            LastModified: entity.UpdatedAt);
    }
}

// Program.cs
builder.Services.AddSingleton<IMediaSource, StorageMediaSource>();
```

That's the only application-side wiring required. The
MediaController itself is auto-discovered by ASP.NET's controller
scan.

**Sample**

Call sites from a browser, `curl`, or any HTTP client:

```
# Original bytes, format preserved
GET /media/019e65a6.../

# Named recipe
GET /media/019e65a6.../poster

# Recipe + size override (allowed because Mutators includes Dimensions)
GET /media/019e65a6.../poster?w=400

# Format shortcut: re-encode source as PNG, preserve dimensions
GET /media/019e65a6.../png

# Format shortcut + ad-hoc resize
GET /media/019e65a6.../png?w=600

# Pure ad-hoc: no recipe seed, params build the pipeline
GET /media/019e65a6...?w=600&h=400&fit=cover&format=webp&q=75
```

The same URL order produces the same bytes regardless of param
spelling order — `?w=600&format=png` and `?format=png&w=600` are
identical because the pipeline always runs in canonical stage
order.

**Why this works:** Each response carries diagnostic headers that
make pipeline behaviour visible in DevTools without server log
dives:

| Header | Meaning |
|---|---|
| `X-Koan-Media-Recipe` | Effective recipe name (or `ad-hoc`) |
| `X-Koan-Media-RecipeHash` | Canonical fingerprint |
| `X-Koan-Media-SourceFormat` | Source's encoded format |
| `X-Koan-Media-OutputFormat` | Bytes you got back |
| `X-Koan-Media-FrameCount` | Animation frame count |
| `X-Koan-Media-FromCache` | `hit` / `miss` / `stale-fallback` |
| `X-Koan-Media-IgnoredParams` | Unknown params (relaxed mode) |

**Usage Scenarios**

- SPA `<img>` tags hitting `/media/{id}/poster` for every catalog tile.
- Server-side OG-image generation by hitting `/media/{id}/og` from
  the page renderer.
- One-off resizing in development via `/media/{id}?w=600`.

---

## 6. Multi-Variant Materialise: One Decode, Many Outputs

**Concepts**

When you need several variants of the same source (display +
thumbnail + poster + OG), the obvious approach decodes the source
once per variant. `MaterializeAsync` does it once total — every
branch shares the decoded image and only pays the encoder cost.

This is the killer feature for batch generation: pre-warming
variants at upload time, regenerating after a recipe change, or
producing a bundle of sizes for a responsive `<img srcset>`.

**Recipe**

No additional setup required — `MaterializeAsync` is on the
`IMediaPipeline` interface.

**Sample**

```csharp
await using var source = File.OpenRead("hero.webp");

var bundle = await source.AsMedia().MaterializeAsync(b => b
    .Add("display", v => v)                                              // original, preserved
    .Add("poster",  v => v.ExtractFrame(0).EncodeAs("png"))              // single frame, PNG
    .Add("thumb",   v => v.ResizeFit(400, 400).EncodeAs("webp", 70))     // small WebP
    .Add("og",      v => v.Shape(crop: CropSpec.Pixels(1200, 630))      // OG image
                          .FlattenTo("jpeg", 85)));

// bundle.Variants is a dictionary keyed by name
foreach (var (name, output) in bundle.Variants)
{
    Console.WriteLine($"{name}: {output.Format} {output.Width}x{output.Height} ({output.Bytes.Length}B)");
}
```

**Why this works:** The engine decodes the source once, then clones
the decoded image per branch so transforms don't bleed across
variants. A 4-variant bundle is roughly 5-10× faster than four
independent pipelines.

**Usage Scenarios**

- Upload-time pre-warming of every named variant a recipe defines.
- `srcset` generation: one pipeline call, four sizes.
- Migration jobs that regenerate the entire catalog after a recipe
  schema bump.

---

## 7. Probing Source Properties

**Concepts**

Sometimes you need to know what's in a source before you commit to
a pipeline — "is this animated?", "does it have alpha?", "what's
the aspect ratio?". `ProbeAsync` returns rich metadata in a single
call:

```csharp
public sealed record MediaInfo(
    string Format,
    int Width,
    int Height,
    int FrameCount,
    bool HasAlpha,
    int ColorDepth,
    int? ExifOrientation,
    bool HasIccProfile)
{
    public bool IsAnimated => FrameCount > 1;
}
```

**Sample**

```csharp
await using var source = File.OpenRead("uploaded.bin");
var info = await source.AsMedia().ProbeAsync();

if (info.IsAnimated)
{
    // Keep animation for display; extract frame 0 for the thumbnail
    var poster = await otherSource.AsMedia().ExtractFrame(0).EncodeAs("webp").ToBytesAsync();
}
else if (info.HasAlpha)
{
    // Preserve alpha for the cover
    var cover = await otherSource.AsMedia().PreserveFormat().ToBytesAsync();
}
else
{
    // Static source — JPEG is fine
    var cover = await otherSource.AsMedia().FlattenTo("jpeg", 85).ToBytesAsync();
}
```

Note that `Probe` is a terminal operation — the pipeline is consumed
after it returns. For the conditional logic above, open the source
twice (Probe on one stream, render on a second) or buffer the bytes
yourself.

**Why this works:** Probe loads the full image (not header-only) so
the alpha-channel and frame-count signals are reliably populated
across every input format. The cost is a single decode you'd have to
pay anyway if you went on to render.

**Usage Scenarios**

- Conditional pipelines that branch on source properties.
- Upload-time validation ("reject sources over 100 megapixels").
- Telemetry on what kinds of content your users upload.

---

## 8. Introspection: `/media/recipes`

**Concepts**

The introspection endpoint is the single source of truth for what
your app exposes. Three forms:

```
GET /media/recipes                         # list all + global metadata
GET /media/recipes/{name}                  # canonical JSON for one recipe
GET /media/recipes/{name}?as=appsettings   # wrapped in Koan:Media:Recipes for paste
```

Use it for:

- **Ops introspection**: "what recipes does this deployment expose?"
- **SPA client codegen**: hit it at build time, emit a typed
  `MediaRecipeName` union so `mediaUrl(id, 'postr')` is a compile
  error.
- **Hotfix paste-back**: query `?as=appsettings`, paste the JSON
  into your environment-specific config to override the recipe
  without code changes.

**Sample**

```bash
$ curl https://yourapp/media/recipes | jq '.recipes[].name'
"og"
"poster"
"thumb-400"

$ curl https://yourapp/media/recipes/poster
{
  "name": "poster",
  "version": 1,
  "description": "Single-frame still, fits 800x800, WebP q80",
  "source": "code",
  "fingerprint": "a1b2c3d4e5f6g7h8",
  "steps": [
    { "op": "extractFrame", "index": 0 },
    { "op": "crop", "aspect": "1:1" },
    { "op": "resize", "width": 800, "name": "size", "primary": true },
    { "op": "encodeAs", "format": "webp", "quality": 80 }
  ],
  "mutators": ["dimensions", "format", "quality", "frame"]
}
```

**Why this works:** The same JSON serialiser drives both the
endpoint and the recipe binder, so what you see is exactly what
config would accept. Round-trip via `?as=appsettings` is the
intended workflow.

**Usage Scenarios**

- Build-time codegen for SPA clients.
- Ops dashboard listing the deployment's media contract.
- Documentation generators that embed the live recipe list.

---

## 9. What's Next

You've got enough to build a real catalog now. Two areas worth
exploring as you grow:

- **`StorageMediaController<TEntity>`**: per-entity routes for raw
  byte streaming with full HTTP semantics (HEAD / Range /
  conditional GETs). Use it alongside the recipe pipeline when you
  want a route like `/api/photos/{key}` that returns the original
  bytes directly. See [Media Pillar Reference](../reference/media/index.md).
- **Multi-variant bundles in upload flow**: trigger
  `MaterializeAsync` at upload time so every named variant is
  pre-warmed when the first request arrives.

For the full URL grammar, mutator table, and configuration knobs:
[Media Pillar Reference](../reference/media/index.md).

For the design rationale (why format-preservation by default, why
the canonical stage order, why the override allowlist):
[MEDIA-0004](../decisions/MEDIA-0004-recipe-pipeline.md).

Migrating from the legacy `StreamTransformExtensions` (Koan.Media
v0.8 and earlier)? See
[Migration: Media v0.8 → v0.9](../migration/v0.8-to-v0.9-media.md).
