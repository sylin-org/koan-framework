using System.Collections.Immutable;
using Koan.Media.Abstractions.Recipes;
using Koan.Media.Core.Fonts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;

namespace Koan.Media.Core.Pipeline;

/// <summary>
/// Default <see cref="IMediaPipeline"/> implementation. Per
/// MEDIA-0004:
/// <list type="bullet">
///   <item>Single decode, multi-output via <see cref="MaterializeAsync"/></item>
///   <item>Format-preserving by default; <see cref="FlattenTo"/> opts into destructive change</item>
///   <item>ImageSharp's <c>Image</c> already applies <see cref="Mutate(IImageProcessingContext)"/> to all frames, so animation survives resize/crop/rotate transparently</item>
/// </list>
///
/// Disposal: source stream is disposed at the first terminal call
/// (<see cref="ToBytesAsync"/>, <see cref="MaterializeAsync"/>, <see cref="ProbeAsync"/>).
/// </summary>
public sealed class MediaPipeline : IMediaPipeline
{
    private readonly Stream _source;
    private readonly bool _disposeSource;
    private readonly ILogger _logger;
    private readonly IOverlayResolver? _overlayResolver;
    private readonly KoanFontRegistry? _fonts;
    private readonly MediaPipelineLimits _limits;
    private readonly int _overlayDepth;
    private readonly List<MediaStep> _steps = new();
    private bool _consumed;

    /// <summary>Entry point: <c>stream.AsMedia()</c> → pipeline.</summary>
    public static IMediaPipeline From(
        Stream source,
        ILogger? logger = null,
        bool disposeSource = true,
        IOverlayResolver? overlayResolver = null,
        KoanFontRegistry? fonts = null,
        MediaPipelineLimits? limits = null) =>
        new MediaPipeline(source, logger ?? NullLogger.Instance, disposeSource,
            overlayResolver, fonts, limits ?? MediaPipelineLimits.Unlimited, overlayDepth: 0);

