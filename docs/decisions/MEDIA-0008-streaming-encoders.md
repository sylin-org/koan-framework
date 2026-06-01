# MEDIA-0008: Streaming Encoders via WriteToAsync

**Status**: Accepted
**Date**: 2026-05-31

Extends MEDIA-0005 (kind-aware pipeline) and MEDIA-0007 (cache-as-storage unification). Builds on the substrate from MEDIA-0004 (recipe pipeline). Closes the "encoded buffer sits in memory until the response flushes" gap that all three prior ADRs explicitly deferred.

## Contract

| Aspect | Specification |
|---|---|
| **Inputs** | A `IMediaPipeline` with an applied recipe and a destination `Stream` plus `CancellationToken`. The destination is typically `HttpResponse.Body` but is unconstrained (storage upload, test sink, file). |
| **Outputs** | Encoded bytes written directly to the destination stream as the encoder produces them. A `MediaOutput` carrying terminal-encode metadata (`ContentType`, `Format`, `Width`, `Height`, `FrameCount`, `Fingerprint`, `KindTrace`) is still returned for header population, but its `Bytes` property is `[Obsolete]` and forwards through an on-demand buffer for legacy callers. |
| **Error modes** | Pipeline planning errors (`MediaPipelineKindMismatchException`) still surface synchronously before any byte hits the destination. Encoder errors mid-stream surface as exceptions to the caller. Cancellation throws `OperationCanceledException` from `WriteToAsync`; partial bytes already flushed to the destination are the caller's problem (HTTP layer aborts the response). |
| **Success criteria** | Animated WebP at 1080p × 200 frames no longer allocates ~80 MB to a `MemoryStream` before serving. The single-encode invariant from MEDIA-0004 holds — one decode, one encoder, one write pass. The byte sequence produced by `WriteToAsync` is bit-identical to what `ToBytesAsync` produced before this ADR (ETag stability). MEDIA-0007 derivation write-through uploads from a stream where `Koan.Storage` accepts streams; falls back to a temp file otherwise. |

## Context

The framework's terminal materialisation path has always been buffer-first. `IMediaPipeline.ToBytesAsync` returns a `MediaOutput` whose `Bytes` property is a fully-encoded `byte[]`. The implementation in `MediaPipeline.EncodeAsync` allocates a `MemoryStream`, calls `Image.SaveAsync(ms, encoder)`, and copies the buffer out via `ms.ToArray()`. The `MediaController.RenderAsync` then hands that buffer to `File(bytes, contentType)`, which writes it to `Response.Body` in one shot.

Three concrete pains have surfaced as the pillar moves from "small static thumbnails" toward "anything ImageSharp and SkiaSharp can decode":

1. **Animated raster encodes balloon memory.** A 1920×1080 animated WebP at 200 frames lossless is roughly 80 MB encoded; with the `MemoryStream`'s growth strategy the working allocation is closer to 120 MB before `ToArray()` copies it to a fresh buffer. That allocation is held for the entire pipeline duration plus the response flush window. At even modest concurrency (8 simultaneous renders of the same source) the process sits on ~1 GB of transient bytes. The framework's host on stone-golden-summit has ~2.9 GB free total — this is operationally unacceptable for the Gposingway corpus, where animated mod previews are increasingly common.

2. **Video output is unreachable.** MEDIA-0005 §1 listed Timeline as a first-class kind. The current `byte[]` materialisation contract cannot express a video output: an MP4 muxer streams frames as it encodes; demanding the full buffer in memory before flush defeats the format's design. Any future video encoder is gated on this ADR.

3. **Storage write-through doubles the buffer.** MEDIA-0007 §c stamps the derivation onto a `MediaOutput.Bytes` byte array, then hands it to `IMediaSource.TryStoreDerivationAsync`. In the in-memory test source that copies the buffer into a `ConcurrentDictionary` entry. In a future filesystem-backed source it copies the buffer into a `File.WriteAllBytesAsync` call. Either way the buffer exists in two places — once as the response body source, once as the storage payload — for the duration of the request. For an 80 MB animated WebP that doubles to 160 MB held bytes.

