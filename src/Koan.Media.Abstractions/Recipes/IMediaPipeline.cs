namespace Koan.Media.Abstractions.Recipes;

/// <summary>
/// Single-decode, multi-output pipeline over a source byte stream.
/// Lazy: nothing decodes until <see cref="ToBytesAsync"/>,
/// <see cref="MaterializeAsync"/>, or <see cref="ProbeAsync"/> is
/// called. Per MEDIA-0004 §4.
///
/// Disposal: implementations dispose the source stream on the first
/// terminal call; do not reuse the pipeline instance afterwards.
/// </summary>
public interface IMediaPipeline
{
    /// <summary>Apply a complete recipe in one shot. Convenience over chaining individual verbs.</summary>
    IMediaPipeline Apply(MediaRecipe recipe);

    /// <summary>EXIF-based auto-orient.</summary>
    IMediaPipeline AutoOrient(bool keep = false);

    /// <summary>Extract a single frame from animated sources; no-op on static.</summary>
    /// <remarks>
    /// Per MEDIA-0005: prefer <see cref="Sample(FrameSelector)"/> with
    /// <see cref="Sample.Frame(int)"/> for the canonical, kind-agnostic
    /// collapse. This verb continues to compile and delegates to
    /// <c>Sample(FrameSelector.Index(index))</c>.
    /// </remarks>
    [Obsolete("Use Sample(FrameSelector.Index(n)) or the Sample.Frame(n) factory.")]
    IMediaPipeline ExtractFrame(int index = 0);

    /// <summary>
    /// Kind-agnostic sample. Collapses any source kind into a single
    /// raster according to the given selector. No-op on
    /// <see cref="MediaKind.Raster"/>. Per MEDIA-0005 §2.
    /// </summary>
    IMediaPipeline Sample(FrameSelector selector);

    /// <summary>
    /// Trim an animated source to its first <paramref name="frames"/>
    /// frames. No-op on a static source. Use named-arg form
    /// (<c>Trim(frames: 60)</c>) — it pairs with the seconds overload and
    /// reads as intent in recipe code.
    /// </summary>
    IMediaPipeline Trim(int frames);

    /// <summary>
    /// Trim an animated source to its first <paramref name="seconds"/>
    /// of playback, walking per-frame delay metadata to find the cutoff.
    /// Accepts fractional values (<c>Trim(seconds: 0.5)</c>). No-op on a
    /// static source or when the format's frame-delay metadata isn't
    /// recognised — the step never silently flattens.
    /// </summary>
    IMediaPipeline Trim(double seconds);

    /// <summary>
    /// Collapse an animated source to a single frame, picked at index
    /// <paramref name="at"/> (default 0). The loud, opt-in alternative to
    /// <c>Sample(FrameSelector.Index(n))</c> when the call site's intent
    /// is "make this static" — the name reads at a glance so a future
    /// reader can't miss that the recipe is destroying animation.
    /// </summary>
    IMediaPipeline Freeze(int at = 0);

    /// <summary>Rotate by degrees clockwise.</summary>
    IMediaPipeline Rotate(int degrees);

    /// <summary>Flip horizontally.</summary>
    IMediaPipeline FlipHorizontal();

    /// <summary>Flip vertically.</summary>
    IMediaPipeline FlipVertical();

    /// <summary>CSS-aligned shape step (single slot).</summary>
    IMediaPipeline Shape(
        CropSpec? crop = null,
        Fit fit = Fit.Cover,
        Position? position = null,
        Background? background = null);

    /// <summary>Convenience over <see cref="Shape"/>: set crop alone (fit defaults to Cover, position to Center).</summary>
    IMediaPipeline Crop(CropSpec spec);

    /// <summary>Convenience: parse a crop spec string (<c>square</c>, <c>16:9</c>, <c>400x200</c>) and apply.</summary>
    IMediaPipeline Crop(string spec);

    /// <summary>
    /// Single-slot resize. <paramref name="upscale"/> defaults to <c>false</c>:
    /// when the source already fits inside the target box on both axes the
    /// resize is skipped (the source dimensions and bytes survive). Set
    /// <c>upscale: true</c> when the caller genuinely needs a smaller source
    /// enlarged (rare); the convenience <c>ResizeCover</c> sets it for you
    /// because covering a target box requires enlarging by definition.
    /// </summary>
    IMediaPipeline Resize(int? width = null, int? height = null, double dpr = 1.0, bool upscale = false);

