# MEDIA-0006: SVG decoder, Skia rasterizer, and strict XML validator

**Status:** Accepted
**Date:** 2026-05-31
**Decision Makers:** Koan architecture group, Media pillar leads
**Affected Components:** Koan.Media.Abstractions, Koan.Media.Core, Koan.Media.Web, consuming applications (Downstream consumer and others)
**Extends:** MEDIA-0005 (Kind-aware pipeline, Sample primitive, and data-driven encoder admission)
**Related:** MEDIA-0004 (Recipe-based media pipeline), MEDIA-0001 (Media pillar baseline)

---

| **Contract**         | **Details**                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| -------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **Inputs**           | Raw SVG bytes (from upload, fetch, or any byte-source the existing `AsMedia` extension accepts), an optional MEDIA-0004 recipe context, and a per-pipeline ingest cap (`MediaPipelineLimits.MaxSourceBytes`, default 5 MiB for SVG).                                                                                                                                                                                                                                                                       |
| **Outputs**          | When a recipe is applied: rasterized PNG bytes at the planner's forward-derived target dimensions (per MEDIA-0005 §4), then handed to the existing ImageSharp encode chain to produce the recipe's terminal format (WebP/JPEG/PNG/etc.). When no recipe is applied: the raw SVG bytes themselves, served with `Content-Type: image/svg+xml`. A `MediaInfo { Format = "svg", Kind = Vector, Width/Height from viewBox }` for probe responses regardless of path.                                            |
| **Error Modes**      | `ValidationFailed` (XML allowlist / DTD / depth violation — terminal, never stored); `ParseFailed` (well-formedness or unrecoverable structural error from XmlReader); `RasterizeFailed` (Svg.Skia or SkiaSharp produced no picture, or surface allocation failed); `OversizedInput` (source bytes exceed `MaxSourceBytes` before parsing begins). All four are surfaced at ingest time and are terminal — no partial blob is ever written.                                                                |
| **Success Criteria** | One pipeline handles SVG as a first-class Vector source with no caller branching. The raw SVG is the source-of-truth blob; rasterization only happens when a recipe demands a Raster target. Strict XML validation rejects scripted, animated, XXE, billion-laughs, and external-reference SVGs before any decoder touches the bytes. The Svg.Skia/SkiaSharp dependency is contained to `Koan.Media.Core`. Existing non-SVG callers see zero behavior change. Adding PDF or other future Vector kinds reuses the same shape. |

---

## Context

MEDIA-0005 closed the recipe-vs-source-kind gap by introducing the `Vector` kind, the `Sample` primitive, encoder `Accepts` as data, and a pure-function planner that forward-derives an implicit `Rasterize` step at the encoder boundary. The planner is property-tested against synthetic Vector sources. What MEDIA-0005 deliberately did *not* ship was the first concrete Vector producer: there is no decoder in `Koan.Media.Core` that detects SVG, returns `MediaKind.Vector`, or holds the raw bytes for the planner's deferred rasterization step. The Downstream consumer corpus — package emblems, mod logos, and the SVG assets the upstream catalogs publish — still fails to ingest.

The gap is narrow but load-bearing:

1. **No SVG header detection.** `MediaPipeline.From(Stream)` resolves through `Image.LoadAsync`, which throws on SVG. There is no seam to declare "this stream is SVG, build a Vector `MediaInfo`."
2. **No Vector executor.** The MEDIA-0005 planner emits a `PlannedStep` with `InputKind = Vector, OutputKind = Raster, Implicit = true` carrying `targetWidth`/`targetHeight` in `ResolvedParams`. The executor in `MediaPipeline.EncodeAsync` has no case for it — the `plan` parameter is only consulted for `KindTrace` diagnostics today.
3. **No validation boundary.** SVG is XML, and XML is a class of injection. `AdminArticleMediaController` already blanket-rejects SVG (lines 45–56) precisely because the framework has no validator. `FetchPreviewImageJob` currently accepts SVG bytes via `UrlContentCache` without any content inspection. Both surfaces need the same validator.
4. **No storage contract for Vector.** MEDIA-0005 leaves "what bytes hit the blob store for a Vector source" undefined. The user-level principle — *"if a consumer has no recipe to apply, dump the capture verbatim"* — means the source-of-truth blob is the raw SVG, not a pre-rasterized PNG.

