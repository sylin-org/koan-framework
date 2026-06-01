---
id: MEDIA-0005
slug: kind-aware-pipeline-and-sample-primitive
domain: Media
status: Accepted
date: 2026-05-31
title: Kind-aware pipeline, Sample primitive, and data-driven encoder admission
---

**Status:** Accepted
**Date:** 2026-05-31
**Decision Makers:** Koan architecture group, Media pillar leads
**Affected Components:** Koan.Media.Abstractions, Koan.Media.Core, Koan.Media.Web, consuming applications (Gposingway and others)
**Extends:** MEDIA-0004 (Recipe-based media pipeline, format-preserving transforms, and overlay composition)
**Related:** MEDIA-0003 (Media variant routing and transforms), MEDIA-0001 (Media pillar baseline)

---

| **Contract**         | **Details**                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| -------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **Inputs**           | Source media bytes (any decoder-registered kind: Raster, AnimatedRaster, Vector, Timeline), a MEDIA-0004 recipe, the encoder registry with declared `Accepts` sets, and a target encoder selected from the recipe's terminal `Encode`/`FlattenTo` step.                                                                                                                                                                                                                                                                                                                                                                |
| **Outputs**          | A validated, kind-coherent execution plan; rendered media bytes whose kind is compatible with the chosen encoder; a structured `kindTrace` diagnostic that records the kind in and out of every step plus any implicit `Rasterize` insertions.                                                                                                                                                                                                                                                                                                                                                                         |
| **Error Modes**      | `KindMismatch` (step `i` cannot accept current kind) with suggested `Sample.First` insertion; `EncoderRefused` (terminal encoder's `Accepts` does not include the plan's final kind) with the same suggestion; `UnknownSelector` for unsupported `Sample` selectors on a given kind; `RasterizeRequiredButNoSizing` when a Vector reaches an encoder boundary with no upstream sizing step to derive extents from.                                                                                                                                                                                                      |
| **Success Criteria** | One recipe handles all source kinds without source-kind branching. `Sample` is a no-op on `Raster` (parity with MEDIA-0004 behavior). `ExtractFrame(n)` continues to compile and behave identically. The planner is a pure function over `(recipe, sourceKind, encoderRegistry)` — property-testable, never reorders author steps, only inserts an implicit `Rasterize` when a Vector reaches a non-Vector boundary. Adding a new encoder (AVIF, MP4, GIF re-encoder) is a one-line `Accepts` registration. Author intent is preserved: `crop-then-sample` and `sample-then-crop` remain distinct, executed in order. |

---

## Context and Problem Statement

MEDIA-0004 landed the recipe-based pipeline and fixed the format-destructive defects of DX-0047. In doing so it concretized the v1 execution model: a single `Image<TPixel>` (ImageSharp) flowing through a fixed stage ordering, with `ExtractFrameStep(0)` available as a defensive no-op for static rasters and a frame selector for animated rasters. That model is sufficient for the catalog the Media pillar shipped against. It does not generalize.

Six concrete failure modes surface as soon as the consuming surface broadens beyond ImageSharp-decodable raster bytes:

1. **Hard-coded ImageSharp decode path.** `MediaPipeline.From(Stream)` resolves through `Image.LoadAsync`. There is no seam to admit an SVG decoder, a video demuxer, or any non-raster source. Every step downstream is typed to `Image<TPixel>`. Adding a new source kind today means a parallel pipeline, not a new step.

2. **No SVG path at all.** The Gposingway corpus contains SVG logos and emblem assets that need to render into package cards alongside JPEG and WebP sources. There is no way to express "treat this SVG as a 600×750 raster at encode time" — there is only "fail to decode."

3. **`ExtractFrame(0)` no-op only generalizes on raster.** v1 leans on the fact that calling `ExtractFrameStep(0)` against a single-frame raster is harmless. That trick collapses to nothing for Vector (no frames to extract) and is wrong for Timeline (frame 0 of a video is decoded differently from frame 0 of an animated raster). The whole "package card from frame 0 of a GIF or WebP or AVI or static PNG" use case is exactly the case the framework cannot express in one recipe.

4. **Encoder-as-source-of-truth coupling.** v1's `EncoderSelector.For(sourceFormat, targetFormat, quality)` reads the source format and the requested target format, then picks an `IImageEncoder`. It has no opinion on what *kinds* of media each encoder accepts. JpegEncoder silently flattens an animated input; WebpEncoder accepts both but its acceptance is implicit in code, not declared as data. There is no place to register "AVIF accepts Raster only" or "MP4 accepts Timeline only" without adding a switch case.