    /// <summary>Convenience: fit-within with both axes capped.</summary>
    IMediaPipeline ResizeFit(int maxWidth, int maxHeight);

    /// <summary>Convenience: cover crop to exact dimensions.</summary>
    IMediaPipeline ResizeCover(int width, int height, Position? position = null);

    /// <summary>
    /// Composite a media-backed overlay layer onto the host. Multiple
    /// calls append additional layers (drawn in declared order). Per
    /// MEDIA-0004 §7.
    /// </summary>
    IMediaPipeline Overlay(
        string mediaId,
        OverlaySize? size = null,
        Position? position = null,
        OverlayPadding? padding = null,
        double opacity = 1.0,
        int rotate = 0,
        string? recipeName = null);

    /// <summary>Composite a text overlay layer onto the host (requires a registered font).</summary>
    IMediaPipeline OverlayText(
        string text,
        string? font = null,
        BackgroundColor? color = null,
        int fontSize = 32,
        Position? position = null,
        OverlayPadding? padding = null,
        double opacity = 1.0,
        int rotate = 0);

    /// <summary>Strip selected metadata kinds before encode.</summary>
    IMediaPipeline Strip(MetadataKinds kinds = MetadataKinds.All);

    /// <summary>Encode in source format (default behaviour).</summary>
    IMediaPipeline PreserveFormat(int quality = Quality.Web);

    /// <summary>Encode in the named format; preserves animation/alpha if target supports them.</summary>
    IMediaPipeline EncodeAs(string format, int quality = Quality.Web);

    /// <summary>Destructive: force a flat single-frame encode in the target format.</summary>
    IMediaPipeline FlattenTo(string format, int quality = Quality.Web);

    /// <summary>Inspect source metadata without committing to an encode.</summary>
    Task<MediaInfo> ProbeAsync(CancellationToken ct = default);

    /// <summary>
    /// Materialise to a single output, returning the encoded bytes and content type.
    /// </summary>
    /// <remarks>
    /// Per MEDIA-0008: prefer <see cref="WriteToAsync"/> for production
    /// rendering paths so animated/large encodes do not allocate the full
    /// output buffer in memory. This method is retained for tests and
    /// callers that genuinely need the bytes (e.g. content-addressing,
    /// hash computation) and decorates <see cref="WriteToAsync"/> via an
    /// internal <see cref="MemoryStream"/>.
    /// </remarks>
    [Obsolete("Use WriteToAsync(Stream, CancellationToken) to stream directly into the response or storage. See MEDIA-0008.", error: false)]
    Task<MediaOutput> ToBytesAsync(CancellationToken ct = default);

    /// <summary>
    /// Stream the encoded output directly into <paramref name="destination"/>.
    /// Returns a <see cref="MediaOutput"/> carrying the terminal-encode
    /// metadata (content type, dimensions, frame count, fingerprint,
    /// kind trace) so the HTTP layer can populate response headers
    /// without buffering the bytes. Per MEDIA-0008 §b — the canonical
    /// materialisation that replaces <see cref="ToBytesAsync"/>.
    /// </summary>
    Task<MediaOutput> WriteToAsync(Stream destination, CancellationToken ct = default);

    /// <summary>
    /// Multi-variant materialisation. One decode, N encodes — each
    /// child callback configures its own pipeline branch on top of the
    /// shared decoded image. Per MEDIA-0004 §11.
    /// </summary>
    Task<MediaBundle> MaterializeAsync(Action<MediaBundleBuilder> configure, CancellationToken ct = default);
}

