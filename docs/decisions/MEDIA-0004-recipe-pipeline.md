---
id: MEDIA-0004
slug: recipe-pipeline
domain: Media
status: Proposed
date: 2026-05-26
title: Recipe-based media pipeline, format-preserving transforms, and overlay composition
---

**Status:** Proposed
**Date:** 2026-05-26
**Decision Makers:** Koan architecture group, Media pillar leads
**Affected Components:** Koan.Media.Abstractions, Koan.Media.Core, Koan.Media.Web, consuming applications (Downstream consumer and others)
**Supersedes:** DX-0047 (Fluent Media Transformation Pipeline API) — encoding policy and stream-extension API
**Extends:** MEDIA-0003 (Media variant routing and transforms) — adopts canonical signature and policy model; expands the operator vocabulary

---

| **Contract**         | **Details**                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| -------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **Inputs**           | Source media bytes (any decoder-supported format), named recipe registry (code attributes + appsettings), URL request `{id}[@{hash}][/{seed}][?params]`, request `Accept` header, signing key for ad-hoc URLs.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| **Outputs**          | Rendered media bytes, content-addressable variant URL, `X-Koan-Media-*` diagnostics, `/media/recipes` introspection JSON, BlurHash placeholders, materialized variant bundles for batch storage.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| **Error Modes**      | Unknown recipe → 404; recipe + format shortcut collision → boot failure; invalid step grammar → 400 with hint; mutator outside allowlist → 400 naming the recipe; output dimension exceeds policy → 400; source decode exceeds policy → 400; unsigned ad-hoc URL in production → 401; overlay recursion depth exceeded → 400; decoder failure → 422; concurrent decode pool saturated → 503 with `Retry-After`.                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| **Success Criteria** | Format, animation, alpha, and color depth preserved across every non-destructive transform. Destructive operations require explicit verbs (`FlattenTo`, `ExtractFrame`, `Quantize`, `DropAlpha`). One decode per request regardless of variant count. Recipe definition is identical in code, appsettings, and the `/media/recipes` JSON. URL grammar identical to builder vocabulary. Cache hit rate >95% on a steady catalog. Animated WebP/GIF/APNG round-trip without frame loss. Compatible with MEDIA-0003 variant routing and storage semantics.                                                                                                                                                                                                                                                                                                       |

---

## Context and Problem Statement

The current Koan.Media pipeline has two coupled deficiencies that surface as silent data loss for consumers:

1. **Format-destructive transforms.** Every method in `Koan.Media.Core.Extensions.StreamTransformExtensions` (implementing DX-0047) hard-codes `SaveAsJpegAsync` regardless of input. `ResizeFit`, `ResizeCover`, `Crop`, `Pad`, `Rotate`, `AutoOrient`, `OptimizeQuality`, and `FlipHorizontal/Vertical` all reduce the source to a single-frame JPEG. The cascade of losses:
   - Animated WebP / GIF / APNG → static still (first frame only)
   - PNG / WebP with alpha → opaque JPEG (transparency dropped)
   - Wide-gamut sources (Display P3, ProPhoto) → sRGB JPEG (color clipped)
   - ICC profiles → discarded
   - EXIF metadata → discarded
   The Downstream consumer investigation found this empirically: ~25% of GitHub-corpus README screenshots were animated WebPs whose motion was being stripped by the cover-extraction pipeline.

2. **Per-step encode amplification.** Each fluent call materializes through an intermediate `MemoryStream` via `Image.LoadAsync` → `Mutate` → `SaveAsJpegAsync`. A four-step chain (`AutoOrient → ResizeFit → Crop → ConvertFormat`) decodes and re-encodes the source four times. For multi-variant generation (`.Result().Branch()`) the encode cost multiplies linearly with branch count even when branches share preprocessing.

Beyond the defects, the developer experience is fragmented across three vocabularies:

- **Backend**: type-overloaded methods (`Resize(int,int)` vs `Resize(double,double)`), enum-typed helpers (`CropFrom`, `PadTo`).
- **HTTP** (per MEDIA-0003): operator-based query params (`?w=300&fit=cover&q=82`), with strict/relaxed alias resolution.
- **SPA**: no first-class client; consumers hand-build URLs.

The three surfaces drift independently. There is no single declarative artifact that says "what does this image look like?" — recipe authorship lives in code, in URLs, in JavaScript templates, and increasingly in `appsettings.json` ad-hoc patches.

This ADR consolidates the three surfaces around a single artifact — the **recipe** — and rebuilds the transform pipeline to preserve source disposition by default.

---

## Decision Drivers

1. **Source fidelity by default.** The framework must never silently destroy animation, alpha, color depth, ICC, or EXIF. Destructive transforms are explicit verbs invoked by the caller.
2. **One vocabulary, three surfaces.** Builder, HTTP query, and SPA client all speak the same step names, parameter names, and value semantics. Round-trip: `/media/recipes/{name}` → copy-paste → `appsettings.json` → behavior identical.
3. **Deterministic, content-addressable outputs.** A given source + recipe always produces identical bytes; cache keys are derived; HTTP cache headers can promise immutability.
4. **One decode per request.** Multi-variant generation reuses the decoded image; the only per-variant cost is the encoder pass.
5. **Operator discipline.** Recipe authors declare what callers can override; ad-hoc requests are bounded by registry policy plus signing.
6. **Cheap to add capabilities.** New steps (overlays, blur backgrounds, watermarks, focus points) plug into the same pipeline + URL + recipe-JSON surface without bespoke wiring.
7. **Compatible with MEDIA-0003 semantics.** Canonical signature, variant URL redirects, primitive-only eligibility, and storage layout are preserved.

---

## Decision

Replace the DX-0047 stream-extension API with a recipe-based pipeline composed of three layers:

