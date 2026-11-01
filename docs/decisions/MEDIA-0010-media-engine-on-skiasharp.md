---
id: MEDIA-0010
slug: media-engine-on-skiasharp
domain: Media
status: Accepted
date: 2026-10-31
title: The media engine runs on SkiaSharp
---

# MEDIA-0010: The media engine runs on SkiaSharp

**Status**: Accepted
**Date**: 2026-10-31

Replaces the engine underneath MEDIA-0004. Amends MEDIA-0004 (the pure-managed claim), MEDIA-0006 (the raster chain after rasterization), MEDIA-0008 (what "streaming" means at the encoder) and MEDIA-0009 (the producible set). `IMediaPipeline`, the recipes, the URL keys, the limits, the cache and the ETag formula are kept.

## Contract

| Aspect | Specification |
|---|---|
| **Inputs** | JPEG, PNG, WebP, GIF, BMP, ICO and WBMP bytes, seekable or not, and SVG through MEDIA-0006. |
| **Outputs** | JPEG, PNG and WebP; animation on WebP only. Exif, XMP, IPTC and format text never appear in an output. A color profile other than sRGB does, unless stripped. |
| **Error modes** | `MediaDecodeException` (422) for anything unreadable and for any decode that does not finish with `Success`; `MediaSourceLimitException` (400) from the header, or for output frames over `MaxOutputMegapixels`, before any pixel is allocated; a boot failure for a recipe pinning an unwritable format; a 404 for a shortcut seed naming one; `format=` naming one is reported as ignored, or a 400 in strict mode. |
| **Success criteria** | A Release build and a single-file publish succeed with no licence key. No project references the previous engine. Reductions match the previous default sampler (PSNR 93.0–100.1 through the old and new pipelines). The native library, and the shaper's, load on a chiseled image as its non-root user. |

## Context

Koan.Media.Core ran on SixLabors.ImageSharp, with SkiaSharp alongside it for SVG only. Clearing six advisories meant moving to ImageSharp 3.2.0, whose build-time licence check fails every non-Debug build that references it directly. Application builds and package publication stopped. The choice was a community licence key in every consuming build, or one engine.

## Decision

SkiaSharp 4.154.0 decodes, transforms, composites and encodes. Svg.Skia moves to 5.2.3, the line compiled against SkiaSharp 4. `SkiaSharp.HarfBuzz` shapes overlay text. No engine type is public: the members that exposed them (`EncoderSelector.CanonicalSlug`, `EncoderSelector.For`, `OverlayCompositor.ApplyAsync`, `KoanFontRegistry.CreateFont`) are gone or internal, and `Koan.Media.Abstractions` and `Koan.Media.Web` still name no engine type.

The gate on 4.154.0, on `runtime-deps:10.0.12-noble-chiseled-extra` as uid 1654:

| Check | Result |
|---|---|
| Fuzz, 300 s, `--memory=2g` | 52,500 mutated inputs: 0 crashes, 0 hangs, 0 managed exceptions, slowest 231 ms. Decode results Success 24,630, ErrorInInput 16,142, IncompleteInput 10,107, InvalidInput 4,977 |
| RAW decoding | Absent. `dng_`, `piex`, `SkRawCodec` occur 0 times in the 4.154.0 Linux native; 14, 1, 1 in 4.150.1 |
| Native load | `SkiaSharp.NativeAssets.Linux.NoDependencies` and `HarfBuzzSharp.NativeAssets.Linux` both load. `SkiaSharp.NativeAssets.Linux` does not, for lack of `libfontconfig.so.1` |

### Resizing

Skia's samplers do not widen their kernel when reducing: Mitchell measured PSNR 12.7 on a zone plate and 23.7–33.7 on art, mipmaps 35.6–39.9 against the previous default. Koan owns a separable Catmull-Rom scaled to the reduction (`Resampler`), over premultiplied pixels and on every frame. Its buffer is a window one kernel tall per worker, not a copy of the source. Rotation and overlay drawing use Skia's samplers.