/// <summary>
/// Encoded bytes plus the metadata needed to serve the result over HTTP.
/// </summary>
/// <param name="Bytes">
/// Encoded output bytes. Per MEDIA-0008 prefer <see cref="WriteToAsync"/> to
/// stream the bytes directly into the destination (response body, storage
/// upload) instead of holding the full encoded buffer in memory. This
/// field is retained for backward compatibility; for the streaming
/// terminal path it is populated by buffering through a
/// <see cref="MemoryStream"/> on demand.
/// </param>
/// <param name="ContentType">MIME type matching <paramref name="Format"/>.</param>
/// <param name="Format">Canonical output format slug (jpeg, png, webp, gif, ...).</param>
/// <param name="SourceFormat">Canonical source format slug as decoded. Equals <paramref name="Format"/> when the recipe preserves format.</param>
/// <param name="Width">Output width in pixels.</param>
/// <param name="Height">Output height in pixels.</param>
/// <param name="FrameCount">Output frame count (1 for static).</param>
/// <param name="Fingerprint">Per-output content fingerprint (informational; recipe fingerprint lives on the recipe).</param>
public sealed record MediaOutput(
    [property: Obsolete("Use WriteToAsync(Stream, CancellationToken) to stream the encoded bytes directly into the destination. See MEDIA-0008.", error: false)]
    byte[] Bytes,
    string ContentType,
    string Format,
    string SourceFormat,
    int Width,
    int Height,
    int FrameCount,
    string Fingerprint)
{
    public bool IsAnimated => FrameCount > 1;

    /// <summary>
    /// Per-step kind transitions recorded by the planner. Per
    /// MEDIA-0005 §7: surfaced as the <c>X-Koan-Media-KindTrace</c>
    /// response header. Empty when the source predates kind tracking.
    /// </summary>
    public IReadOnlyList<MediaKind> KindTrace { get; init; } = Array.Empty<MediaKind>();

    private readonly Func<Stream, CancellationToken, Task>? _writeToAsync;

    /// <summary>
    /// Stream the encoded bytes into the caller-supplied destination
    /// stream. Per MEDIA-0008, this is the canonical way to surface
    /// encoded output — HTTP responses thread <see cref="System.IO.Stream"/>
    /// end-to-end so animated and high-resolution encodes don't allocate
    /// the full output buffer.
    /// <para>When unset, the writer falls back to copying
    /// <see cref="Bytes"/> into the destination — so legacy callers that
    /// constructed <see cref="MediaOutput"/> without a writer continue
    /// to work. The recipe pipeline overrides this property with a
    /// closure that drives the encoder directly into the destination
    /// (no intermediate <see cref="MemoryStream"/>).</para>
    /// </summary>
    public Func<Stream, CancellationToken, Task> WriteToAsync
    {
        get => _writeToAsync ?? BufferedWriter;
        init => _writeToAsync = value;
    }

    private Task BufferedWriter(Stream destination, CancellationToken ct)
    {
#pragma warning disable CS0618 // Falling back to the obsolete byte buffer is the documented default behaviour.
        var bytes = Bytes ?? Array.Empty<byte>();
#pragma warning restore CS0618
        return destination.WriteAsync(bytes, ct).AsTask();
    }

    /// <summary>
    /// Optional async-disposable hook the pipeline can attach to release
    /// the decoded source image once all <see cref="WriteToAsync"/>
    /// invocations have completed. When unset, the writer is considered
    /// self-contained (legacy buffered path) and no cleanup is needed.
    /// </summary>
    public IAsyncDisposable? RenderResources { get; init; }
}

/// <summary>
/// Result of <see cref="IMediaPipeline.MaterializeAsync"/>. Keyed by
/// variant name; values are independent encoded outputs.
/// </summary>
public sealed record MediaBundle(IReadOnlyDictionary<string, MediaOutput> Variants);

/// <summary>
/// Builder surface passed to the <see cref="IMediaPipeline.MaterializeAsync"/>
/// callback. Each <see cref="Add"/> declares one named variant whose
/// branch is configured by the supplied func.
/// </summary>
public sealed class MediaBundleBuilder
{
    private readonly List<(string Name, Func<IMediaPipeline, IMediaPipeline> Configure)> _variants = new();

    /// <summary>Declared variants. Used by engines.</summary>
    public IReadOnlyList<(string Name, Func<IMediaPipeline, IMediaPipeline> Configure)> Variants => _variants;

    /// <summary>Add a named variant whose pipeline is configured by <paramref name="configure"/>.</summary>
    public MediaBundleBuilder Add(string name, Func<IMediaPipeline, IMediaPipeline> configure)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Variant name required.", nameof(name));
        if (_variants.Any(v => v.Name == name))
            throw new ArgumentException($"Duplicate variant name '{name}' in bundle.", nameof(name));
        _variants.Add((name, configure));
        return this;
    }
}