The fix is direct: replace the byte buffer at every layer with a `Stream` the encoder writes into. ImageSharp's `Image.SaveAsync(Stream, encoder)` already accepts a stream — the existing call site at `MediaPipeline.cs:807` passes a `MemoryStream` for no reason other than that `MediaOutput.Bytes` demanded a `byte[]`. The buffer was a contract problem, not an implementation problem.

The two non-streaming paths in the codebase — the SVG rasterizer (`SvgRasterizer.RenderToPng` returns `byte[]`) and the legacy `IMediaOutputCache.SetAsync` shim — keep behaving as today through a `MemoryStream` adapter. They were already buffered in memory; this ADR doesn't make them worse.

## Decision

Introduce `WriteToAsync(Stream destination, CancellationToken ct)` as the new canonical terminal materialisation on `IMediaPipeline`. Mark `ToBytesAsync` and `MediaOutput.Bytes` as `[Obsolete]` but preserve them — they decorate over `WriteToAsync` via an internal `MemoryStream` so existing callers compile and behave identically. The controller, the storage write-through, and the encoder engine all switch to the stream path.

### a. `MediaOutput.WriteToAsync` becomes the bytes carrier

`MediaOutput` is widened to carry the streaming writer rather than the resolved buffer:

```csharp
public sealed record MediaOutput(
    Func<Stream, CancellationToken, Task> WriteToAsync,
    string ContentType,
    string Format,
    string SourceFormat,
    int Width,
    int Height,
    int FrameCount,
    string Fingerprint)
{
    public bool IsAnimated => FrameCount > 1;
    public IReadOnlyList<MediaKind> KindTrace { get; init; } = Array.Empty<MediaKind>();

    /// <summary>
    /// Materialise the encoded bytes into a freshly-allocated buffer.
    /// Obsolete: callers should stream directly into the destination
    /// (response body, storage upload) via <see cref="WriteToAsync"/>.
    /// Preserved for backward compatibility; allocates a MemoryStream
    /// internally on every access.
    /// </summary>
    [Obsolete("Use WriteToAsync(Stream, CancellationToken) to avoid allocating the full encoded buffer. See MEDIA-0008.", error: false)]
    public byte[] Bytes => MaterializeBufferOnDemand();

    private byte[] MaterializeBufferOnDemand()
    {
        using var ms = new MemoryStream();
        WriteToAsync(ms, CancellationToken.None).GetAwaiter().GetResult();
        return ms.ToArray();
    }
}
```

The `WriteToAsync` delegate closes over the decoded `Image` (or its rasterized stand-in for SVG) plus the encoder configuration that `EncodeAsync` resolved. It is **single-shot**: calling it twice on the same `MediaOutput` produces undefined results because the underlying `Image` is disposed after the first invocation. The pipeline already enforces single-use via `_consumed`; this ADR keeps that invariant by making the writer's `await` the disposal trigger.

Rejected alternatives:

- **Keep `Bytes` as the primary, add `WriteToAsync` as an opt-in.** Doubles the materialisation surface forever. Callers default to the buffered path because it's familiar, and the memory pain persists indefinitely. The `[Obsolete]` on `Bytes` makes the migration direction unambiguous.
- **Return an `IAsyncEnumerable<byte[]>` of chunks.** Forces every consumer (response writer, storage upload, file sink) to implement a chunk loop. ASP.NET Core's `Response.Body` is a `Stream`; storage providers' upload APIs take streams. The `Stream` destination is the lingua franca; an enumerable would require an adapter at every call site.

### b. `IMediaPipeline.WriteToAsync` is the canonical terminal

The pipeline interface gains:

```csharp
/// <summary>
/// Stream the encoded result directly into <paramref name="destination"/>.
/// Replaces <see cref="ToBytesAsync"/> as the canonical terminal; the
/// older method now decorates this one via a MemoryStream and is
/// [Obsolete]. Per MEDIA-0008.
/// </summary>
Task<MediaOutput> WriteToAsync(Stream destination, CancellationToken ct = default);

[Obsolete("Use WriteToAsync(Stream, CancellationToken) to stream directly into the response or storage. See MEDIA-0008.", error: false)]
Task<MediaOutput> ToBytesAsync(CancellationToken ct = default);
```

`WriteToAsync` returns a `MediaOutput` so headers (`ETag`, `Content-Type`, `X-Koan-Media-*`) still populate after the write completes. The `MediaOutput.WriteToAsync` closure is set to a no-op that throws `InvalidOperationException` ("already streamed; consume MediaOutput.WriteToAsync only when calling the buffered ToBytesAsync path") — the controller-style flow writes through the pipeline's stream method, then uses the returned `MediaOutput` for metadata only.

The default `ToBytesAsync` implementation becomes:

```csharp
public async Task<MediaOutput> ToBytesAsync(CancellationToken ct = default)
{
    using var ms = new MemoryStream();
    var output = await WriteToAsync(ms, ct).ConfigureAwait(false);
    var bytes = ms.ToArray();
    return output with
    {
        WriteToAsync = (dest, ict) => dest.WriteAsync(bytes, ict).AsTask(),
    };
}
```

Here `output with { WriteToAsync = ... }` is the migration accommodation: a `MediaOutput` returned from `ToBytesAsync` carries a writer that replays the captured buffer, so the obsolete `Bytes` property works idempotently. Code that mixes the two APIs sees consistent bytes.

### c. ImageSharp encode threads the destination stream

`MediaPipeline.EncodeAsync` already calls `await working.SaveAsync(ms, encoder, ct)`. The change is mechanical:

```csharp
// BEFORE
await using var ms = new MemoryStream();
await working.SaveAsync(ms, encoder, ct).ConfigureAwait(false);
var bytes = ms.ToArray();
return new MediaOutput(Bytes: bytes, /* ... */);

// AFTER
Func<Stream, CancellationToken, Task> writer = async (destination, writerCt) =>
{
    await working.SaveAsync(destination, encoder, writerCt).ConfigureAwait(false);
};
return new MediaOutput(WriteToAsync: writer, /* ... */);
```

The `working` image is captured by the closure; the pipeline's existing `_consumed` flag plus the disposal-after-terminal pattern already guarantee single-use semantics, and the encoder's `SaveAsync` is the only step that consumes the image. Disposal of `working` (and the source `image`) moves into the closure so the encoder owns its lifetime:

```csharp
Func<Stream, CancellationToken, Task> writer = async (destination, writerCt) =>
{
    try
    {
        await working.SaveAsync(destination, encoder, writerCt).ConfigureAwait(false);
    }
    finally
    {
        composed?.Dispose();
        // image disposed by the outer using statement only after WriteToAsync runs
    }
};
```

The pipeline's outer `using var image = await LoadOrThrowAsync(...)` would otherwise dispose `image` before the writer fires. The fix is to lift disposal into the writer: `EncodeAsync` returns the `MediaOutput` with the writer closure capturing `image`, and the pipeline's terminal method (`WriteToAsync` on `MediaPipeline`) awaits the destination flush before disposing.

This is the load-bearing refactor: the `using` blocks in `MediaPipeline.ToBytesAsync` and `MaterializeAsync` move from synchronous (compile-time-scoped) disposal to writer-scoped disposal. The encoder, not the terminal method, owns the image lifecycle.

### d. SVG rasterizer stays buffered through a MemoryStream adapter

`SvgRasterizer.RenderToPng` returns `byte[]` today and changing it requires a Skia-side refactor outside this ADR's scope. The MEDIA-0006 SVG path in `MediaPipeline.EncodeSvgAsync` already materialises the PNG bytes in memory; this ADR retains that step and wraps the resulting buffer in a `MemoryStream` for the downstream ImageSharp re-decode. The Skia bytes are already in memory anyway — the streaming win for SVG comes from the *terminal* encode (ImageSharp's `SaveAsync` writing into the response stream), not the intermediate rasterization.