5. **Animation policy implicit, not expressible.** Whether a recipe preserves animation, flattens to a still, or rasterizes a vector is decided by which other step the author happened to include (or omit). There is no per-recipe declaration of intent that the planner can validate against.

6. **Recipes cannot capture "frame 0 of any animated kind."** Writing the package-card recipe today requires either branching on source kind in the caller (defeating the recipe abstraction) or shipping N parallel recipes (`PackageCard_Static`, `PackageCard_Animated`, `PackageCard_Vector`). Both options push pipeline knowledge into application code, which is the exact debt MEDIA-0004 was supposed to retire.

The Gposingway maintainer's load-bearing example — a single `PackageCard` recipe that produces a 600×750 WebP from a JPEG, an animated GIF, a static PNG, an SVG logo, or (eventually) the first frame of an AVI — is unreachable in v1 without per-source branching at the call site. That is the gap this ADR closes.

---

## Decision Drivers

1. **One recipe, many source kinds.** The framework — not the caller — discriminates kinds. The author writes intent ("first frame, sized 600×750, encoded WebP"); the planner makes it valid.
2. **Addressable collapse point.** The step that says "I want a still from whatever this is" must be a named, greppable primitive. Today the closest thing is `ExtractFrame(0)`, which both lies (it isn't extracting from a still raster) and doesn't generalize.
3. **Encoder admission is data.** Encoder capability must be declared, not deduced. Adding AVIF or video output is registration, not surgery.
4. **Fail loud at plan time.** Kind mismatches must surface during planning, not during the encode pass. The planner is a pure function; mismatches are diagnostics, not exceptions in the middle of a stream.
5. **Author intent preserved.** The planner does not reorder. `crop-then-sample` and `sample-then-crop` are different recipes and execute as written.
6. **No lenient mode.** The strict encoder gate is the only mode. There is no "best-effort fallback to JPEG" branch. v1's silent flattening is the bug class this ADR exists to delete.

---

## Decision

Introduce three additions to Koan.Media on top of the MEDIA-0004 substrate:

1. An explicit **`Sample` step** — kind-agnostic, selector-discriminated, no-op on `Raster`.
2. A plan-time **kind tracker / planner** — pure function, threads `currentKind`, inserts implicit `Rasterize` at encoder boundaries, never reorders.
3. **Encoder `Accepts` as data** — declared on each encoder, validated by the planner, surfaced as diagnostics.

### 1. Kind taxonomy

Four kinds, declared as a sealed discriminated set in `Koan.Media.Abstractions`:

```text
MediaKind ::=
  | Raster           // single frame, width × height, pixel buffer
  | AnimatedRaster   // N frames with per-frame timing, dimensions, loop count
  | Vector           // device-independent extents, no pixel grid yet
  | Timeline         // time-indexed video, duration, framerate, audio track (opaque)
```

A decoder declares the kind it produces. The planner reads it. No step in the pipeline asks "what format was this?" — that question is answered once at decode time and never re-litigated.

### 2. The `Sample` step

`Sample` is the named collapse point from any animated or time-indexed kind into a single `Raster`. It is the primitive that lets one recipe handle all source kinds.

```text
SampleStep {
  Stage    = PipelineStage.Frame    // shares the v1 Frame slot
  Selector = SampleSelector
  Name?    : string
  Primary  : bool
}

SampleSelector ::=
  | First                  // first available frame / earliest time / canonical pose
  | Frame(index: int)      // explicit frame index; AnimatedRaster only
  | At(time: TimeSpan)     // time offset; Timeline only
  | Thumbnail              // decoder-provided thumbnail if present, else First
```

Behavior per source kind, applied by the planner and executed by the engine:

```text
plan(Sample, currentKind) =
  match currentKind with
  | Raster          -> no-op,             produces Raster
  | AnimatedRaster  -> apply selector,    produces Raster
  | Vector          -> no-op at plan time, defer to Rasterize at encoder boundary
  | Timeline        -> apply selector,    produces Raster
```

`Sample` on `Raster` is the move that lets one recipe handle every kind. The recipe author writes `Sample.First` once; the planner makes it a no-op when it doesn't need to do anything, a frame extraction when it does, and a deferred rasterization when the kind is Vector.

`ExtractFrame(n)` survives as `[Obsolete] alias` for `Sample.Frame(n)` — see Migration.

### 3. Encoder `Accepts` as data

Each encoder declares its accepted kinds as a set, alongside its existing format slug and content type:

```text
EncoderRegistration {
  FormatSlug   : string       // "webp", "jpeg", "avif", "mp4", ...
  ContentType  : string
  Accepts      : Set<MediaKind>
  Build        : (quality, options) -> IEncoder
}

// Registrations:
JpegEncoder  : Accepts = { Raster }
PngEncoder   : Accepts = { Raster }
WebpEncoder  : Accepts = { Raster, AnimatedRaster }
GifEncoder   : Accepts = { Raster, AnimatedRaster }
AvifEncoder  : Accepts = { Raster }                  // future
Mp4Encoder   : Accepts = { Timeline }                // future
```

Adding AVIF or any video encoder is a single registration line plus the codec binding. The planner reads `Accepts` directly; there is no second source of truth in switch statements.

### 4. The planner

The planner is the new heart of Koan.Media. It is a **pure function**:

```text
plan(recipe: MediaRecipe, sourceKind: MediaKind, encoders: EncoderRegistry)
    : Result<ExecutionPlan, PlanError>

ExecutionPlan {
  Steps      : ImmutableArray<PlannedStep>     // recipe.Steps + implicit insertions
  FinalKind  : MediaKind
  KindTrace  : ImmutableArray<KindTraceEntry>  // per-step kind in / kind out / implicit?
}

PlannedStep {
  Source       : MediaStep         // the author's step, or "implicit"
  KindIn       : MediaKind
  KindOut      : MediaKind
  Implicit     : bool
  Reason       : string?           // populated for implicit insertions
}
```

The planner's contract, in order:

```text
planner contract:
  1. currentKind := sourceKind
  2. for each step in recipe.Steps (IN AUTHOR ORDER, NEVER REORDERED):
       a. nextKind := step.transition(currentKind)
       b. if nextKind is Error -> return KindMismatch {
              stepIndex,
              expectedKinds,
              actualKind,
              suggestion: "insert Sample.First before step {i}"
          }
       c. if step is Sample and currentKind = Vector:
            defer Rasterize until encoder boundary; currentKind unchanged
       d. emit PlannedStep with KindIn = currentKind, KindOut = nextKind
       e. currentKind := nextKind
  3. resolve terminal encoder from recipe.Steps.OfType<EncodeStep|FlattenToStep>().Last()
  4. if currentKind not in encoder.Accepts:
       if currentKind = Vector:
         derive target extents from the previous sizing step (Resize/Shape)
         if no upstream sizing step -> return RasterizeRequiredButNoSizing
         insert implicit Rasterize(extents) before encode
         currentKind := Raster
       else:
         return EncoderRefused {
            encoder,
            actualKind: currentKind,
            accepted: encoder.Accepts,
            suggestion: "insert Sample.First before the encode step"
         }
  5. return ExecutionPlan { Steps, FinalKind = currentKind, KindTrace }
```

Forward-derivation of the Rasterize target is what closes the loop: when a `Vector` reaches an encoder boundary on a `preserve-animation` recipe that never declared a `Sample`, the planner reaches back to the most recent sizing step (`Resize`, `ResizeFit`, `ResizeCover`, `Shape`) and uses its target dimensions to rasterize the Vector before encoding. If no upstream sizing step exists, the recipe is rejected at plan time with `RasterizeRequiredButNoSizing` — there is no plausible default extent for a kind that has no native pixel grid.

Because the planner is a pure function over `(recipe, sourceKind, encoderRegistry)`, it is exhaustively property-testable: round-trip every kind through every recipe in the registry, assert that the resulting plan either validates or returns a typed error with a `stepIndex` and a `suggestion`.

### 5. Strict encoder gate

There is no lenient mode. A `KindMismatch` at plan time is a `400 Bad Request` at the HTTP surface and an exception at the in-process surface. The error payload always includes:

- the failing step index in the recipe,
- the kinds the step accepts,
- the kind the planner had at that point,
- the literal `Sample.First` insertion that would fix the recipe.

v1's silent flatten — the behavior that prompted MEDIA-0004 — is gone. The planner refuses to encode an `AnimatedRaster` into JPEG. The caller must say `Sample.First` (or `FlattenTo("jpeg")`, which is itself defined in terms of `Sample` — see Migration).

### 6. Author intent preservation

The planner never reorders. `crop-then-sample` (crop the animation, then take frame 0) and `sample-then-crop` (take frame 0, then crop the still) are different operations and the framework respects both. The planner only *inserts* (the implicit `Rasterize` at an encoder boundary on `Vector`), and only when no other interpretation is possible. Every insertion is recorded in `KindTrace` with `Implicit = true` and a human-readable `Reason`.

### 7. `kindTrace` diagnostic

The execution plan's `KindTrace` is surfaced as `X-Koan-Media-KindTrace` on rendered responses, alongside the existing `X-Koan-Media-*` headers from MEDIA-0004. Format:

```text
X-Koan-Media-KindTrace: Vector -> Resize[Vector] -> implicit Rasterize(600x750)[Raster] -> Encode/webp[Raster]
```

This is the same data structure the planner returns to in-process callers via `ProbeAsync`. There is one structured representation; the header is its serialization.

---

## Migration

This is an additive change for existing callers, with one deprecation:

- **`ExtractFrameStep(n)` becomes `[Obsolete("Use Sample.Frame(n)")]` alias for `SampleStep(SampleSelector.Frame(n))`.** The record type is preserved; the fluent builder method `ExtractFrame()` continues to compile; the canonical fingerprint encoding emits the `Sample` form so cache keys stabilize on the new vocabulary. Source compatibility is total; binary compatibility is preserved for the existing record shape.
- **`FlattenToStep(format, quality)`** is redefined as syntactic sugar for `Sample.First + EncodeStep(format, quality)` with the added semantic that any subsequent encoder is rejected. Existing call sites do not change. The planner expands `FlattenTo` to the two-step form during planning so the kind transition is visible in `KindTrace`.
- **`MaterializeAsync` and the single-decode multi-output substrate from MEDIA-0004 are untouched.** Multiple variants share one decode and one plan-time evaluation; each variant runs its own encoder pass. Kind tracking is per-variant since each variant may declare a different terminal encoder.
- **Existing recipes that do not encounter Vector or Timeline sources continue to behave identically.** The planner's behavior on a `Raster` source through a v1-shaped recipe is a no-op layer over the v1 execution path.

The Gposingway recipes (`PackageCard`, `ArticleHero`, `ArticleCard`, `AdminPreview`, `PackageSighting`) all keep their existing definitions. They gain Vector support automatically once the SVG decoder lands (next ADR) — the planner's forward-derived `Rasterize` insertion handles them with no recipe change.

---

## What we explicitly DON'T do

The following options were considered and rejected. Listing them here so future contributors don't relitigate:

- **No step reordering.** The planner only inserts (`Rasterize` at encoder boundary), never reorders. `crop-then-sample` and `sample-then-crop` are distinct.
- **No commute analysis.** The planner does not attempt to prove that two adjacent steps commute and reorder them for performance.
- **No `Branch(by: kind)` recipe DSL.** Source-kind branching at recipe authorship time defeats the entire point: the planner discriminates, not the author.
- **No `Promote` / `NoReorder` annotations.** Author intent is the default; there is no annotation language because there is no automatic reordering to opt out of.
- **No lenient encoder gate.** A `KindMismatch` is a plan-time error, full stop. There is no "best-effort fallback" mode and there will not be one.
- **No per-call kind override.** The caller cannot tell the planner "treat this as Raster" — the decoder declares the kind, and that declaration is the only source of truth.

---

## Consequences

### Positive

- **One recipe handles all source kinds.** The `PackageCard` recipe produces a 600×750 WebP from a JPEG, a GIF, a PNG, or (with the SVG decoder) an SVG. The application stops branching on source kind at the call site.
- **Addressable collapse point.** `Sample` is greppable, namable, and primary-flaggable in the MEDIA-0004 recipe model. Diagnostics name it; cache fingerprints encode it; future tooling (recipe linters, `/media/recipes` introspection) can reason about it.
- **Future-proof for video.** Adding an MP4 encoder is a single `EncoderRegistration { Accepts = { Timeline } }`. Adding a Timeline decoder is the same on the decode side. The planner already understands the kind.
- **Pure-function planner.** Property tests cover the planner exhaustively. Mismatches surface with `stepIndex` and a literal suggestion — actionable in CI logs and HTTP error bodies.
- **Encoder admission is data.** New formats (AVIF, JXL) ship as registrations, not as switch-statement amendments.
- **Author intent preserved.** No surprising reorderings. `crop-then-sample` ≠ `sample-then-crop` ≠ unintended optimization.

### Negative

- **~500 LoC of new framework.** Kind taxonomy + `Sample` step + planner + `Accepts` registry + plan-time error model. Concentrated in `Koan.Media.Abstractions` and `Koan.Media.Core`. Bounded and testable.
- **Three concepts to learn.** Recipe authors now reason about (kind, planner, encoder admission) in addition to MEDIA-0004's (stage, step, recipe). The docs surface grows; the mental model is genuinely larger.
- **v1 lenient behavior is gone.** The strict encoder gate refuses recipes that v1 would have silently flattened. Migration of existing callers that depended on the silent flatten requires adding an explicit `Sample.First` or `FlattenTo`. We consider this a feature, not a regression — the silent flatten is the bug class this ADR exists to delete.
- **Plan-time errors are surfaced earlier.** Recipe authors who previously got runtime-late JPEGs of animated sources will now get plan-time `KindMismatch` errors. This is the intended trade.

### Neutral

- Cache keys for recipes that contain `ExtractFrame(n)` shift to the canonical `Sample.Frame(n)` fingerprint on next deployment. Cold cache warm-up cost is bounded by the existing storage layer's deduplication.

---

## Out of scope (deferred to follow-up ADRs)

The following are explicit non-goals for this ADR. Each warrants its own decision document:

- **SVG decoder + Skia rasterizer.** This ADR builds the framework that admits a Vector kind. The SVG decoder itself, the Skia binding, and the rasterizer's options (DPI, font fallback, color profile handling) are MEDIA-0006.
- **Cache-as-storage unification.** Aligning Koan.Media's cache layer with Koan.Storage's content-addressed semantics is a separate cross-pillar decision.
- **Streaming encoders.** Encoder pipelines that emit bytes incrementally rather than producing a full `byte[]` are out of scope; the MEDIA-0004 buffered model is preserved.
- **Format negotiation contract.** Smart `Accept` header negotiation (serving WebP to Chrome and JPEG to legacy clients from the same recipe) is a routing concern handled by MEDIA-0003 + Web pillar, not a planner concern.
- **Full I/O streaming.** Source streams larger than memory require a separate decode-streaming substrate. Out of scope.

---

## Implementation phase plan

This ADR lands the **foundation**: kind taxonomy, `Sample` step, planner, `Accepts` registry, `KindMismatch` errors, `kindTrace` diagnostics, `ExtractFrame` → `Sample.Frame` deprecation alias, and `FlattenTo` redefinition in planner terms. All four kinds are declared; the planner handles all four; only Raster and AnimatedRaster have decoders today.

The **SVG decoder** — the concrete Vector producer — lands as a subsequent ADR (MEDIA-0006). The planner's Vector handling is implemented and property-tested in this pass using synthetic Vector sources in the test suite, so the codepath is exercised before MEDIA-0006 introduces the real producer.

The **Timeline decoder and video encoder** are explicitly out of scope; the kind is declared so the planner's switch is exhaustive, but no production code path produces or consumes Timeline media in this pass.

### Test coverage requirements

- Planner property tests: for every `(sourceKind, recipe)` pair in the registry, assert the plan either validates with a coherent `KindTrace` or returns a typed error with `stepIndex` and `suggestion`.
- `KindMismatch` error payload shape: `stepIndex`, `expectedKinds`, `actualKind`, and the literal `Sample.First` suggestion string are present and stable.
- `EncoderRefused` for every `(kind, encoder)` pair outside `Accepts`.
- `ExtractFrame(n)` source-compat: existing tests in `Koan.Media.Core.Tests` continue to pass with the alias in place.
- `FlattenTo` planner expansion: assert that the executed plan contains the implicit `Sample.First` and that `KindTrace` reports it.
- Vector forward-derivation: synthetic Vector source through `Resize(w, h) → Encode("webp")` produces an implicit `Rasterize(w, h)` and a valid plan.
- Vector without sizing: synthetic Vector source through a sizing-free recipe returns `RasterizeRequiredButNoSizing`.

### Registration in toc.yml

Append `MEDIA-0005-kind-aware-pipeline-and-sample-primitive.md` to the Media section of `docs/decisions/toc.yml` after draft acceptance.