This ADR fills all four. The Skia rasterizer (`SkiaSharp 3.119.4` + `Svg.Skia 3.0.6`, both MIT) is the chosen renderer because it is the only mature managed binding that handles the SVG 1.1 + SVG 2 static subset with gradients, masks, filters, and embedded raster references — the feature set the catalog corpus actually uses. The native binary cost (~73 MB Win32, ~53 MB Linux, ~7 MB macOS) is taken honestly: SVG support is worth a per-platform native payload, but the dependency stays inside `Koan.Media.Core` and is loaded only when an SVG actually appears.

The "raw bytes as source of truth" stance is the same one the storage and capture layers have taken since MEDIA-0001: the framework never lossy-transcodes a capture at ingest. Rasterizing SVG-at-ingest would discard the vector and force every downstream recipe to operate on whatever resolution we guessed. Forward-derivation from the planner's most-recent sizing step is the right resolution, and it is computed per-recipe, per-variant, every time.

---

## Decision

Add four narrow, cooperating components to `Koan.Media.Core`, plus one storage-policy rule that lives in the consuming jobs (not the framework). All four sit behind the existing `AsMedia` extension and the existing `MediaPipeline` surface — no public API changes for non-SVG callers.

### 1. `SvgFormat` — header sniff + viewBox extraction

`SvgFormat` is the SVG detector. It is invoked before `Image.LoadAsync` in `MediaPipeline.From(Stream)`. If the stream is SVG, the SVG branch takes over; otherwise the existing ImageSharp path runs unchanged.

Detection cannot rely on `Content-Type` (uploads, fetches, and cached blobs all lie or omit it). It cannot rely on filename extension (`UrlContentCache` strips it). It must inspect bytes.

```text
SvgFormat.Detect(headerBytes: ReadOnlySpan<byte>) -> bool
  // Read up to 1024 bytes from the stream (rewind after).
  // Decode as UTF-8 with replacement; lowercase.
  // Accept if any of:
  //   - starts with "<?xml" and the first <svg appears within the prefix
  //   - starts (after whitespace) with "<svg"
  //   - starts with UTF-8 BOM followed by either of the above
  // Reject if:
  //   - starts with binary magic (PNG/JPEG/WebP/GIF/RIFF/etc.)
  //   - first non-whitespace char is not "<"
```

The `Probe` path constructs the `MediaInfo` from the SVG document head — specifically the `viewBox` attribute on the root `<svg>` element, falling back to `width`/`height` attributes in that order. The viewBox is parsed as four numbers; the dimensions are the third and fourth (width/height). If neither viewBox nor width/height resolves, the document is malformed and `ParseFailed` is raised. The Skia path is not invoked at probe time.

```text
SvgFormat.Probe(stream) -> MediaInfo
  validate stream via SvgValidator (see §2)         // also caps + structure
  parse root <svg> attributes via XmlReader (no full DOM)
  if viewBox present and parsable -> dims := (vb.W, vb.H)
  else if width/height present     -> dims := (w, h)
  else                              -> throw ParseFailed
  return MediaInfo {
    Format       = "svg",
    Kind         = Vector,
    Width        = dims.W,
    Height       = dims.H,
    FrameCount   = 1,
    HasAlpha     = true,        // SVG is always alpha-capable
    ColorDepth   = 32,
    ExifOrientation = null,
    HasIccProfile   = false
  }
```

The raw bytes accompany the `MediaInfo` through the pipeline; the planner-derived rasterize step (when it fires) will read them. Nothing about probe touches Skia.

### 2. `SvgValidator` — strict allowlist, terminal-on-failure

`SvgValidator` is the security boundary. It is the first thing the SVG branch does, before viewBox extraction, before any decoder. Failure is terminal: the bytes are never stored, never cached, and never returned in any form to a downstream consumer. The validator does not "clean" the SVG — it rejects.

The validator uses `XmlReader` configured for hard refusal of DTD, entities, and external resolution. The security survey enumerated the threat classes; the configuration below is the direct map:

```text
XmlReaderSettings:
  DtdProcessing            = Prohibit       // blocks <!DOCTYPE> entirely
  XmlResolver              = null           // no external entity / schema fetch
  MaxCharactersInDocument  = 1_000_000      // entity-expansion budget cap
  Async                    = true
  IgnoreWhitespace         = true
  IgnoreComments           = true
```