Future ADRs may swap `SvgRasterizer.RenderToPng` for a streaming variant if profiling shows it matters; today the PNG buffer is a few MB at most (rasterized at the planner's target dimensions, typically 600×750 or 1200×1500), an order of magnitude smaller than the animated raster case this ADR targets.

### e. MediaController writes through Response.Body

`MediaController.RenderAsync` currently builds a `File(output.Bytes, output.ContentType)` action result. The replacement writes the headers up front and streams directly:

```csharp
// BEFORE
return File(output.Bytes, output.ContentType);

// AFTER
Response.ContentType = output.ContentType;
Response.Headers[HeaderNames.ETag] = etag;
Response.Headers[HttpHeaderNames.CacheControl] = _options.DefaultCacheControl;
// ... other headers (Vary, X-Koan-Media-*) ...
await output.WriteToAsync(Response.Body, ct).ConfigureAwait(false);
return new EmptyResult();
```

`EmptyResult` is the canonical ASP.NET pattern for "I've already written the response; don't do anything else." The diagnostic headers populate from the `MediaOutput` returned by the pipeline's `WriteToAsync` before the write begins (the writer is a closure; the encoder hasn't run yet at the point we read the metadata).

Wait — that's incorrect. The metadata (`Width`, `Height`, `FrameCount`, `Fingerprint`) is populated by `EncodeAsync` *before* it returns the `MediaOutput`, but those fields are derived from the `working` image's post-Mutate state. They're already final at the point `EncodeAsync` returns; the encoder's `SaveAsync` only writes the already-resolved pixel buffer. So the metadata is safe to read before invoking `WriteToAsync`. The header order in the controller becomes: pipeline returns `MediaOutput` (metadata final, writer pending) → controller writes headers → controller invokes writer → encoder streams bytes.

The conditional-GET short-circuit at `MediaController.cs:180` is untouched — `If-None-Match` returns 304 without invoking the pipeline at all, and 304 has no body.

### f. MEDIA-0007 derivation write-through uploads from a stream

`IMediaSource.TryStoreDerivationAsync` currently takes a `MediaOutput` whose `Bytes` is a `byte[]`. With `Bytes` obsoleted, the method needs an explicit stream-acceptance contract:

```csharp
Task TryStoreDerivationAsync(
    string sourceId,
    string recipeFingerprint,
    MediaOutput output,           // metadata (content type, fingerprint)
    Func<Stream, CancellationToken, Task> writer,  // bytes
    string? recipeName,
    string? recipeVersion,
    CancellationToken ct = default);
```