- **Recipe model + registry** (`Koan.Media.Abstractions`): immutable step sequence, content-hashable, codified via `[MediaRecipe]` attribute and/or `appsettings.json` configuration.
- **Pipeline engine** (`Koan.Media.Core`): single-decode, multi-output execution surface; fluent builder for ad-hoc use; canonical stage ordering independent of caller invocation order.
- **HTTP surface** (`Koan.Media.Web`): `/media/{id}[@{shortHash}][/{seed}][?params]` URL grammar, `/media/recipes` introspection, content-addressable redirects, signed ad-hoc URLs, content negotiation.

Plus a typed SPA client and a `<KoanImage>` component that consume the introspection endpoint at build time.

### 1. Pipeline mental model — canonical stages

The pipeline executes in fixed stage order regardless of how steps were declared in the recipe or URL. URL parameter order is irrelevant to execution order.

| # | Stage         | Step kinds                                      | Notes                                                  |
|---|---------------|-------------------------------------------------|--------------------------------------------------------|
| 1 | Decode        | (implicit, always)                              | Loads all frames into `Image.Frames` collection        |
| 2 | Orient        | `AutoOrient`                                    | EXIF-based; default-on, opt-out via `?orient=keep`     |
| 3 | Frame         | `ExtractFrame(index)`                           | Animated → still; no-op on static sources              |
| 4 | Rotate / Flip | `Rotate(degrees)`, `Flip(H/V)`                  | Explicit orientation after EXIF normalisation          |
| 5 | Shape         | `Crop`, `Fit`, `Pad` (CSS-aligned shape step)   | Single slot; `crop` + `fit` + `position` + `bg`        |
| 6 | Size          | `Resize`, `ResizeX/Y`                           | Single slot; mutated by `?w=`/`?h=`/`?dpr=`            |
| 7 | Overlay       | `Overlay(media|text)` (N layers, indexed)       | Composited per-frame for animated hosts                |
| 8 | Metadata      | `Strip(MetadataKinds)`                          | EXIF / ICC / XMP removal                                |
| 9 | Encode        | `EncodeAs(format, quality)` — **always terminal** | Default: preserve source format                       |

A pipeline can omit any non-decode/non-encode stage. The recipe enumerates which stages are populated and what each step's parameters are.

**Why fixed stage order:**
`/media/{id}?w=600&format=png` and `/media/{id}?format=png&w=600` produce identical bytes — the only sensible interpretation, since "encode then resize then encode again" is degenerate.

### 2. Recipe model

A recipe is an ordered list of `MediaStep` instances, each pinned to a stage. The model:

```csharp
public sealed record MediaRecipe(
    string? Name,                              // null for anonymous (ad-hoc URLs)
    string? Description,
    IReadOnlyList<MediaStep> Steps,
    MutatorKind AllowedMutators,
    int Version);                              // bumped when step grammar changes

public abstract record MediaStep(PipelineStage Stage, string? Name);

public sealed record FitStep(...) : MediaStep(PipelineStage.Shape, Name);
public sealed record ResizeStep(...) : MediaStep(PipelineStage.Size, Name);
public sealed record OverlayStep(...) : MediaStep(PipelineStage.Overlay, Name);
public sealed record EncodeStep(...) : MediaStep(PipelineStage.Encode, Name);
// ... one record per step kind
```

**Fingerprint.** `recipe.Fingerprint()` returns a stable SHA-256 over the canonicalized step list (alias-normalised keys, sorted, defaults dropped, recipe version included). Cache key = `(sourceHash, recipeFingerprint)`.

**Named steps.** Recipe authors may attach `.Name("slug")` to any step. Named steps are addressable via the URL override grammar (`?{stepName}.{param}=`).

**Primary marker.** When a recipe has multiple steps of the same kind (e.g., two resize stages with different intents), the recipe author marks one with `.Primary()`. Unprefixed overrides (`?w=`) target the primary step. If multiple steps of the same kind exist and none is primary, unprefixed overrides return `400 Ambiguous` with a hint pointing at `/media/recipes/{name}`.

### 3. Recipe registry (code + appsettings, unified)

Two sources, one registry, same JSON schema.

**Code-based registration** via attribute on a static method:

```csharp
public static class MediaRecipes
{
    [MediaRecipe("poster",
        Description = "Single still frame, fits 800x800 square, WebP q80",
        Mutators = MutatorKind.Dimensions | MutatorKind.Format | MutatorKind.Quality | MutatorKind.Frame)]
    public static MediaRecipe Poster() => MediaRecipe.New()
        .ExtractFrame(0)
        .Crop(aspect: "1:1")
        .Fit(Fit.Cover, position: Position.Center)
        .Resize(width: 800).Name("size").Primary()
        .EncodeAs("webp", q: Quality.Web);

    [MediaRecipe("og",
        Description = "OpenGraph 1200x630, JPEG q85, blur background",
        Mutators = MutatorKind.Background | MutatorKind.Quality)]
    public static MediaRecipe OpenGraph() => MediaRecipe.New()
        .Crop(aspect: "1200:630")
        .Fit(Fit.Contain, bg: Background.Blur(radius: 40))
        .Resize(width: 1200, height: 630).Primary()
        .EncodeAs("jpeg", q: 85);
}
```

Discovered via Koan's `AddAllOf<>` scan pattern; the method must be `static`, return `MediaRecipe`, and carry the attribute.

**Config-based registration** under `Koan:Media:Recipes`. Schema is identical to the JSON shape that `/media/recipes/{slug}` emits — round-trippable by design:

```jsonc
{
  "Koan": {
    "Media": {
      "Recipes": {
        "poster": {
          "description": "Single still frame, fits 800x800 square, WebP q80",
          "steps": [
            { "op": "extractFrame", "index": 0 },
            { "op": "crop", "aspect": "1:1" },
            { "op": "fit", "mode": "cover", "position": "center" },
            { "op": "resize", "width": 800, "name": "size", "primary": true },
            { "op": "encodeAs", "format": "webp", "quality": 80 }
          ],
          "mutators": ["dimensions", "format", "quality", "frame"]
        }
      }
    }
  }
}
```

**Collision policy.** Config wins over code. When both register a recipe with the same name, the config recipe is used and a single Information log line is emitted at startup naming the override. This lets ops hotfix the visual contract without a redeploy.

**Validation depth.** Recipes from both sources are type-checked at registry binding (boot time). Unknown `op`, unknown mutator, out-of-range dimensions, missing required params → fail-fast with the offending JSON path. **Per Decision Driver #6**: boot-time failure beats first-request failure.

**Hot reload.** Config recipes participate in `IOptionsMonitor<RecipesOptions>`. A change to `appsettings.json` (or a configured environment-variable provider) refreshes the registry. Recipe cache entries are invalidated via the fingerprint — old recipe fingerprints become orphans, swept by the existing variant GC.

**Reserved-name enforcement.** Recipe names cannot collide with format shortcuts (`png`, `jpeg`, `webp`, `gif`, `avif`, `bmp`, `tiff`). Enforced at registry-load time; collision = boot failure.

### 4. Backend builder (programmatic use)

`Stream.AsMedia(ct)` is the single entry point. Everything else is fluent and lazy until materialization.

```csharp
// Single-output, format-preserving
var bytes = await source.AsMedia(ct)
    .AutoOrient()
    .Fit(Fit.Cover, width: 800, height: 600)
    .ToBytes(ct);                              // animated WebP stays animated

// Apply a named recipe
var bytes = await source.AsMedia(ct)
    .Apply(MediaRecipes.Poster())
    .ToBytes(ct);

// Multi-variant: one decode, many encodes
var bundle = await source.AsMedia(ct).Materialize(b => b
    .Add("display", v => v)                                                // original, preserved
    .Add("poster",  v => v.ExtractFrame(0))                                // still in source format
    .Add("thumb",   v => v.Fit(Fit.Cover, 400, 400).EncodeAs("webp", 70))
    .Add("og",      v => v.Fit(Fit.Cover, 1200, 630).FlattenTo("jpeg", 85))
, ct);

await PackageMedia.Store(bundle, keyPrefix: packageId, ct);
```

**Non-destructive verbs (preserve format/animation/alpha/color):**
`AutoOrient`, `Rotate`, `Flip*`, `Resize`, `ResizeX/Y`, `Fit`, `Crop`, `Pad`, `Overlay`, `Strip`, `EncodeAs` (target = source format).

**Destructive verbs (explicit, log at Information):**
- `FlattenTo("jpeg"|"png"|"webp"|..., q)` — format change; drops anim/alpha if target doesn't support them.
- `ExtractFrame(int index = 0)` — animated → still.
- `Quantize(colorCount)` — for palette-target outputs.
- `DropAlpha(Color background)` — explicit transparency removal.

**Inspection without re-decoding:**
- `Probe(ct)` returns `MediaInfo { Format, Width, Height, FrameCount, HasAlpha, ColorDepth, ExifOrientation, IccProfile, DominantColor }`.
- `Tap(out var info)` peeks pipeline state mid-flight without affecting the pipeline.

**Conditional steps** (source-aware recipes without forking):
```csharp
MediaRecipe.New()
    .WhenAnimated(p => p.ExtractFrame(0))      // collapse to still only if source is animated
    .WhenLargerThan(2000, p => p.ResizeFit(2000, 2000))
    .WhenFormat("webp", p => p.EncodeAs("webp", Quality.Web))
    .Else(p => p.EncodeAs("jpeg", 85))         // fallback when WhenFormat doesn't match
```

**Quality presets** (named constants, appear in `/media/recipes` JSON as canonical names):
```csharp
public static class Quality
{
    public const int Thumbnail = 60;
    public const int Web       = 80;
    public const int Print     = 95;
    public const int Lossless  = -1;           // resolved per-encoder (lossless mode flags)
}
```

### 5. CSS-aligned shape vocabulary

The shape stage (canonical position 5) is a single slot keyed by four orthogonal params: `crop`, `fit`, `position`, `bg`. Vocabulary matches CSS conventions so frontend devs need no new mental model.

#### `crop=` (defines output shape)

| Form                          | Meaning                                                |
|-------------------------------|--------------------------------------------------------|
| `crop=square`                 | Biggest fitting 1:1 from source                        |
| `crop=16:9`, `crop=4:3`, `21:9` | Biggest fitting aspect ratio                         |
| `crop=400x200`                | Literal pixel dimensions                               |
| `crop=400x200+100,50`         | Literal crop at explicit offset (`position=` ignored)  |
| `aspect=16:9`                 | Alias for `crop=16:9` (semantically honest shorthand)  |

#### `fit=` (how source maps into the crop shape)

| Value         | Behavior                                                   |
|---------------|------------------------------------------------------------|
| `cover` (default when `crop=` is set or when both `w` and `h` are given) | Fill the shape, crop overflow |
| `contain`     | Fit source inside the shape, leave bg-filled space        |
| `fill`        | Stretch to fill, aspect broken (CSS default)              |
| `scale-down`  | Like `contain` but never upscales                         |
| `none`        | No resize; honor source dimensions                        |

#### `position=` (anchor when there's freedom)

| Value                        | Meaning                                            |
|------------------------------|----------------------------------------------------|
| `center` (default)           |                                                    |
| `top`, `bottom`, `left`, `right` | Edge anchors                                   |
| `top-left`, `top-right`, etc.    | Corner anchors                                 |
| `33%`                        | Single percent on the cropped axis                 |
| `x:33,y:50`                  | Explicit per-axis percentage                       |
| `focus`                      | Use the source media's stored focus point (see §10) |