Three caps gate ingest before any character of XML is parsed:

```text
MaxSourceBytes   = 5 * 1024 * 1024     // 5 MiB ingest cap for Format = "svg"
MaxNestingDepth  = 32                  // tracked manually during walk
MaxBlurStdDev    = 10                  // feGaussianBlur cap
```

The element allowlist (everything else is rejected) covers structural, shape, fill, mask, filter, text, and reference elements typical of catalog SVG content:

```text
Allowed elements:
  Structural: svg, g, defs, symbol, use, marker
  Shapes:     path, circle, rect, line, polyline, polygon, ellipse
  Text:       text, tspan, title, desc
  Paint:      linearGradient, radialGradient, stop, pattern
  Mask/Clip:  mask, clipPath
  Filter:     filter,
              feGaussianBlur, feOffset, feMerge, feMergeNode, feComposite,
              feFlood, feColorMatrix, feBlend, feConvolveMatrix
  Images:     image      (href: data: URIs only — no http/https/file/ftp)

Forbidden elements (immediate ValidationFailed):
  script, foreignObject, iframe, object, embed, applet,
  meta, link, style, a, switch, metadata,
  animate, animateMotion, animateTransform, set,
  feTurbulence, feDisplacementMap
```

The attribute allowlist is enforced per-element. Global rules apply to every element:

```text
Global per-attribute rules (any violation -> ValidationFailed):
  - any attribute whose local name starts with "on" (event handlers)
  - href or xlink:href whose value is not "#fragment" or "data:..."
    (exception: <image> may use data: URIs; nothing else may)
  - style attribute containing "url(", "expression(", "@import", or "javascript:"
  - data-* attributes (none are needed for rendering)
  - xmlns: redeclarations targeting the XHTML, XLink-non-svg, or null namespaces
```

Per-element attribute lists follow the security survey: each shape, text, gradient, mask, filter, and reference element has a fixed set of geometry, fill, stroke, transform, and identity attributes. Attributes outside that set on an allowed element are rejected. Numeric attributes are bounded: no `Infinity`, no `NaN`, no negative dimensions where the SVG spec disallows them, and `feGaussianBlur/@stdDeviation` is capped at `MaxBlurStdDev`.

The validator walks the document once, in streaming fashion, with a depth counter:

```text
SvgValidator.ValidateAsync(stream, ct) -> void   // throws on failure
  if stream.Length > MaxSourceBytes -> throw OversizedInput
  fast-prefix scan (first 1024 bytes): reject if any of
      "<!doctype", "<!entity", "<script", " system ", " public "
  reset stream
  using reader = XmlReader.Create(stream, settings)
  depth := 0
  while await reader.ReadAsync():
    if Element:
      depth += 1
      if depth > MaxNestingDepth -> throw ValidationFailed
      if localName in Forbidden  -> throw ValidationFailed
      if localName not in Allowed -> throw ValidationFailed
      foreach attribute:
        apply global rules and per-element allowlist
    if EndElement:
      depth -= 1
  // reaching end = pass; caller may rewind and re-read for rasterization
```

The validator is a pure function over bytes; it has no I/O, no logger dependency, no cache. It is the same code path on every surface that ingests SVG.

### 3. `SvgRasterizer` — Svg.Skia adapter, planner-driven target

`SvgRasterizer` is the implementation of the MEDIA-0005 implicit `Rasterize` step for `MediaKind.Vector`. It takes validated SVG bytes plus the planner-resolved `targetWidth`/`targetHeight` and produces PNG bytes. It does nothing else. It has no opinion on the final encoded format — the ImageSharp pipeline still handles WebP/JPEG/etc. after rasterization.