### Decoding and limits

The source is read into memory first, asynchronously and cancellably: `MaxSourceBytes` (256 MiB in `Defaults` and in Web), with a ceiling of 1 GiB when no limit is named. Dimensions and frame count come from the header, and `MaxSourceMegapixels` and `MaxFrameCount` apply before any pixel is allocated, on non-seekable input as well. A fixed `maxDecodedMegapixels` of 512 (2 GiB of RGBA) applies whatever the caller's limits say, because a frame count and a frame size that each pass can still multiply into an allocation no host survives. Any result other than `Success` is refused; partial images are never served.

Frames are premultiplied RGBA8888. A 16-bit PNG reduces to 8 bits per channel.

### Output limit

A very small source can ask for very large frames: 3 KB of 16x16 WebP, 60 frames, into a 4096x4096 canvas is 4 GiB. `MaxOutputMegapixels` bounds what a render's frames may grow to in total, width times height times frames: 256 MP in `Defaults` and in Web (1 GiB of frames), with a ceiling of 512 MP when no limit is named. Every step that produces frames names their size to `RasterImage.Map`, which checks before the first is allocated; overlay layers and an SVG's rasterize target take the same check. Over it, the request gets `MediaSourceLimitException` (`maxOutputMegapixels`, HTTP 400). A step that does not grow the render is held by the source limits instead, so a large animation served at its own size still passes. At HTTP an overlay's pixel size also falls under `MaxOutputEdge`.

Cover computes only the box it keeps of the scaled picture, with the same result as scaling all of it and cropping, so an extreme aspect allocates the output and no more. Drawing a layer or a frame takes a view of its pixels, not a copy.

### Formats

| Source or request | What happens |
|---|---|
| Source JPEG, PNG, WebP, nothing pinned | Same format |
| Source GIF, nothing pinned | WebP, which keeps its animation and transparency (MEDIA-0009 §d: the source kind maps to the encoder that admits it). The loop count is not kept: the encoder writes no loop option, so an animation loops forever |
| Source BMP, ICO, WBMP, nothing pinned | PNG, which keeps every pixel |
| Source APNG | Reads as its first frame. `MediaInfo.FrameCount` is 1 |
| Source TIFF, TGA | Refused as unreadable |
| `/media/{id}` with no transform | The stored bytes as they are, whatever their format |
| `/media/{id}/gif`, `/bmp`, `/tiff` | 404: unproducible shortcuts resolve as unknown (MEDIA-0009 addendum) |
| `?format=gif`, `bmp`, `tiff` | Reported in `X-Koan-Media-IgnoredParams` and the format preserved; 400 in strict mode |
| Recipe pinning `gif`, `bmp`, `tiff` | Boot failure (`RecipeOutputFormatValidator`) |
| `EncodeAs("gif")` in code | `NotSupportedException` |
| Animated source to `EncodeAs("png")` or `"jpeg"` | `MediaPipelineKindMismatchException`, as before; `FlattenTo` is the explicit way through |

### Metadata

Skia's encoders write no Exif, XMP, IPTC or text, and offer no way to. So `MetadataKinds` is now `None`, `Icc` and `All = Icc`, and the `strip` key takes `none`, `icc` and `all`. `.Strip(MetadataKinds.All & ~MetadataKinds.Icc)` still compiles and means keep the profile, which is also the default. `strip=exif` is reported as ignored, or a 400 in strict mode; a configured recipe naming it fails at boot.

Stripping the profile converts the pixels to sRGB, so the picture looks the same. An sRGB profile is the same picture as none: it is neither carried nor reported by `MediaInfo.HasIccProfile`. `AutoOrient(keep: true)` leaves the stored pixels unrotated, and the output carries no orientation tag to say so.

### Text