#### `bg=` (fills blank pixels: letterbox, off-bounds crops, non-orthogonal rotations)

| Form                          | Behavior                                                   |
|-------------------------------|------------------------------------------------------------|
| `bg=transparent` (default)    | Preserved if output supports alpha; falls back to `bg-fallback` otherwise |
| `bg=black`, `bg=white`, named | CSS named colors                                           |
| `bg=1a1a1a`                   | Hex RGB (no `#` — URL fragment marker)                     |
| `bg=1a1a1aa0`                 | Hex RGBA                                                   |
| `bg=rgba:0,0,0,0.5`           | Decimal form, comma-separated                              |
| `bg=auto`                     | Sample border pixels, average → solid color                |
| `bg=dominant`                 | k-means dominant color (cached per source hash)            |
| `bg=blur`                     | Source upscaled + Gaussian blurred, contained image overlaid on top (Instagram/Spotify style) |
| `bg-fallback=white` (default) | Solid used when `bg=transparent` and output is alpha-incapable (JPEG) |
| `bg-blur=N`                   | Blur radius when `bg=blur` (default = 5% of longest edge) |

**Composition rules:**

- `crop` defines shape; `fit` defines how source maps into it; `position` resolves anchor freedom; `bg` fills any introduced blanks.
- `crop=square&w=300` → crop to 800×800 first, then resize to 300×300. **Shape first, then size.**
- `?w=600` on a 1200×800 source (no `crop=`) → 600×400 (single-axis scale, aspect preserved).
- `?w=600&h=600` (no `crop=`) → default `fit=cover` (filling the requested dimensions).
- `?w=600&h=600&fit=contain&bg=black` → 600×400 centered in 600×600 black box.
- `?crop=square&fit=contain&w=300&bg=auto` → 300×300, contained 300×200 source, bg sampled from edge.
- `?w=600` on a recipe whose primary step is `Fit(Cover)` → proportional scale (both axes scaled by same factor; cover ratio preserved).

#### Mutator declaration

Recipe authors declare which override classes the recipe accepts:

```csharp
[Flags]
public enum MutatorKind
{
    None       = 0,
    Dimensions = 1 << 0,   // w, h, dpr
    Format     = 1 << 1,   // format, q
    Quality    = 1 << 2,
    Frame      = 1 << 3,
    Position   = 1 << 4,
    Background = 1 << 5,
    Crop       = 1 << 6,   // shape change (replaces recipe's crop slot)
    Fit        = 1 << 7,
    Overlay    = 1 << 8,
    Rotate     = 1 << 9,
    Strip      = 1 << 10,
}
```

Resolved override rules:
- Override outside the recipe's declared `Mutators` → 400 with hint.
- `?crop=` on a recipe with no crop step but `MutatorKind.Crop` declared → fills the slot.
- `?crop=` on a recipe that already declares `crop=16:9` without `MutatorKind.Crop` → 400.
- `?position=` without any crop step (recipe or override) → 400 (silent no-ops mask typos).
- `?bg=` honored only when `MutatorKind.Background` is declared.

### 6. Background system — implementation notes

- **`bg=auto` and `bg=dominant` are cached** by `(sourceHash, computeKind)`. First request computes; subsequent requests against any recipe touching that source reuse the cached color. Cost: ~1ms (auto) or ~20ms (dominant) once per source, ever.
- **`bg=blur` ships in v1.** Implementation: produce the contained foreground; in parallel, resize source to box dims at `Cover`, apply Gaussian blur at `bg-blur` radius, composite foreground on top. Cost: ~20-40ms beyond the contained pass.
- **Format/alpha matrix:**

| Output format | Alpha support | `bg=transparent` honored?         |
|---------------|---------------|-----------------------------------|
| WebP          | full          | yes                               |
| PNG / APNG    | full          | yes                               |
| AVIF          | full (when encoder ships) | yes                   |
| GIF           | 1-bit mask    | yes (binary)                      |
| JPEG          | none          | falls back to `bg-fallback`       |

The `X-Koan-Media-BgFallback: transparent→white` response header surfaces silent substitutions for debugging.

### 7. Overlay system

Composition layers added at stage 7 (between metadata and encode). Each layer's source is itself a Koan media row, optionally pre-processed by a recipe.

#### URL grammar

```
?overlay={id}                                  # alias for overlay.0={id}
  &overlay.recipe={recipeName}                 # apply a recipe to the overlay before compositing
  &overlay.size=0.1                            # 10% of host's longest edge; or 100x, x50, 120x40
  &overlay.position=br                         # same vocabulary as shape's position=
  &overlay.padding=0.05                        # 5% of host's longest edge; or px value
  &overlay.opacity=0.6
  &overlay.rotate=-15
```

Multi-layer via index — layers composite in index order (lower index = drawn first / further back):

```
?overlay.0={logoId}&overlay.0.position=br&overlay.0.size=0.1
&overlay.1={badgeId}&overlay.1.position=tl&overlay.1.size=0.08
```

#### Text overlays

Same step kind, different source — useful for OG image generation:

```
?overlay.0.text=Hello%20World
  &overlay.0.font=Inter
  &overlay.0.color=white
  &overlay.0.position=center
```

`{{year}}`, `{{sourceId}}`, `{{width}}`, `{{height}}` are template tokens substituted at render time — supports dynamic copyright stamps without per-request recipe generation.

#### Recipe JSON