```text
SvgRasterizer.Rasterize(svgBytes: ReadOnlyMemory<byte>, target: (w, h)) -> byte[]
  using svg = new SKSvg()
  using sourceStream = new ReadOnlyMemoryStream(svgBytes)
  picture := svg.Load(sourceStream)
  if picture is null -> throw RasterizeFailed
  vb := svg.ViewBox       // SKRect; falls back to picture.CullRect if absent
  using surface = SKSurface.Create(new SKImageInfo(target.w, target.h))
  if surface is null -> throw RasterizeFailed
  canvas := surface.Canvas
  canvas.Clear(SKColors.Transparent)
  scale := min(target.w / vb.Width, target.h / vb.Height)
  // center the rendered viewBox in the target surface (letterbox if AR differs)
  dx := (target.w - vb.Width  * scale) / 2
  dy := (target.h - vb.Height * scale) / 2
  canvas.Translate(dx, dy)
  canvas.Scale(scale)
  canvas.DrawPicture(picture)
  using image = surface.Snapshot()
  using data  = image.Encode(SKEncodedImageFormat.Png, 100)
  return data.ToArray()
```

The rasterizer renders to PNG, not directly to the recipe's terminal format, for two reasons: (a) PNG is lossless so the subsequent ImageSharp encode pass has full color fidelity, and (b) it keeps SkiaSharp's encoder surface to the single format we have validated empirically against the corpus. WebP/JPEG/AVIF re-encoding is handled by ImageSharp, which is already tuned and tested for those formats in MEDIA-0004.

The aspect-ratio policy is **letterbox-into-transparent**, not stretch. SVG viewBoxes commonly do not match recipe target aspect ratios (a 1:1 logo into a 600×750 card). Stretching distorts the asset; cropping discards intent; transparent letterbox preserves both and composes correctly under the existing overlay pipeline.

### 4. Pipeline integration — where the SVG branch lives

`MediaPipeline.From(Stream)` gains a single pre-decode branch:

```text
MediaPipeline.From(stream, logger, overlayResolver, fonts, limits) -> IMediaPipeline
  headerBuffer := peek up to 1024 bytes from stream
  if SvgFormat.Detect(headerBuffer):
    rewind stream
    return new SvgMediaPipeline(stream, logger, fonts, limits)
  rewind stream
  return new MediaPipeline(stream, logger, overlayResolver, fonts, limits)  // existing
```

`SvgMediaPipeline` implements `IMediaPipeline` with the same public shape (`ProbeAsync`, `ToBytesAsync`, `MaterializeAsync`). Internally:

```text
SvgMediaPipeline.ProbeAsync(ct):
  await SvgValidator.ValidateAsync(stream, ct)        // terminal on failure
  return SvgFormat.Probe(stream)

SvgMediaPipeline.ToBytesAsync(ct):
  if no steps configured:
    return RawSvgBytes(stream)                        // dump-capture path
  await SvgValidator.ValidateAsync(stream, ct)
  probe := SvgFormat.Probe(stream)
  plan  := MediaPipelinePlanner.Plan(probe, _steps, terminalEncoderAccepts)
  if plan is PlanError -> throw
  // planner forward-derived (targetWidth, targetHeight) from the most-recent
  // sizing step; lives in plan.Steps.Last(implicit Rasterize).ResolvedParams
  target := plan.ImplicitRasterizeTarget()
  pngBytes := SvgRasterizer.Rasterize(svgBytes, target)
  // hand off to the existing ImageSharp pipeline for the recipe's terminal encode
  return await MediaPipeline.From(pngBytes, logger, overlayResolver, fonts, limits)
                            .WithSteps(_steps.WithoutImplicitRasterize())
                            .ToBytesAsync(ct)
```

The handoff is the key move: SVG-specific code ends at PNG bytes. Everything from `Resize → Encode` (and any overlays, color adjustments, format negotiation) runs on the existing, well-tested ImageSharp path. There is one Vector executor and one Raster executor, joined at the rasterizer.

`MaterializeAsync` (multi-variant) rasterizes **once per distinct target dimension** across all variants, not once per variant. Two variants requesting 600×750 share a rasterization; a third requesting 1200×1500 triggers a second one. The dedupe key is the resolved `(width, height)` tuple, scoped to the request.

### 5. Storage policy — raw bytes are the source of truth

The framework does not rasterize at ingest. Ever. The consuming surfaces follow:

