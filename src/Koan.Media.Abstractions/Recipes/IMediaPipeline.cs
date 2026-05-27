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
    IMediaPipeline ExtractFrame(int index = 0);

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

    /// <summary>Single-slot resize.</summary>
    IMediaPipeline Resize(int? width = null, int? height = null, double dpr = 1.0);

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

    /// <summary>Materialise to a single output, returning the encoded bytes and content type.</summary>
    Task<MediaOutput> ToBytesAsync(CancellationToken ct = default);

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
/// <param name="Bytes">Encoded output bytes.</param>
/// <param name="ContentType">MIME type matching <paramref name="Format"/>.</param>
/// <param name="Format">Canonical output format slug (jpeg, png, webp, gif, ...).</param>
/// <param name="SourceFormat">Canonical source format slug as decoded. Equals <paramref name="Format"/> when the recipe preserves format.</param>
/// <param name="Width">Output width in pixels.</param>
/// <param name="Height">Output height in pixels.</param>
/// <param name="FrameCount">Output frame count (1 for static).</param>
/// <param name="Fingerprint">Per-output content fingerprint (informational; recipe fingerprint lives on the recipe).</param>
public sealed record MediaOutput(
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