```jsonc
{
  "op": "overlay",
  "layers": [
    {
      "source": { "kind": "media", "id": "{guid}", "recipe": "mono-white" },
      "position": "br",
      "size": 0.08,
      "padding": 0.04,
      "opacity": 1.0
    },
    {
      "source": { "kind": "text", "value": "© {{year}}", "font": "Inter", "color": "white" },
      "position": { "x": "50%", "y": "92%" },
      "opacity": 0.7
    }
  ]
}
```

#### Behavioural decisions

| Knob                        | Decision                                                                                    |
|-----------------------------|---------------------------------------------------------------------------------------------|
| `size` reference axis       | Longest edge of host (default); explicit `WxH` for literal pixels                           |
| `padding` reference         | Same as `size` — longest edge of host                                                       |
| Default `position`          | `center` — neutral; watermark callers opt into `br`                                         |
| Default `size`              | Overlay's natural dimensions (no auto-shrink)                                               |
| Default `opacity`           | `1.0`                                                                                       |
| Animated host + static overlay | Overlay composites onto every frame                                                      |
| Animated host + animated overlay | Overlay loops to match host frame count; differing durations resolved by elapsed-time mapping |
| Permissions                 | Caller must have read access to both host and overlay (reuses existing auth path)           |
| Cache key contribution      | Includes overlay source hash + nested recipe fingerprint + overlay params                   |
| Recipe recursion depth      | Capped at 2 (overlay's recipe may itself include an overlay, but no deeper)                |

#### Font availability

`Koan.Media.Web` ships a default font (Inter Variable) bundled with the package. Custom fonts register via:

```csharp
services.AddKoanFont("brand", "wwwroot/fonts/BrandSans.ttf");
services.AddKoanFont("mono",  "wwwroot/fonts/JetBrainsMono.ttf");
```

Fonts not registered (or not bundled) → 400 with the registered font list. Keeps the attack surface bounded — no arbitrary filesystem reads.

### 8. HTTP surface — URL grammar

```
GET /media/{id}[@{shortHash}][/{seed}][?params]
```

| Segment       | Meaning                                                                          |
|---------------|----------------------------------------------------------------------------------|
| `{id}`        | Media GUID                                                                        |
| `@{shortHash}` | Optional content-addressable suffix (first 12 chars of source SHA-256). When present, response is `Cache-Control: public, immutable, max-age=31536000`. |
| `{seed}`      | Recipe name OR format shortcut (`png`, `jpeg`, `webp`, `gif`, `avif`). Absent = original |
| `?params`     | Layer on top of seed via mutator rules                                            |

**Seed resolution order:** registered recipe → format shortcut → 404. Reserved-name enforcement (§3) ensures these can never collide.

#### Override layering

- Named recipe seed → mutators apply per the recipe's declared `Mutators` (400 outside allowlist).
- Format shortcut seed → equivalent to "preserve source, then `EncodeAs({shortcut}, q=85)`". Mutators apply as if a recipe declared `MutatorKind.Dimensions | Format | Quality | Strip`.
- No seed → pure ad-hoc: every param becomes a step in a freshly-built pipeline. Allowlist of permitted ad-hoc steps configured via `Koan:Media:AdHoc:AllowedSteps` (default: `fit, cover, crop, rotate, flip, pad, strip, frame, format, quality, position, bg`).

#### Worked examples (source 1200×800)

| URL                                                   | Result                                                              |
|-------------------------------------------------------|---------------------------------------------------------------------|
| `/media/{id}`                                         | Original bytes, format preserved                                    |
| `/media/{id}@a1b2c3d4e5f6`                            | Same as above, immutable cache headers                              |
| `/media/{id}/poster`                                  | Registered recipe                                                   |
| `/media/{id}/poster?width=1200`                       | Poster with size mutator                                            |
| `/media/{id}/png`                                     | Source re-encoded as PNG                                            |
| `/media/{id}/png?w=600`                               | Source resized to 600×400 then encoded as PNG                       |
| `/media/{id}?w=600&format=png`                        | Same as `/png?w=600` — order is structural, not URL-positional      |
| `/media/{id}?crop=square&fit=contain&bg=blur&w=400`   | 400×400 with blur background, source contained                      |
| `/media/{id}/og?overlay={logoId}&overlay.position=br` | OG recipe with overlay mutator                                      |
| `/media/{id}/poster?frame=2`                          | Frame 2 (if recipe declares `MutatorKind.Frame`)                    |

#### Signing

Ad-hoc URLs (no recipe seed) require HMAC signatures in production:

```
?s=<hex-hmac>&exp=<unix-ts>
```

Signature payload: canonicalized query string + path + expiry. Default mode by environment:
- `Development`: signing disabled (open ad-hoc)
- `Staging`: signing required, warnings on bypass attempts
- `Production`: signing required, requests without `s` → 401

Named-recipe URLs (with or without mutator overrides) are unsigned — the recipe registry is the capacity cap. Mutator violations return 400 before any work happens.

Signing key sourced from `Koan:Media:SigningKey` (or referenced secret via Koan's standard secret resolution).

#### Content negotiation

When neither the seed nor `?format=` pins an output format, the response format is selected by the request's `Accept` header against the source's encodable formats. AVIF > WebP > PNG (for transparency) > JPEG, restricted to formats the client advertises. `Vary: Accept` is mandatory on negotiated responses.

#### HTTP cache hygiene

- ETag: `"{sourceHashShort}-{recipeFingerprint}"`.
- `If-None-Match` → 304 with empty body.
- `Cache-Control`:
  - With `@{shortHash}` in URL: `public, immutable, max-age=31536000`
  - Without: `public, max-age=3600, stale-while-revalidate=86400`
- `Vary: Accept` when content-negotiated.

#### Diagnostic headers

| Header                              | Meaning                                                  |
|-------------------------------------|----------------------------------------------------------|
| `X-Koan-Media-Recipe`               | Effective recipe name (or `ad-hoc`)                      |
| `X-Koan-Media-RecipeHash`           | Canonical fingerprint                                    |
| `X-Koan-Media-SourceFormat`         | Decoded source format                                    |
| `X-Koan-Media-OutputFormat`         | Encoded output format                                    |
| `X-Koan-Media-FrameCount`           | Source frames (`1` for static)                           |
| `X-Koan-Media-FromCache`            | `hit` / `miss` / `stale-fallback`                        |
| `X-Koan-Media-DecodeMs`             | Decode time (rounded to int ms)                          |
| `X-Koan-Media-EncodeMs`             | Encode time                                              |
| `X-Koan-Media-IgnoredParams`        | Comma-separated unknown params (Relaxed mode)            |
| `X-Koan-Media-BgFallback`           | `transparent→white` when alpha substitution happened     |

### 9. `/media/recipes` — introspection

```
GET /media/recipes                  # list all
GET /media/recipes/{name}            # single recipe
GET /media/recipes/{name}?as=appsettings  # nested under Koan:Media:Recipes for copy-paste
GET /media/recipes/{name}?as=ts      # TypeScript type fragment for codegen
```

Returns the canonical JSON shape:

```jsonc
{
  "recipes": [
    {
      "name": "poster",
      "version": 1,
      "description": "Single still frame, fits 800x800 square, WebP q80",
      "source": "code",                  // or "config" or "config-override"
      "steps": [
        { "op": "extractFrame", "index": 0 },
        { "op": "crop", "aspect": "1:1" },
        { "op": "fit", "mode": "cover", "position": "center" },
        { "op": "resize", "width": 800, "name": "size", "primary": true },
        { "op": "encodeAs", "format": "webp", "quality": 80 }
      ],
      "mutators": ["dimensions", "format", "quality", "frame"]
    }
  ],
  "formatShortcuts": ["png", "jpeg", "webp", "gif"],
  "adHocSteps": ["fit", "cover", "crop", "rotate", "flip", "pad", "strip", "frame", "format", "quality"],
  "paramAliases": {
    "w": "width", "h": "height", "q": "quality"
  },
  "signing": { "mode": "ad-hoc-required-in-production" },
  "limits": { "maxOutputEdge": 4096, "maxSourceMegapixels": 100 }
}
```

This endpoint is the **single source of truth** consumed by:
- The SPA build-time codegen (typed `MediaRecipeName` union)
- OpenAPI generation
- Operators copy-pasting into `appsettings.json` (with `?as=appsettings`)
- The `/media/recipes` UI in the admin dashboard

### 10. Source-level features

#### Focus points

Each media row can carry an optional focus point: `{ x: 0.0-1.0, y: 0.0-1.0 }`. When recipes use `position=focus`, crops anchor to that point. Source of the focus point:
- Admin manually sets via `POST /api/admin/media/{id}/focus { "x": 0.42, "y": 0.31 }`
- Saliency auto-detection deferred to v2 (manual focus delivers ~80% of the value at ~5% of the code).

#### BlurHash / LQIP placeholders

Generated once at upload, stored alongside variants:

```
GET /media/{id}/placeholder
GET /media/{id}/placeholder?as=blurhash   # returns ~30-char BlurHash string
GET /media/{id}/placeholder?as=lqip       # returns ~50-byte base64 data-URI (PNG, 32x16, q1)
```

Both forms are generated at upload time and cached. v1 ships the endpoint; backfill task for legacy media is a one-shot.

### 11. Multi-variant materialization

`Materialize` produces N outputs from one decoded image:

```csharp
public sealed record MediaBundle(IReadOnlyDictionary<string, MediaOutput> Variants);
public sealed record MediaOutput(byte[] Bytes, string ContentType, string Etag, MediaInfo Info);
```

```csharp
var bundle = await source.AsMedia(ct).Materialize(b => b
    .Add("display", v => v)
    .Add("poster",  v => v.ExtractFrame(0))
    .Add("thumb",   v => v.Fit(Fit.Cover, 400, 400).EncodeAs("webp", 70))
, ct);

await PackageMedia.Store(bundle, keyPrefix: packageId, ct);
```

Cost model: one decode + N encodes. For 4 variants of a typical photo this is 5-10× faster than 4 independent pipelines.

`PackageMedia.Store(bundle, keyPrefix)` lays variants under one content-addressed prefix: `{sha}/display.webp`, `{sha}/poster.webp`, etc. Per-variant URLs become `/media/{id}/{variantName}`.

### 12. SPA client and `<KoanImage>`

Build-time codegen consumes `/media/recipes` and emits:

```ts
// Generated; do not edit
export type MediaRecipeName = 'poster' | 'thumb-400' | 'og' | 'png' | 'jpeg' | 'webp' | 'gif';
export type MediaParam = 'width' | 'height' | 'format' | 'quality' | 'frame' | 'position' | 'bg';

export function mediaUrl(id: string, recipe?: MediaRecipeName, params?: MediaUrlParams): string;
export function mediaSrcSet(id: string, recipe: MediaRecipeName, widths: number[]): string;
```

`<KoanImage>` (Preact/React) wraps `<picture>` with auto srcset, lazy loading, BlurHash placeholder, fade-in on load, and error fallback:

```tsx
<KoanImage
  id={pkg.coverId}
  recipe="poster"
  sizes="(max-width: 768px) 400px, 800px"
  placeholder="blurhash"
/>
```

The component reads its variant union from the generated types — `recipe="postr"` is a compile error.

### 13. Safety and operability

| Concern                       | Default                                       | Override path                                  |
|-------------------------------|-----------------------------------------------|------------------------------------------------|
| Max output edge (pixels)      | 4096                                          | `Koan:Media:Limits:MaxOutputEdge`              |
| Max source megapixels         | 100                                           | `Koan:Media:Limits:MaxSourceMegapixels`        |
| Max decoded frame count       | 600                                           | `Koan:Media:Limits:MaxFrameCount`              |
| Decode concurrency cap        | `Environment.ProcessorCount`                  | `Koan:Media:Limits:DecodeConcurrency`          |
| Ad-hoc URL signing            | Disabled in Dev, required in Staging/Prod     | `Koan:Media:Signing:Mode`                      |
| Cache key salt                | Empty                                         | `Koan:Media:CacheKeySalt` (rotate to bust all) |

Limit violations return 400 with the specific cap that fired, exposed via `X-Koan-Media-LimitExceeded: maxOutputEdge`.

Decode pool saturation returns 503 with `Retry-After`.

### 14. Operational hooks

- `POST /api/admin/media/{id}/warm` — pre-generates all named recipes for one media row. Idempotent; cached variants are no-ops.
- `[MediaRecipe("poster", Eager = true)]` — recipe pre-warms at upload time. Default is lazy (generate-on-first-request).
- Variant cache invalidation is implicit — recipe fingerprint changes (code edit, config patch) rotate the cache key. Orphaned bytes are reaped by the existing variant GC sweep.

> **Implementation status (Koan.Media.Web 0.11.2).** The variant cache shipped as `IMediaOutputCache`, consulted by `MediaController` before the pipeline and populated write-through after, keyed on `(id, recipeFingerprint)` as designed. It is **opt-in** via `Koan:Media:Web:OutputCache` (`Enabled` + `Path`); the default is a no-op. The default backing is filesystem (one file per render), not Koan.Storage. **No GC sweep is implemented** — invalidation-by-fingerprint means stale entries are never served, but orphans are reclaimed manually (delete the cache directory). The `warm` endpoint and `Eager` pre-warm above remain unimplemented. See [Media reference → OutputCache](../reference/media/index.md#koanmediaweboutputcache-mediaoutputcacheoptions).

---

## Consequences

### Positive

- **Source fidelity preserved by default.** Animated WebP / GIF / APNG round-trip; alpha and ICC retained.
- **One decode per request**, regardless of variant count. ~5-10× faster on multi-variant generation.
- **Single vocabulary** across backend, HTTP, SPA, and config. Round-trippable JSON between `/media/recipes/{slug}` and `appsettings.json`.
- **Content-addressable URLs** unlock immutable HTTP caching with zero invalidation logic.
- **Cheap to extend.** Overlays, blur backgrounds, focus points, watermarks, and OG-image generation are all natural compositions of the same step model.
- **Ops can override visual contract** without redeploy via `appsettings.json` recipe definitions.
- **Diagnostic headers** make pipeline behavior debuggable in DevTools without server log dives.
- **Compatible with MEDIA-0003**: canonical signature, variant URL pattern, storage layout, primitive-only eligibility all preserved.

### Negative

- **Storage budget increases.** Preserving animated WebP variants is bigger than JPEG stills. Acceptable per stakeholder confirmation; bounded by named-recipe set + lazy materialization.
- **CPU cost on animated sources.** Per-frame resize + per-frame composite on overlays. Mitigation: existing `MaxOutputEdge` cap; animated covers are usually small.
- **Memory pressure on large animations.** Sum of frame buffers during decode. Mitigation: `MaxFrameCount` cap; bounded decode concurrency.
- **Breaking change** to `Koan.Media.Core` API surface. Acceptable — Koan.Media is internal-use only at present, per stakeholder confirmation.
- **New dependencies**: BlurHash library (`Blurhash.ImageSharp` or equivalent), font asset (Inter Variable bundled).

### Neutral

- Recipe authors carry a small new responsibility: declare `MutatorKind` flags appropriate to the recipe's intent. Mitigated by sensible defaults (`Dimensions | Format | Quality` for most recipes).
- The unified shape vocabulary (`crop` + `fit` + `position` + `bg`) replaces three legacy operations (`ResizeFit`, `ResizeCover`, `Pad`). Authors of recipes built on the old vocabulary migrate to the unified surface during the rewrite (one-time cost).

---

## Migration path

### Phase 1 — Core pipeline (Koan.Media.Abstractions + Koan.Media.Core)

1. Introduce `MediaRecipe`, `MediaStep`, `PipelineStage`, `MutatorKind`, `Quality`, `Fit`, `Position`, `Background` types.
2. Implement `IMediaPipeline` engine with canonical stage execution and `Image`-shared multi-variant `Materialize`.
3. Implement the `IImageEncoder` selector keyed on `Image.Metadata.DecodedImageFormat`.
4. Implement non-destructive verbs first (`Resize`, `Fit`, `Crop`, `Pad`, `AutoOrient`, `Rotate`, `Flip*`, `Strip`).
5. Implement destructive verbs (`FlattenTo`, `ExtractFrame`, `Quantize`, `DropAlpha`) with explicit Information logging.
6. Implement `Probe`, `Tap`, conditional steps (`WhenAnimated`, `WhenLargerThan`, `WhenFormat`/`Else`).
7. Implement `MediaRecipe.Fingerprint()` (canonical SHA-256).

### Phase 2 — Registry

8. Implement `IMediaRecipeRegistry` with code (`[MediaRecipe]` scan) + config (`Koan:Media:Recipes`) sources.
9. Boot-time validation with fail-fast on collisions / unknown ops / reserved-name violations.
10. `IOptionsMonitor`-based hot reload for config recipes.

### Phase 3 — HTTP surface (Koan.Media.Web)

11. Rewrite `MediaController` URL grammar per §8.
12. Implement override layering (mutator allowlist + ad-hoc fallback).
13. Implement content negotiation, signing, diagnostic headers, content-addressable URL handling.
14. Implement `/media/recipes` introspection endpoint with `as=appsettings|ts` query.

### Phase 4 — Smart features

15. Implement `bg=blur`, `bg=dominant`, `bg=auto` with per-source caching.
16. Implement overlay step with media + text sources, recipe nesting (depth-2 cap), font registration.
17. Implement focus point storage + `position=focus` resolution.
18. Implement `/media/{id}/placeholder` BlurHash + LQIP generation, eager generation on upload.

### Phase 5 — SPA and codegen

19. `<KoanImage>` component (Preact + React variants).
20. Build-time codegen from `/media/recipes` introspection.
21. Typed `mediaUrl`, `mediaSrcSet` helpers.

### Phase 6 — Sample + consumer migration

22. Migrate `samples/S6.SnapVault` to recipe-based usage; remove DX-0047 calls.
23. Migrate `DownstreamConsumer.FetchPreviewImageJob` to drop the forced `ConvertFormat("jpeg")` call; add a `poster` recipe for static thumbnails alongside the preserved-format display variant.
24. Backfill task: regenerate previews for the existing GitHub corpus to recover lost animations and alpha.

### Phase 7 — Cleanup

25. Delete `Koan.Media.Core.Extensions.StreamTransformExtensions` and `TransformResult`.
26. Remove operator classes (`ResizeOperator`, `RotateOperator`, `TypeConverterOperator`) — replaced by stage-keyed recipe steps.
27. Update `docs/reference/media.md` with the new grammar.

---

## Alternatives considered

### Alternative 1 — Patch DX-0047 in place

Add an `IImageEncoder` selector to `StreamTransformExtensions` so each method preserves format. Keeps the per-step encode amplification, keeps the fragmented backend/HTTP/SPA vocabularies, keeps multi-variant cost. Fixes only the format-preservation bug. **Rejected**: leaves the structural defects (cost amplification, vocabulary drift) untouched and forecloses the smart-feature capabilities (overlays, bg=blur, content-addressable URLs).

### Alternative 2 — Outsource to imgproxy / Cloudflare Images / Imgix

Drop the pipeline entirely; proxy media requests to a managed image service. **Rejected**: introduces a third-party dependency for a workload Koan can handle natively in-process; loses the recipe-as-data introspection benefits; complicates on-premises deploys; transfers cost from CPU to network and per-request fees.

### Alternative 3 — Switch to Magick.NET

Replace ImageSharp with ImageMagick bindings (richer format support, native HEIC, AVIF encoder today). **Rejected**: native dependencies (libMagick) complicate cross-platform deploys and container images; ImageSharp's pure-managed surface is a load-bearing Koan property. We can revisit Magick.NET if and when ImageSharp's AVIF gap becomes blocking.

### Alternative 4 — Defer overlays to a future ADR

Ship recipe pipeline + HTTP grammar first; revisit overlays in v2. **Rejected**: overlays slot into the existing stage 7 with no architectural surprise; deferring them just postpones a feature whose shape is already settled. The cost of building the surface a second time exceeds the cost of building it once now.

### Alternative 5 — Code-only recipes (no appsettings)

Drop the `Koan:Media:Recipes` config surface; recipes live exclusively in code. **Rejected**: ops surface is too valuable. Stakeholder feedback was explicit that the config surface should exist in parallel with code. The collision policy (config wins, single log line) makes the precedence model clear.

---

## Open follow-ups (post-merge)

1. **AVIF encoder.** Track ImageSharp's encoder roadmap; add `AvifEncoder` to the `IImageEncoder` selector when available.
2. **Saliency-based focus detection.** v2 feature. Likely a small ML model + admin UI surface; current manual focus point delivers the bulk of value.
3. **`Koan.Media.Video`.** Sibling module providing `VideoPipeline : IMediaPipeline` over an FFmpeg backend, video-specific step records (`TrimStep`, `SpeedStep`, `MuteAudioStep`, `ConcatStep`, etc.), `VideoEncoderSelector` for H.264 / H.265 / VP9 / AV1 / AAC / Opus, and `Stream.AsVideo()` entry point. The recipe model, URL grammar, MediaController, and registry surfaces in this ADR are content-neutral — they apply unchanged to video; the engine layer is what specialises. Reserved `PipelineStage.Timeline` and `PipelineStage.Audio` slots are already in the canonical stage enum so the module can ship as a pure additive package without enum reshuffling. Out of scope for this ADR's deliverables; revisit if and when video-class assets become catalog citizens.
4. **`bg=blur` performance pass.** Profile on hot paths; consider GPU-accelerated path via SkiaSharp's GPU surface if CPU-bound on production workloads.
5. **Async pipeline for slow recipes.** Background-job mode for recipes whose render time exceeds an interactive budget (`bg=dominant` with k-means on huge sources). v2 if hit.

---

## References

- [MEDIA-0001](MEDIA-0001-media-pillar-baseline-and-storage-integration.md) — Media pillar baseline and storage integration
- [MEDIA-0002](MEDIA-0002-s6-social-creator-and-htmx-ui.md) — S6 Social Creator sample
- [MEDIA-0003](MEDIA-0003-media-variant-routing-and-transforms.md) — Variant routing, canonical signature, policy model (extended by this ADR)
- [DX-0047](DX-0047-fluent-media-transform-api.md) — Fluent Media Transformation Pipeline API (superseded by this ADR for encoding policy)
- ImageSharp documentation: <https://docs.sixlabors.com/articles/imagesharp/>
- CSS `object-fit` and `object-position` specifications (W3C CSS Images Module Level 3)
- BlurHash algorithm: <https://blurha.sh/>
- WebP container specification (RIFF chunks, ANIM/ANMF for animation detection)