- **`AdminArticleMediaController`** lifts its blanket SVG block once `SvgValidator` is wired into its upload path. On upload, the controller validates the bytes; if valid, it stores the raw SVG verbatim and records `MediaKind = Vector` on the asset record. Validation failures are returned as `400 Bad Request` with the violating element/attribute named.
- **`FetchPreviewImageJob`** gains an SVG bypass: after `UrlContentCache` returns bytes, if `SvgFormat.Detect` matches, it runs `SvgValidator` and stores the raw bytes as the preview blob with `Content-Type: image/svg+xml`. No pipeline execution. No rasterization. Failure to validate skips the asset (logged at `Warning`) — the catalog item simply has no preview, which is the same outcome as a 404 from the upstream.
- **Recipe-driven serving (`MediaController` variants)** triggers the rasterize path the first time a recipe is applied to the asset. The rasterized output is cached per (asset, recipe fingerprint) by the existing MEDIA-0004 cache layer; the SVG source bytes are never replaced.

This is the user-level "dump capture if no recipe" rule, restated as code: the source-of-truth bytes flow through the system unchanged until a recipe explicitly asks for a Raster derivation.

---

## Migration

This is purely additive. No public API changes. No recipe shape changes. No cache key changes for existing assets.

- **Non-SVG callers are untouched.** `MediaPipeline.From(Stream)` on a JPEG, PNG, GIF, or WebP byte source skips the SVG branch on the header check and runs the existing path.
- **Existing recipes work for SVG without modification.** `PackageCard`, `ArticleHero`, etc. already declare a sizing step (`Resize(600, 750)` and friends); the MEDIA-0005 planner forward-derives the rasterize target from them. The author writes nothing new.
- **`AdminArticleMediaController` deletes its SVG block.** The blanket `415 Unsupported Media Type` for `image/svg+xml` becomes a `SvgValidator.ValidateAsync` call followed by the normal upload flow.
- **`FetchPreviewImageJob` gains the validator + raw-store bypass.** A small change at the post-cache hook: header-sniff, validate, raw-store with the correct content type. No change to job semantics, retries, or scheduling.
- **The `Koan.Media.Core` package gains `SkiaSharp 3.119.4` and `Svg.Skia 3.0.6` as runtime dependencies.** Both MIT-licensed. Platform-specific natives are pulled by the platform RID at publish time; the managed assembly weight is ~8.3 MiB. Consumers that never ingest SVG still pay the binary cost but not the load cost — Skia natives are not loaded until the first SVG flows through the pipeline.

There is no data migration. There is no marker bump. There is no backfill. Greenfield Vector-capable assets show up the moment the next SVG ingest lands.

---

## What we explicitly DON'T do

The following are out of scope and will not be addressed in this ADR. Listing them so future contributors don't relitigate:

- **Animated SVG (SMIL / CSS animations).** `animate`, `animateMotion`, `animateTransform`, and `set` are on the forbidden-elements list. We render vector-static only. Timeline-style SVG belongs under the `Timeline` kind whenever that decoder lands; we will not coerce animated SVG into `AnimatedRaster`.
- **SVG output encoder.** There is no Raster-to-SVG path. We never produce SVG. `EncoderRegistry` has no `SvgEncoder` registration and there is no plan to add one.
- **JavaScript-enabled SVG.** `<script>` is on the forbidden list. There is no opt-in. There is no sandbox mode. The validator is a hard gate, not a policy switch.
- **Streaming SVG rasterization.** SVG is parsed memory-resident. The 5 MiB cap is the entire budget. Streaming a 50 MiB SVG is not a use case the catalog has, and the security model (single-pass validator before any decoder) requires the bytes to be available.
- **SVG-as-overlay.** Overlay layers stay raster. The overlay engine accepts pre-rendered PNG/WebP overlays only; SVG overlays would require running the rasterizer inside the overlay compositor, which is not worth the rendering-context complexity for a single-digit number of overlay assets.
- **Font fallback / system-font resolution beyond what `KoanFontRegistry` already exposes.** `Svg.Skia` honors the system font fallback chain; we do not add a Koan-managed font substitution layer for SVG text.

---

## Consequences

### Positive

- **Vector quality is preserved for the no-recipe path.** SVG served directly is served as SVG. Browsers render at their native DPI; the asset scales losslessly with the page.
- **One recipe handles SVG the same as JPEG/GIF/PNG.** The MEDIA-0005 planner's forward-derivation does the work; the recipe author writes nothing new. The Downstream consumer `PackageCard` recipe gains SVG support the moment this ADR lands.
- **Future Vector kinds slot in.** PDF (via PdfPig + Skia) and EPS (via Ghostscript bindings) reuse the same shape: detector + validator + rasterizer adapter + planner-driven target. Nothing in `MediaPipeline.From` needs to change again to admit a new Vector kind.
- **The security boundary is one validator.** `AdminArticleMediaController`, `FetchPreviewImageJob`, and any future SVG-accepting surface share `SvgValidator.ValidateAsync`. The allowlist is one list, in one file, with one test suite.