Hosts that back the source with `Koan.Storage` pass `writer` directly to `IStorageEntity.Upload(Stream, ...)` if the provider takes a stream; otherwise they buffer to a `MemoryStream` (filesystem providers' atomic-write tempfile already buffers by necessity) or a `Path.GetTempFileName()` temp file for very large payloads.

The controller's call site changes from:

```csharp
await _source.TryStoreDerivationAsync(id, fingerprint, output, recipeName, recipeVersion, ct);
```

to a two-step flow where the writer is invoked twice — once into the response, once into storage. The response write happens first (the user is waiting); the storage upload happens after, in a fire-and-forget continuation:

```csharp
// Render headers + response body
Response.ContentType = output.ContentType;
// ... headers ...
await output.WriteToAsync(Response.Body, ct).ConfigureAwait(false);

// Storage write-through (best-effort, async, off the response path).
// MediaOutput.WriteToAsync is single-shot — the response just consumed
// the writer. For the storage write we need a SECOND writer over the
// same encoded bytes. EncodeAsync returns a writer that re-encodes on
// each invocation (it captures the decoded Image + recipe; the encoder
// is idempotent), so a second call produces the same bytes.
```

That last sentence is load-bearing. The writer closure must be **idempotent across multiple invocations** — calling it twice produces the same bytes both times. ImageSharp's `Image.SaveAsync` does not consume the image (no destructive side effect on the pixel buffer), so re-invoking the writer with a fresh destination is safe. The implementation note: don't dispose `working` inside the writer's `finally` block; instead, the pipeline retains ownership and disposes after the storage write-through completes (or after some timeout). Practically this means the `EncodeAsync` method returns a `MediaOutput` whose writer closure does NOT dispose anything, and the calling `WriteToAsync` on `MediaPipeline` retains the `image` lifetime until both the response write and the storage write-through have run.

The cleanest expression is a `MediaRenderHandle : IAsyncDisposable` returned by the pipeline that exposes the metadata and the writer while owning the underlying image; the controller `await using` the handle, calls writer twice, and disposal releases the image. For minimal API churn this ADR keeps `MediaOutput` as the return type and accepts that its writer may be invoked 0, 1, or 2 times before the pipeline tears down its image. Pipeline disposal becomes "after both writes have run or the response cancelled".

### g. Cancellation semantics

`WriteToAsync(Stream, CancellationToken)` cooperatively cancels at the encoder's frame boundaries (animated paths) or at the next async checkpoint (static paths). A cancelled write may leave partial bytes in the destination — for HTTP responses the framework drops the connection; for storage uploads the framework deletes the partial upload (filesystem providers via temp-file unlink, S3/Azure via abort-multipart). The contract is: callers are responsible for the partial-write cleanup on their destination; the pipeline does not retry.

## What we explicitly DON'T do

- **No `IAsyncEnumerable<byte[]>` surface.** The `Stream` destination is the canonical Anthropic for byte writers — `Response.Body`, storage uploads, file streams all consume one. Adding an enumerable variant doubles the surface for zero new capability.
- **No write-side compression negotiation.** `Content-Encoding: gzip` etc. are middleware concerns; this ADR does not add `Vary: Accept-Encoding` or recompress.
- **No video encoders.** Threading the stream contract through ImageSharp's existing raster encoders is this ADR's scope. Video (MP4 muxer over a Timeline source) needs its own encoder registration plus the Timeline kind producer; that's a separate ADR.
- **No streaming SVG rasterizer.** `SvgRasterizer.RenderToPng` keeps its `byte[]` signature; the PNG bytes flow through a `MemoryStream` to satisfy the downstream stream contract.
- **No retry on partial writes.** A cancelled or faulted `WriteToAsync` leaves the destination in an undefined state and is not idempotent at the byte level. Re-rendering the same recipe produces the same bytes (deterministic encoder), so callers retry by calling `WriteToAsync` again with a fresh destination.

## Consequences

**Positive.**

- The 80 MB animated WebP case allocates O(encoder-frame-buffer) instead of O(full-encoded-output). Peak working-set during a render drops by an order of magnitude for animated and high-resolution raster sources.
- Video output becomes reachable. A future MP4 encoder can declare `Accepts = KindSet.Timeline` and stream frames into the response without first allocating the entire MP4 in memory.
- Storage write-through avoids the doubled buffer: filesystem providers stream into their tempfile, S3/Azure providers stream into multipart uploads.
- The ETag is preserved across the migration. The encoder produces bit-identical bytes (the change is *where* the bytes go, not *what* they are), so the existing `{sourceHash12}-{fingerprint}` ETag formula keeps working without any cache invalidation.

**Negative.**

- The disposal lifecycle for the decoded `Image` moves from compile-time-scoped (`using` block) to writer-scoped (released after the writer closure completes). Reviewers need to trace the lifetime through the closure; the `using` keyword no longer tells the whole story.
- Idempotency-on-double-invocation is a property the writer closure must guarantee. A future engine change that makes the encoder destructive (e.g. a streaming encoder that consumes the source image's frames as it writes) breaks the second invocation. This is a discipline the test suite enforces via the "WriteToAsync produces same bytes as ToBytesAsync, and a second call produces the same bytes as the first" test.
- Callers that mixed the obsolete `Bytes` property with the new `WriteToAsync` see two encodes — one for the obsolete `Bytes` getter materialising into a `MemoryStream`, one for the streaming call. The deprecation warning steers them away; for the migration window both work.
- The `IAsyncDisposable`-style handle would have been cleaner but adds an extra type to the public surface. This ADR accepts the cleanup-after-second-use rule on `MediaOutput.WriteToAsync` as a documented constraint; a future ADR may introduce `MediaRenderHandle` if the discipline proves error-prone.

## Migration

Two-release deprecation matches MEDIA-0007's pattern:

- **This release (MEDIA-0008).** `IMediaPipeline.WriteToAsync` and `MediaOutput.WriteToAsync` ship as the canonical materialisation. `IMediaPipeline.ToBytesAsync` and `MediaOutput.Bytes` are `[Obsolete]` but functional; they buffer through a `MemoryStream` and produce identical bytes. `MediaController.RenderAsync` calls `WriteToAsync(Response.Body, ct)`. `IMediaSource.TryStoreDerivationAsync` accepts a `Func<Stream, CancellationToken, Task>` writer parameter alongside the metadata; in-memory and filesystem sources buffer; future Koan.Storage-backed sources stream.
- **Next release (MEDIA-0009 or later).** `MediaOutput.Bytes` and `IMediaPipeline.ToBytesAsync` are deleted. The obsolete shims go away.

Host migration:
1. Replace `output.Bytes` with `await output.WriteToAsync(destination, ct)` at every call site. Compiler warnings list them all.
2. If implementing `IMediaSource` with derivation persistence, update `TryStoreDerivationAsync` to take the writer parameter and pass it to the storage upload API.
3. Re-run the `StreamingOutputSpec` test suite — the contracts there match the migration's success criteria.

No data migration. ETags are stable; derivation bytes are stable; HTTP semantics are stable.

## Out of scope

- **Range requests.** `Range: bytes=...` support requires either a seekable stream or storage-side range handling. This ADR does not add it; the existing `IMediaSource.OpenDerivationAsync` path already returns a `Stream` that ASP.NET can serve range requests against when the host wires it up.
- **Streaming SVG rasterizer.** Skia-side refactor, separate ADR.
- **Video output.** Separate ADR.
- **`Vary: Accept-Encoding`** and content negotiation for response compression. Middleware concern.
- **Backpressure across response cancellation.** ASP.NET handles client disconnects via `HttpContext.RequestAborted`; the pipeline cooperates by passing that token through `WriteToAsync`.

## Test coverage requirements

- **Correctness invariant.** `WriteToAsync` writes the same bytes as `ToBytesAsync` for the same source and recipe (verify byte-equality across a static raster, animated raster, and SVG source).
- **Animated source bytes are valid.** Stream an animated source through `WriteToAsync` into a `MemoryStream`; decode the resulting bytes via `Image.LoadAsync`; assert `Frames.Count` matches the input frame count.
- **Memory ceiling.** Render a large animated source through `WriteToAsync` into a tracking stream; assert peak managed allocations stay below a documented ceiling (using `GC.GetAllocatedBytesForCurrentThread()` between checkpoints).
- **Cancellation mid-write.** Pass a cancelled token after the writer has begun; assert `OperationCanceledException` propagates and the destination contains a prefix (not the full output).
- **ETag determinism.** Same source + same recipe produces bit-identical output bytes via `WriteToAsync` across two invocations (separates the writer-idempotency property from the source/recipe determinism).
- **Storage write-through stream path.** `IMediaSource.TryStoreDerivationAsync` is called with the writer parameter; the storage source captures bytes equal to the response-body bytes.
- **Obsolete `Bytes` accessor returns same bytes.** For code that still uses `output.Bytes`, the buffered fallback produces the same byte sequence as `WriteToAsync` into a `MemoryStream`.