    private MediaPipeline(
        Stream source,
        ILogger logger,
        bool disposeSource,
        IOverlayResolver? overlayResolver,
        KoanFontRegistry? fonts,
        MediaPipelineLimits limits,
        int overlayDepth)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _logger = logger;
        _disposeSource = disposeSource;
        _overlayResolver = overlayResolver;
        _fonts = fonts;
        _limits = limits;
        _overlayDepth = overlayDepth;
    }

    // ----- builder verbs (mirror MediaRecipeBuilder; single-slot replacement enforced) -----

    public IMediaPipeline Apply(MediaRecipe recipe)
    {
        foreach (var step in recipe.Steps) AddStep(step);
        return this;
    }

    public IMediaPipeline AutoOrient(bool keep = false) =>
        AddStep(new AutoOrientStep(Keep: keep));

    public IMediaPipeline ExtractFrame(int index = 0) =>
        AddStep(new ExtractFrameStep(index));

    public IMediaPipeline Rotate(int degrees) =>
        AddStep(new RotateStep(degrees));

    public IMediaPipeline FlipHorizontal() =>
        AddStep(new FlipStep(FlipAxis.Horizontal));

    public IMediaPipeline FlipVertical() =>
        AddStep(new FlipStep(FlipAxis.Vertical));

    public IMediaPipeline Shape(CropSpec? crop = null, Fit fit = Fit.Cover, Position? position = null, Background? background = null)
    {
        var shape = new ShapeStep(
            Crop: crop,
            Fit: fit,
            Position: position ?? Position.Center,
            Background: background ?? Background.Transparent());
        return AddStep(shape);
    }

    public IMediaPipeline Crop(CropSpec spec) => Shape(crop: spec);

    public IMediaPipeline Crop(string spec)
    {
        if (!CropSpec.TryParse(spec, out var parsed))
            throw new ArgumentException($"Invalid crop value '{spec}'.", nameof(spec));
        return Crop(parsed);
    }

    public IMediaPipeline Resize(int? width = null, int? height = null, double dpr = 1.0) =>
        AddStep(new ResizeStep(width, height, dpr));

    public IMediaPipeline ResizeFit(int maxWidth, int maxHeight) =>
        Resize(maxWidth, maxHeight).Shape(fit: Fit.Contain);

    public IMediaPipeline ResizeCover(int width, int height, Position? position = null) =>
        // Resize+Fit.Cover maps to ImageSharp's ResizeMode.Crop which already
        // does scale-then-crop natively. Adding an explicit CropSpec.Pixels
        // here would cause the Shape stage (40) to do a literal pixel-rect
        // crop BEFORE the Size stage (50) ever runs — producing a center
        // chunk of the source instead of a cover-scaled thumbnail.
        Resize(width, height).Shape(fit: Fit.Cover, position: position ?? Position.Center);

    public IMediaPipeline Overlay(
        string mediaId,
        OverlaySize? size = null,
        Position? position = null,
        OverlayPadding? padding = null,
        double opacity = 1.0,
        int rotate = 0,
        string? recipeName = null)
    {
        var layer = new OverlayLayer(
            Source: new MediaOverlaySource(mediaId, recipeName),
            Size: size ?? OverlaySize.Natural,
            Position: position ?? Position.Center,
            Padding: padding ?? OverlayPadding.Zero,
            Opacity: Math.Clamp(opacity, 0.0, 1.0),
            Rotate: rotate);
        return AppendOverlayLayer(layer);
    }

    public IMediaPipeline OverlayText(
        string text,
        string? font = null,
        BackgroundColor? color = null,
        int fontSize = 32,
        Position? position = null,
        OverlayPadding? padding = null,
        double opacity = 1.0,
        int rotate = 0)
    {
        var layer = new OverlayLayer(
            Source: new TextOverlaySource(text, font, color, fontSize),
            Size: OverlaySize.Natural,
            Position: position ?? Position.Center,
            Padding: padding ?? OverlayPadding.Zero,
            Opacity: Math.Clamp(opacity, 0.0, 1.0),
            Rotate: rotate);
        return AppendOverlayLayer(layer);
    }

    private MediaPipeline AppendOverlayLayer(OverlayLayer layer)
    {
        EnsureUnconsumed();
        var existing = _steps.FindIndex(s => s.Stage == PipelineStage.Overlay);
        if (existing >= 0)
        {
            var current = (OverlayStep)_steps[existing];
            _steps[existing] = current with { Layers = current.Layers.Add(layer) };
        }
        else
        {
            _steps.Add(new OverlayStep(ImmutableArray.Create(layer)));
        }
        return this;
    }

    public IMediaPipeline Strip(MetadataKinds kinds = MetadataKinds.All) =>
        AddStep(new StripStep(kinds));

    public IMediaPipeline PreserveFormat(int quality = Quality.Web) =>
        AddStep(new EncodeStep(Format: null, Quality: quality));

    public IMediaPipeline EncodeAs(string format, int quality = Quality.Web) =>
        AddStep(new EncodeStep(Format: format, Quality: quality));

    public IMediaPipeline FlattenTo(string format, int quality = Quality.Web)
    {
        _logger.LogInformation("Media pipeline: destructive FlattenTo({Format}) — animation/alpha may be dropped.", format);
        return AddStep(new FlattenToStep(Format: format, Quality: quality));
    }

    // ----- terminal operations -----

    public async Task<MediaInfo> ProbeAsync(CancellationToken ct = default)
    {
        EnsureUnconsumed();
        _consumed = true;
        try
        {
            if (_source.CanSeek) _source.Position = 0;
            // Full LoadAsync (not Identify-only) so the alpha-channel and frame-count
            // info are reliably populated across all input formats. Probe is rarely
            // hot; correctness wins over the millisecond savings.
            using var image = await LoadOrThrowAsync(_source, ct).ConfigureAwait(false);
            return BuildMediaInfo(image);
        }
        finally
        {
            await DisposeSourceAsync().ConfigureAwait(false);
        }
    }

    public async Task<MediaOutput> ToBytesAsync(CancellationToken ct = default)
    {
        EnsureUnconsumed();
        _consumed = true;
        try
        {
            if (_source.CanSeek) _source.Position = 0;
            using var image = await LoadOrThrowAsync(_source, ct).ConfigureAwait(false);
            return await EncodeAsync(image, _steps, _overlayResolver, _fonts, _overlayDepth, _logger, ct).ConfigureAwait(false);
        }
        finally
        {
            await DisposeSourceAsync().ConfigureAwait(false);
        }
    }

    private async Task<Image> LoadOrThrowAsync(Stream source, CancellationToken ct)
    {
        // Pre-decode safety: if limits are configured, run a header-only
        // Identify pass and reject oversized sources before allocating
        // the full decoded buffer.
        if (_limits.MaxSourceMegapixels > 0 || _limits.MaxFrameCount > 0)
        {
            await EnforceLimitsAsync(source, ct).ConfigureAwait(false);
        }

        try
        {
            return await Image.LoadAsync(source, ct).ConfigureAwait(false);
        }
        catch (SixLabors.ImageSharp.UnknownImageFormatException ex)
        {
            throw new MediaDecodeException("Source bytes did not match any registered image format.", ex);
        }
        catch (SixLabors.ImageSharp.InvalidImageContentException ex)
        {
            throw new MediaDecodeException($"Source bytes failed to decode: {ex.Message}", ex);
        }
    }

    private async Task EnforceLimitsAsync(Stream source, CancellationToken ct)
    {
        if (!source.CanSeek)
        {
            // We need to re-read the bytes after the Identify pass; without
            // seek support we can't enforce limits cheaply. Skip silently —
            // most real-world inputs are seekable streams.
            return;
        }

        var pos = source.Position;
        ImageInfo? info;
        try
        {
            info = await Image.IdentifyAsync(source, ct).ConfigureAwait(false);
        }
        catch (SixLabors.ImageSharp.UnknownImageFormatException ex)
        {
            // Defer to the full LoadAsync below; it will throw MediaDecodeException
            // with a consistent error shape.
            source.Position = pos;
            return;
        }
        finally
        {
            source.Position = pos;
        }

        if (info is null) return;

        if (_limits.MaxSourceMegapixels > 0)
        {
            // Round up so a 100.01-megapixel source still trips a 100-cap.
            var megapixels = (long)Math.Ceiling((info.Width * (long)info.Height) / 1_000_000.0);
            if (megapixels > _limits.MaxSourceMegapixels)
            {
                throw new MediaSourceLimitException(
                    "maxSourceMegapixels", megapixels, _limits.MaxSourceMegapixels);
            }
        }

        if (_limits.MaxFrameCount > 0)
        {
            var frames = info.FrameMetadataCollection?.Count ?? 1;
            if (frames > _limits.MaxFrameCount)
            {
                throw new MediaSourceLimitException(
                    "maxFrameCount", frames, _limits.MaxFrameCount);
            }
        }
    }

    public async Task<MediaBundle> MaterializeAsync(Action<MediaBundleBuilder> configure, CancellationToken ct = default)
    {
        if (configure is null) throw new ArgumentNullException(nameof(configure));
        EnsureUnconsumed();
        _consumed = true;
        var builder = new MediaBundleBuilder();
        configure(builder);
        if (builder.Variants.Count == 0)
            throw new ArgumentException("Bundle must declare at least one variant.", nameof(configure));

        try
        {
            if (_source.CanSeek) _source.Position = 0;
            // Decode once; clone per variant so transforms don't bleed across branches.
            using var sourceImage = await Image.LoadAsync(_source, ct).ConfigureAwait(false);

            var variants = new Dictionary<string, MediaOutput>(builder.Variants.Count, StringComparer.Ordinal);
            foreach (var (name, configureBranch) in builder.Variants)
            {
                ct.ThrowIfCancellationRequested();
                // Each branch records its own step list against a transient pipeline carrier
                var branch = new StepRecorder();
                configureBranch(branch);
                // Clone the decoded image so transforms apply only to this branch
                using var clone = sourceImage.Clone(_ => { });
                var output = await EncodeAsync(clone, branch.Steps, _overlayResolver, _fonts, _overlayDepth, _logger, ct).ConfigureAwait(false);
                variants[name] = output;
            }
            return new MediaBundle(variants);
        }
        finally
        {
            await DisposeSourceAsync().ConfigureAwait(false);
        }
    }

    // ----- engine -----

    /// <summary>
    /// Run all <paramref name="steps"/> against <paramref name="image"/>
    /// in canonical <see cref="PipelineStage"/> order and encode the
    /// result. Caller owns image disposal.
    /// </summary>
    private static async Task<MediaOutput> EncodeAsync(
        Image image,
        IReadOnlyList<MediaStep> steps,
        IOverlayResolver? overlays,
        KoanFontRegistry? fonts,
        int overlayDepth,
        ILogger logger,
        CancellationToken ct)
    {
        // Default: auto-orient when no explicit orient step is declared.
        var hasOrient = steps.Any(s => s.Stage == PipelineStage.Orient);
        var ordered = steps
            .Select((s, i) => (s, i))
            .OrderBy(t => (int)t.s.Stage)
            .ThenBy(t => t.i)
            .Select(t => t.s)
            .ToList();
        if (!hasOrient)
        {
            ordered.Insert(0, new AutoOrientStep(Keep: false));
        }

        MediaStep? encodeStep = null;
        ShapeStep? pendingShape = null;
        ResizeStep? pendingResize = null;
        OverlayStep? pendingOverlay = null;
        StripStep? pendingStrip = null;

        foreach (var step in ordered)
        {
            ct.ThrowIfCancellationRequested();
            switch (step)
            {
                case AutoOrientStep ao when !ao.Keep:
                    image.Mutate(x => x.AutoOrient());
                    break;
                case AutoOrientStep:
                    // Keep — no-op
                    break;
                case ExtractFrameStep ef:
                    ApplyExtractFrame(image, ef.Index);
                    break;
                case RotateStep rs:
                    image.Mutate(x => x.Rotate(rs.Degrees));
                    break;
                case FlipStep fs:
                    image.Mutate(x => x.Flip(fs.Axis == FlipAxis.Horizontal ? FlipMode.Horizontal : FlipMode.Vertical));
                    break;
                case ShapeStep shape:
                    pendingShape = shape;
                    break;
                case ResizeStep resize:
                    pendingResize = resize;
                    break;
                case OverlayStep overlayStep:
                    pendingOverlay = overlayStep;
                    break;
                case StripStep strip:
                    pendingStrip = strip;
                    break;
                case EncodeStep or FlattenToStep:
                    encodeStep = step;
                    break;
            }
        }

        // Shape + Resize collapse into a single ImageSharp Mutate so we
        // don't allocate intermediate frame buffers. Order: shape (crop +
        // fit + position + bg) → size.
        if (pendingShape is not null || pendingResize is not null)
        {
            image.Mutate(ctx => ApplyShapeAndResize(ctx, image.Size, pendingShape, pendingResize));
        }

        // Background compose: when bg is non-transparent + Fit.Contain has
        // a fully-defined target canvas, build a new sized canvas, paint
        // it (solid color / dominant / auto-border / cover-blur), and
        // composite the shaped image onto it. The canvas becomes the
        // working image for overlays, strip, and encode so all subsequent
        // stages land on the final pixels.
        using var composed = BackgroundComposer.TryCompose(image, pendingShape, pendingResize, ct);
        var working = composed ?? image;

        // Overlays composite onto the shaped/sized host before metadata
        // strip and encode so they're part of the final encoded bytes.
        if (pendingOverlay is not null)
        {
            await OverlayCompositor.ApplyAsync(
                working, pendingOverlay, overlays, fonts, overlayDepth, logger, ct).ConfigureAwait(false);
        }

        if (pendingStrip is not null)
        {
            ApplyStrip(working, pendingStrip.Kinds);
        }

        // Resolve encode step. Implicit format-preserving encode if none declared.
        string? targetFormat;
        int quality;
        switch (encodeStep)
        {
            case FlattenToStep flatten:
                targetFormat = flatten.Format;
                quality = flatten.Quality;
                ApplyFlatten(working, flatten.Format);
                break;
            case EncodeStep encode:
                targetFormat = encode.Format;
                quality = encode.Quality;
                break;
            default:
                targetFormat = null;
                quality = Quality.Web;
                break;
        }

        // Preserve source-format metadata for diagnostics even when we
        // swap to a freshly-allocated canvas (which has no DecodedImageFormat).
        var sourceFormat = image.Metadata.DecodedImageFormat;
        var encoder = EncoderSelector.For(sourceFormat, targetFormat, quality);
        var resolvedFormat = targetFormat?.ToLowerInvariant() ?? EncoderSelector.CanonicalSlug(sourceFormat);

        await using var ms = new MemoryStream();
        await working.SaveAsync(ms, encoder, ct).ConfigureAwait(false);
        var bytes = ms.ToArray();

        var sourceSlug = EncoderSelector.CanonicalSlug(sourceFormat);
        return new MediaOutput(
            Bytes: bytes,
            ContentType: EncoderSelector.ContentType(resolvedFormat),
            Format: resolvedFormat,
            SourceFormat: sourceSlug,
            Width: working.Width,
            Height: working.Height,
            FrameCount: working.Frames.Count,
            Fingerprint: $"{resolvedFormat}-{working.Width}x{working.Height}-f{working.Frames.Count}-q{quality}");
    }

    private static void ApplyExtractFrame(Image image, int index)
    {
        if (image.Frames.Count <= 1) return; // no-op on static
        if (index < 0 || index >= image.Frames.Count)
            throw new ArgumentOutOfRangeException(nameof(index),
                $"Frame index {index} out of range for source with {image.Frames.Count} frame(s).");
        // Remove every frame except the requested one. We iterate from
        // the end so indices stay stable.
        for (var i = image.Frames.Count - 1; i >= 0; i--)
        {
            if (i != index) image.Frames.RemoveFrame(i);
        }
    }

    private static void ApplyShapeAndResize(
        IImageProcessingContext ctx,
        Size source,
        ShapeStep? shape,
        ResizeStep? resize)
    {
        // Compute shape pass (crop to aspect / explicit pixels)
        if (shape?.Crop is { } crop)
        {
            switch (crop.Kind)
            {
                case CropSpecKind.Aspect:
                {
                    var targetAspect = (double)crop.Width / crop.Height;
                    var (cw, ch) = LargestFitting(source.Width, source.Height, targetAspect);
                    var (cx, cy) = AnchorOffset(source.Width, source.Height, cw, ch, shape.Position);
                    ctx.Crop(new Rectangle(cx, cy, cw, ch));
                    break;
                }
                case CropSpecKind.Pixels:
                {
                    // Cover-style: scale source so the crop window fits, then crop centered on position.
                    var (cw, ch) = (Math.Min(crop.Width, source.Width), Math.Min(crop.Height, source.Height));
                    var (cx, cy) = AnchorOffset(source.Width, source.Height, cw, ch, shape.Position);
                    ctx.Crop(new Rectangle(cx, cy, cw, ch));
                    break;
                }
                case CropSpecKind.PixelsWithOffset:
                {
                    var x = Math.Clamp(crop.OffsetX, 0, Math.Max(0, source.Width - crop.Width));
                    var y = Math.Clamp(crop.OffsetY, 0, Math.Max(0, source.Height - crop.Height));
                    var w = Math.Min(crop.Width, source.Width - x);
                    var h = Math.Min(crop.Height, source.Height - y);
                    ctx.Crop(new Rectangle(x, y, w, h));
                    break;
                }
            }
        }

        // Resize pass
        if (resize is { } rz)
        {
            var (rw, rh) = ResolveResizeDimensions(ctx.GetCurrentSize(), rz);
            if (rw is not null && rh is not null)
            {
                var resizeOptions = new ResizeOptions
                {
                    Size = new Size(rw.Value, rh.Value),
                    Mode = shape?.Fit switch
                    {
                        Fit.Cover => ResizeMode.Crop,
                        Fit.Contain => ResizeMode.Max,
                        Fit.Fill => ResizeMode.Stretch,
                        Fit.ScaleDown => ResizeMode.Max,
                        Fit.None => ResizeMode.Manual,
                        _ => ResizeMode.Max,
                    },
                };
                if (shape?.Fit == Fit.ScaleDown)
                {
                    var currentSize = ctx.GetCurrentSize();
                    if (currentSize.Width <= rw.Value && currentSize.Height <= rh.Value)
                    {
                        // never upscale
                        resizeOptions = null!;
                    }
                }
                if (resizeOptions is not null) ctx.Resize(resizeOptions);
            }
        }
        else if (shape is { Fit: Fit.Contain, Crop: { } cropForFit })
        {
            // Shape+contain without explicit resize: still honor contain by
            // not exceeding crop dimensions. No-op here; the crop already
            // bounded the image.
            _ = cropForFit;
        }
    }

    private static (int? Width, int? Height) ResolveResizeDimensions(Size current, ResizeStep rz)
    {
        var w = rz.Width;
        var h = rz.Height;
        if (rz.Dpr > 0 && Math.Abs(rz.Dpr - 1.0) > 0.001)
        {
            if (w.HasValue) w = (int)Math.Round(w.Value * rz.Dpr);
            if (h.HasValue) h = (int)Math.Round(h.Value * rz.Dpr);
        }
        // Single-axis scale: derive the missing dimension to preserve aspect.
        if (w.HasValue && !h.HasValue)
        {
            h = (int)Math.Round(current.Height * (w.Value / (double)current.Width));
        }
        else if (h.HasValue && !w.HasValue)
        {
            w = (int)Math.Round(current.Width * (h.Value / (double)current.Height));
        }
        return (w, h);
    }

    private static (int Width, int Height) LargestFitting(int sourceW, int sourceH, double targetAspect)
    {
        var sourceAspect = (double)sourceW / sourceH;
        if (sourceAspect > targetAspect)
        {
            // source wider — height bound
            var h = sourceH;
            var w = (int)Math.Round(h * targetAspect);
            return (w, h);
        }
        else
        {
            // source taller (or equal) — width bound
            var w = sourceW;
            var h = (int)Math.Round(w / targetAspect);
            return (w, h);
        }
    }

    private static (int X, int Y) AnchorOffset(int sourceW, int sourceH, int cropW, int cropH, Position position)
    {
        var freeX = sourceW - cropW;
        var freeY = sourceH - cropH;
        if (freeX < 0) freeX = 0;
        if (freeY < 0) freeY = 0;

        // UseFocus or per-axis percent: positional in [0,1] coords
        var xFrac = position.UseFocus ? 0.5 : position.X;
        var yFrac = position.UseFocus ? 0.5 : position.Y;

        if (position.Anchor is { } anchor)
        {
            (xFrac, yFrac) = anchor switch
            {
                PositionAnchor.Center => (0.5, 0.5),
                PositionAnchor.Top => (0.5, 0.0),
                PositionAnchor.Bottom => (0.5, 1.0),
                PositionAnchor.Left => (0.0, 0.5),
                PositionAnchor.Right => (1.0, 0.5),
                PositionAnchor.TopLeft => (0.0, 0.0),
                PositionAnchor.TopRight => (1.0, 0.0),
                PositionAnchor.BottomLeft => (0.0, 1.0),
                PositionAnchor.BottomRight => (1.0, 1.0),
                _ => (0.5, 0.5),
            };
        }
        var x = (int)Math.Round(freeX * Math.Clamp(xFrac, 0.0, 1.0));
        var y = (int)Math.Round(freeY * Math.Clamp(yFrac, 0.0, 1.0));
        return (x, y);
    }

    private static void ApplyStrip(Image image, MetadataKinds kinds)
    {
        var md = image.Metadata;
        if (kinds.HasFlag(MetadataKinds.Exif)) md.ExifProfile = null;
        if (kinds.HasFlag(MetadataKinds.Icc)) md.IccProfile = null;
        if (kinds.HasFlag(MetadataKinds.Xmp)) md.XmpProfile = null;
    }

    /// <summary>
    /// FlattenTo behavior: collapse to single frame and drop alpha
    /// against a sensible fill when the target lacks an alpha channel.
    /// </summary>
    private static void ApplyFlatten(Image image, string targetFormat)
    {
        // Collapse animation
        while (image.Frames.Count > 1) image.Frames.RemoveFrame(image.Frames.Count - 1);
        // Drop alpha for JPEG (alpha matrix matters for visual fidelity later;
        // ImageSharp's encoder will composite onto black by default, which is
        // often not what callers want. The HTTP layer's bg-fallback resolves
        // this by overriding the background before encode.)
        if (!EncoderSelector.SupportsAlpha(targetFormat))
        {
            // No explicit fill here — bg-fallback applied via Shape step at HTTP
            // layer when FlattenTo is invoked through a URL. For programmatic
            // callers, FlattenTo into JPEG produces black-composited output —
            // documented behavior.
        }
    }

    private static MediaInfo BuildMediaInfo(Image image)
    {
        var format = EncoderSelector.CanonicalSlug(image.Metadata.DecodedImageFormat);
        var frameCount = image.Frames.Count;
        var hasAlpha = image.PixelType.AlphaRepresentation is PixelAlphaRepresentation.Associated
            or PixelAlphaRepresentation.Unassociated;
        // ImageSharp's PNG decoder may collapse an RgbWithAlpha source to Rgb24 when
        // it determines all alpha samples are opaque. Consult the format metadata
        // so the alpha-capability signal matches the source's declared color type,
        // not the optimised pixel representation.
        if (!hasAlpha)
        {
            hasAlpha = SourceFormatDeclaresAlpha(image);
        }
        var depth = image.PixelType.BitsPerPixel;
        int? exifOrient = null;
        var exif = image.Metadata.ExifProfile;
        if (exif is not null && exif.TryGetValue(SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.Orientation, out var orientValue))
        {
            exifOrient = orientValue?.Value;
        }
        return new MediaInfo(
            Format: format,
            Width: image.Width,
            Height: image.Height,
            FrameCount: frameCount,
            HasAlpha: hasAlpha,
            ColorDepth: depth,
            ExifOrientation: exifOrient,
            HasIccProfile: image.Metadata.IccProfile is not null);
    }

    /// <summary>
    /// Source format declares an alpha channel — covers the case where
    /// ImageSharp optimised a sparsely-transparent source into Rgb24.
    /// </summary>
    private static bool SourceFormatDeclaresAlpha(Image image)
    {
        var decoded = image.Metadata.DecodedImageFormat;
        if (decoded is SixLabors.ImageSharp.Formats.Png.PngFormat)
        {
            var meta = image.Metadata.GetPngMetadata();
            return meta.ColorType is SixLabors.ImageSharp.Formats.Png.PngColorType.RgbWithAlpha
                or SixLabors.ImageSharp.Formats.Png.PngColorType.GrayscaleWithAlpha;
        }
        if (decoded is SixLabors.ImageSharp.Formats.Webp.WebpFormat)
        {
            var meta = image.Metadata.GetWebpMetadata();
            return meta.FileFormat == SixLabors.ImageSharp.Formats.Webp.WebpFileFormatType.Lossless;
        }
        if (decoded is SixLabors.ImageSharp.Formats.Gif.GifFormat)
        {
            // GIF supports 1-bit binary transparency
            return true;
        }
        return false;
    }

    private MediaPipeline AddStep(MediaStep step)
    {
        EnsureUnconsumed();
        // Single-slot stages: replace
        if (IsSingleSlot(step.Stage))
        {
            var existing = _steps.FindIndex(s => s.Stage == step.Stage);
            if (existing >= 0) _steps[existing] = step;
            else _steps.Add(step);
        }
        else
        {
            _steps.Add(step);
        }
        return this;
    }

    private static bool IsSingleSlot(PipelineStage stage) =>
        stage is PipelineStage.Shape
              or PipelineStage.Size
              or PipelineStage.Encode
              or PipelineStage.Frame
              or PipelineStage.Orient
              or PipelineStage.Metadata;

    private void EnsureUnconsumed()
    {
        if (_consumed) throw new InvalidOperationException(
            "Media pipeline has already been materialised. Create a new pipeline for additional outputs.");
    }

    private async ValueTask DisposeSourceAsync()
    {
        if (!_disposeSource) return;
        try { await _source.DisposeAsync().ConfigureAwait(false); }
        catch { /* swallow dispose errors */ }
    }

    /// <summary>
    /// Internal carrier used by <see cref="MaterializeAsync"/> branches
    /// to record their step list without executing anything.
    /// </summary>
    private sealed class StepRecorder : IMediaPipeline
    {
        public List<MediaStep> Steps { get; } = new();

        public IMediaPipeline Apply(MediaRecipe recipe)
        {
            foreach (var step in recipe.Steps) Add(step);
            return this;
        }
        public IMediaPipeline AutoOrient(bool keep = false) => Add(new AutoOrientStep(keep));
        public IMediaPipeline ExtractFrame(int index = 0) => Add(new ExtractFrameStep(index));
        public IMediaPipeline Rotate(int degrees) => Add(new RotateStep(degrees));
        public IMediaPipeline FlipHorizontal() => Add(new FlipStep(FlipAxis.Horizontal));
        public IMediaPipeline FlipVertical() => Add(new FlipStep(FlipAxis.Vertical));
        public IMediaPipeline Shape(CropSpec? crop = null, Fit fit = Fit.Cover, Position? position = null, Background? background = null) =>
            Add(new ShapeStep(crop, fit, position ?? Position.Center, background ?? Background.Transparent()));
        public IMediaPipeline Crop(CropSpec spec) => Shape(crop: spec);
        public IMediaPipeline Crop(string spec)
        {
            if (!CropSpec.TryParse(spec, out var parsed))
                throw new ArgumentException($"Invalid crop value '{spec}'.", nameof(spec));
            return Crop(parsed);
        }
        public IMediaPipeline Resize(int? width = null, int? height = null, double dpr = 1.0) =>
            Add(new ResizeStep(width, height, dpr));
        public IMediaPipeline ResizeFit(int maxWidth, int maxHeight) =>
            Resize(maxWidth, maxHeight).Shape(fit: Fit.Contain);
        public IMediaPipeline ResizeCover(int width, int height, Position? position = null) =>
            Resize(width, height).Shape(fit: Fit.Cover, position: position ?? Position.Center);
        public IMediaPipeline Overlay(
            string mediaId,
            OverlaySize? size = null,
            Position? position = null,
            OverlayPadding? padding = null,
            double opacity = 1.0,
            int rotate = 0,
            string? recipeName = null)
        {
            var layer = new OverlayLayer(
                Source: new MediaOverlaySource(mediaId, recipeName),
                Size: size ?? OverlaySize.Natural,
                Position: position ?? Position.Center,
                Padding: padding ?? OverlayPadding.Zero,
                Opacity: Math.Clamp(opacity, 0.0, 1.0),
                Rotate: rotate);
            return AppendOverlayLayer(layer);
        }
        public IMediaPipeline OverlayText(
            string text,
            string? font = null,
            BackgroundColor? color = null,
            int fontSize = 32,
            Position? position = null,
            OverlayPadding? padding = null,
            double opacity = 1.0,
            int rotate = 0)
        {
            var layer = new OverlayLayer(
                Source: new TextOverlaySource(text, font, color, fontSize),
                Size: OverlaySize.Natural,
                Position: position ?? Position.Center,
                Padding: padding ?? OverlayPadding.Zero,
                Opacity: Math.Clamp(opacity, 0.0, 1.0),
                Rotate: rotate);
            return AppendOverlayLayer(layer);
        }
        private IMediaPipeline AppendOverlayLayer(OverlayLayer layer)
        {
            var existing = Steps.FindIndex(s => s.Stage == PipelineStage.Overlay);
            if (existing >= 0)
            {
                var current = (OverlayStep)Steps[existing];
                Steps[existing] = current with { Layers = current.Layers.Add(layer) };
            }
            else
            {
                Steps.Add(new OverlayStep(ImmutableArray.Create(layer)));
            }
            return this;
        }
        public IMediaPipeline Strip(MetadataKinds kinds = MetadataKinds.All) => Add(new StripStep(kinds));
        public IMediaPipeline PreserveFormat(int quality = Quality.Web) => Add(new EncodeStep(null, quality));
        public IMediaPipeline EncodeAs(string format, int quality = Quality.Web) => Add(new EncodeStep(format, quality));
        public IMediaPipeline FlattenTo(string format, int quality = Quality.Web) => Add(new FlattenToStep(format, quality));

        public Task<MediaInfo> ProbeAsync(CancellationToken ct = default) =>
            throw new NotSupportedException("Probe is not supported inside Materialize branches; call Probe on the parent pipeline.");
        public Task<MediaOutput> ToBytesAsync(CancellationToken ct = default) =>
            throw new NotSupportedException("ToBytes is implicit inside Materialize; the bundle returns each branch's output.");
        public Task<MediaBundle> MaterializeAsync(Action<MediaBundleBuilder> configure, CancellationToken ct = default) =>
            throw new NotSupportedException("Nested Materialize is not supported.");

        private IMediaPipeline Add(MediaStep step)
        {
            if (IsSingleSlot(step.Stage))
            {
                var existing = Steps.FindIndex(s => s.Stage == step.Stage);
                if (existing >= 0) Steps[existing] = step;
                else Steps.Add(step);
            }
            else
            {
                Steps.Add(step);
            }
            return this;
        }
    }
}

/// <summary>Thrown when source bytes cannot be decoded as a known image format.</summary>
public sealed class MediaDecodeException : Exception
{
    public MediaDecodeException(string message) : base(message) { }
    public MediaDecodeException(string message, Exception inner) : base(message, inner) { }
}