### Negative

- **~12 MB of SkiaSharp native binaries per platform RID** (Win32 ~73 MiB, Linux ~53 MiB, macOS ~7 MiB on the 3.119.x line). Container images grow. Cold-start memory is unaffected — natives are not loaded until the first SVG.
- **A new validator to maintain.** The allowlist is a security artifact; it must be revisited when SVG 2 features stabilize, when new CVE classes emerge in `SkiaSharp`, or when the corpus introduces a previously-unseen element type. The test suite is the contract.
- **Two decoder paths at runtime.** `MediaPipeline.From(Stream)` now has a pre-decode branch. The branch is one header sniff and a single delegation; the two paths reconverge at PNG bytes. The conceptual cost is real but bounded.

### Neutral

- The Svg.Skia + SkiaSharp choice is reversible at the rasterizer-adapter boundary. If a managed-only SVG renderer becomes viable, `SvgRasterizer` is the only file that changes.

---

## Out of scope (deferred to follow-up ADRs)

The following are explicit non-goals for this ADR. Each warrants its own decision document:

- **Cache-as-storage unification (MEDIA-0007).** Whether rasterized SVG outputs share the same content-addressed store as Koan.Storage blobs, or stay in the MEDIA-0004 variant cache, is a cross-pillar decision.
- **Streaming encoders (MEDIA-0008).** The SVG rasterizer is memory-resident and produces a full PNG byte[]; aligning with a future streaming-encoder substrate is a separate effort.
- **Format negotiation contract (MEDIA-0009).** Smart `Accept`-header negotiation between SVG (when supported) and rasterized derivatives is a routing concern handled by the Web pillar, not the planner.

---

## Test coverage requirements

- **`SvgValidator` allowlist suite.** For every allowed element, at least one positive test (well-formed instance passes) and one negative test (the same element with a forbidden attribute fails with the violating attribute named).
- **`SvgValidator` rejection suite.** A representative payload for each forbidden class: `<script>`, `<foreignObject>`, `<iframe>`, `<!DOCTYPE>` with internal subset, `<!ENTITY>` billion-laughs, on-attribute event handler, external `href`, external `xlink:href`, `style` with `url(`, `style` with `expression(`, oversize input (>5 MiB), nesting beyond 32 levels, `feGaussianBlur` with `stdDeviation > 10`.
- **`SvgRasterizer` target-size correctness.** For target dims `(600, 750)`, `(1200, 1500)`, `(64, 64)`: assert the output PNG decodes back to the requested dimensions, that the alpha channel is preserved, and that the letterbox transparency matches the aspect-ratio difference.
- **Pipeline integration via `VectorBridge`.** End-to-end test: synthetic SVG → `Resize(600, 750)` → `Encode("webp")`. Assert the planner inserts the implicit Rasterize at the encoder boundary (MEDIA-0005 contract, already covered there), assert `SvgRasterizer` is invoked exactly once with `(600, 750)`, assert the final bytes are valid WebP at 600×750.
- **End-to-end Vector → Raster → Encode for the three common recipes.** `PackageCard` (WebP, 600×750), `ArticleHero` (WebP, 1200×630), `AdminPreview` (PNG, 256×256). Each test starts from a real SVG fixture from the Downstream consumer corpus and asserts the rendered output matches a recorded checksum within an LPIPS tolerance.
- **Storage policy enforcement.** `FetchPreviewImageJob` test: valid SVG fetched → raw bytes in blob store, `Content-Type: image/svg+xml`, no pipeline execution recorded. Invalid SVG fetched → no blob written, job logs `Warning` with the validator's violation message.
- **No-rasterize path for no-recipe serving.** `MediaController` test: GET an SVG asset with no recipe applied → response body is byte-identical to the stored bytes, `Content-Type: image/svg+xml`.

### Registration in toc.yml

Append `MEDIA-0006-svg-decoder-and-skia-rasterizer.md` to the Media section of `docs/decisions/toc.yml` after draft acceptance.