Overlay text is split into lines on line feeds, each line into runs of one script and direction, and each run shaped by HarfBuzz; runs then reorder by level. Direction resolution follows the Unicode bidirectional algorithm where titles need it and does not interpret embedding controls. A mark stays in its base character's run. The layer runs from a small margin to the far edge of the ink, so an anchor places the words as the previous engine did, within 4 px in the measured case. Descenders that it clipped no longer are. Fonts stay registered and loaded by file path, and nothing depends on a system font. There is still no fallback between fonts: a glyph the registered font lacks draws as that font's missing glyph.

An SVG `<text>` is drawn by Svg.Skia through system fonts. On a host with none it draws nothing, with no error.

### Cancellation and streaming

No decode or encode call takes a token. The token is honoured while reading the source, between steps and frames, around overlay resolution, and while writing the output; one native call runs to its end.

The encoders are synchronous, and a response body refuses synchronous writes, so the encoded output sits in native memory until it is copied to the destination asynchronously. `WriteToAsync` remains the terminal, and nothing crosses the managed heap, but MEDIA-0008's "no buffer between the encoder and the sink" no longer holds. Skia's animated WebP encoder assembles its whole output in memory in any case. The buffer is bounded by the output's pixels. The determinism and write-once properties of MEDIA-0008 hold.

### Stale derivations

Cache keys and ETags come from the recipe's steps plus the source hash, so renders by the previous engine would be served forever. `MediaRecipe.OutputGeneration` is folded into every fingerprint and is now 2: bump it whenever the same steps start producing different bytes. Every recipe fingerprint and ETag changes once. Stored renders under the old fingerprints are never read again and are not reclaimed; Koan has no orphan sweep (MEDIA-0004 R07-17), so a host that wants the space back deletes those `MediaDerivation` rows itself.

## Bundled native code

SkiaSharp 4.154.0 builds on Skia m154 and bundles, per its `cgmanifest.json` at tag `v4.154.0`:

| Component | Version |
|---|---|
| libpng | 1.6.58 |
| libjpeg-turbo | 3.1.4.1 |
| libwebp | 1.6.0 |
| zlib | 1.3.2 |
| expat | 2.8.1 |
| FreeType | 2.14.3 |
| Brotli | 1.2.0 |
| Wuffs | 0.3.3 |

The libpng and libjpeg-turbo versions were also read from the Linux native itself. HarfBuzzSharp is 14.2.1.

**SkiaSharp publishes no codec advisories, and NuGet audit will not flag these components.** A CVE in libwebp, libpng or any of the others produces no warning in a Koan build. It has to be watched for and answered by a version bump. This is the standing cost of the decision; a green audit says nothing about the codecs.

## Attribution

`THIRD-PARTY-NOTICES.txt` ships in the native NuGet packages and does **not** reach a publish output, single-file or not. Several of the licenses it reproduces require their notices in binary distributions. A Koan application that distributes a build must carry that file itself, taken from `SkiaSharp.NativeAssets.Linux.NoDependencies` in its package cache.

## Consequences

**Positive.** A Release build needs no licence key. Encoder size and fidelity match the previous engine (JPEG q80 347 KB against 349 KB, WebP q80 297 KB against 296 KB, PSNR equal). One 4000x3000 JPEG to a 1600-wide WebP took 426 ms against 1176 ms. A header bomb is refused before allocating, on non-seekable input too.

**Negative.** Native code handles untrusted bytes, with no advisory feed for it. Peak working set for that same render is higher, 133 MB against 109 MB. Each platform carries a native payload, about 15 MB on Linux. GIF, BMP and TIFF output and TIFF and TGA input are gone. Koan owns a resampler and a bidi run splitter.

## Alternatives considered

- **Six Labors' community licence key.** Keeps a pure-managed engine, at the cost of a key in every consuming build. Set aside by the framework's owner after assessment; it remains the fallback if a later release fails this decision's gate.
- **Skia's own samplers.** Rejected on the measurements above.
- **A second encoder library for GIF.** Rejected: WebP keeps the animation and transparency GIF carries, with one engine to watch.
