using System.Collections.Immutable;
using System.Xml;
using Koan.Media.Abstractions.Recipes;
using Koan.Media.Core.Fonts;
using Koan.Media.Core.Formats;
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
///   <item>ImageSharp's <c>Image</c> already applies <c>Mutate(IImageProcessingContext)</c> to all frames, so animation survives resize/crop/rotate transparently</item>
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

    [Obsolete("Use Sample(FrameSelector.Index(n)) or the Sample.Frame(n) factory.")]
    public IMediaPipeline ExtractFrame(int index = 0) =>
        // Delegate to Sample so all kind-tracking and fingerprint emission
        // route through the canonical MEDIA-0005 vocabulary.
        AddStep(new SampleStep(new FrameSelector.Index(index)));

    public IMediaPipeline Sample(FrameSelector selector)
    {
        if (selector is null) throw new ArgumentNullException(nameof(selector));
        return AddStep(new SampleStep(selector));
    }

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
            // MEDIA-0006: SVG pre-decode branch. The header sniff is the
            // first thing every terminal does — Image.LoadAsync throws on
            // SVG, and Content-Type / extension cannot be trusted.
            var svgBytes = await TryReadSvgBytesAsync(_source, ct).ConfigureAwait(false);
            if (svgBytes is not null)
            {
                SvgValidator.ValidateOrThrow(svgBytes);
                return BuildSvgMediaInfo(svgBytes);
            }

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

    [Obsolete("Use WriteToAsync(Stream, CancellationToken) to stream directly into the response or storage. See MEDIA-0008.", error: false)]
    public async Task<MediaOutput> ToBytesAsync(CancellationToken ct = default)
    {
        // Buffered legacy path: drive the streaming terminal into a
        // MemoryStream and replay the captured bytes through the
        // returned MediaOutput. Per MEDIA-0008 §b, this preserves
        // backward-compat: callers reading output.Bytes still get the
        // full encoded buffer.
        using var ms = new MemoryStream();
        var output = await WriteToAsync(ms, ct).ConfigureAwait(false);
        var bytes = ms.ToArray();
        // Detach any disposable render resources — the bytes are
        // captured, the decoded image is no longer needed.
        if (output.RenderResources is { } owned)
        {
            await owned.DisposeAsync().ConfigureAwait(false);
        }
#pragma warning disable CS0618 // Buffered legacy path: surfaces Bytes by contract.
        return output with
        {
            Bytes = bytes,
            WriteToAsync = (dest, dct) => dest.WriteAsync(bytes, dct).AsTask(),
            RenderResources = null,
        };
#pragma warning restore CS0618
    }

    /// <summary>
    /// Streaming terminal. Decodes the source, plans the pipeline, runs
    /// the mutating stages, and encodes directly into
    /// <paramref name="destination"/> via ImageSharp's
    /// <see cref="Image.SaveAsync(Stream, IImageEncoder, CancellationToken)"/>.
    /// No <see cref="MemoryStream"/> sits between the encoder and the
    /// destination — animated and high-resolution encodes stream
    /// through without buffering the full output. Per MEDIA-0008.
    /// </summary>
    public async Task<MediaOutput> WriteToAsync(Stream destination, CancellationToken ct = default)
    {
        if (destination is null) throw new ArgumentNullException(nameof(destination));
        EnsureUnconsumed();
        _consumed = true;
        try
        {
            // MEDIA-0006: SVG pre-decode branch. Validate, plan against a
            // synthetic Vector probe, rasterize at the planner's forward-
            // derived target, and hand a PNG MemoryStream off to the
            // existing ImageSharp encode chain. The raw SVG is the source
            // of truth in storage; rasterization only fires when a recipe
            // demands a Raster target. Per MEDIA-0008 §d the SVG path
            // remains buffered to a MemoryStream for the intermediate
            // Skia-produced PNG, then streams the final raster encode.
            var svgBytes = await TryReadSvgBytesAsync(_source, ct).ConfigureAwait(false);
            if (svgBytes is not null)
            {
                return await StreamEncodeSvgAsync(svgBytes, destination, ct).ConfigureAwait(false);
            }

            if (_source.CanSeek) _source.Position = 0;
            var image = await LoadOrThrowAsync(_source, ct).ConfigureAwait(false);
            try
            {
                // MEDIA-0005: plan before execute. The planner is a pure
                // function over (probe, steps, encoderAccepts); a failing plan
                // throws synchronously and never reaches the encode pass.
                var probe = BuildMediaInfo(image);
                var plan = PlanOrThrow(probe, _steps);
                return await StreamEncodeAsync(
                    image, _steps, _overlayResolver, _fonts, _overlayDepth, _logger, plan, destination, ct).ConfigureAwait(false);
            }
            finally
            {
                image.Dispose();
            }
        }
        finally
        {
            await DisposeSourceAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// MEDIA-0006 + MEDIA-0008 streaming SVG terminal: validate, plan,
    /// rasterize, then stream the recipe's terminal raster encode
    /// directly into <paramref name="destination"/>. The intermediate
    /// PNG bytes flow through a <see cref="MemoryStream"/> per MEDIA-0008
    /// §d (Svg.Skia is buffered by nature; the streaming win comes from
    /// the final encode).
    /// </summary>
    private async Task<MediaOutput> StreamEncodeSvgAsync(byte[] svgBytes, Stream destination, CancellationToken ct)
    {
        SvgValidator.ValidateOrThrow(svgBytes);

        var probe = BuildSvgMediaInfo(svgBytes);
        var plan = PlanOrThrow(probe, _steps);

        var (targetW, targetH) = ResolveRasterizeTarget(plan, probe);
        var pngBytes = SvgRasterizer.RenderToPng(svgBytes, targetW, targetH);

        // Strip the implicit Rasterize step before re-entering the raster
        // pipeline — the rasterizer has already produced bytes at the
        // planner's forward-derived target. Author steps (Resize, Shape,
        // Encode) flow through unchanged.
        using var rasterized = new MemoryStream(pngBytes, writable: false);
        var image = await Image.LoadAsync(rasterized, ct).ConfigureAwait(false);
        try
        {
            // Re-plan against the raster-sided probe so KindTrace records the
            // full Vector -> Raster -> Encode transition.
            var rasterProbe = BuildMediaInfo(image);
            var rasterPlan = PlanOrThrow(rasterProbe, _steps);
            var output = await StreamEncodeAsync(
                image, _steps, _overlayResolver, _fonts, _overlayDepth, _logger, rasterPlan, destination, ct).ConfigureAwait(false);
            return output with
            {
                SourceFormat = SvgFormat.Slug,
                KindTrace = plan.KindTrace,
            };
        }
        finally
        {
            image.Dispose();
        }
    }

    private static (int Width, int Height) ResolveRasterizeTarget(PlanResult plan, MediaInfo svgProbe)
    {
        // Planner forward-derives an implicit Rasterize at the encoder
        // boundary when a Vector reaches a non-Vector encoder. Its
        // ResolvedParams carry the target dimensions.
        foreach (var step in plan.Steps)
        {
            if (step.Implicit
                && step.OutputKind == MediaKind.Raster
                && step.ResolvedParams is { } resolved
                && resolved.TryGetValue("targetWidth", out var rawW)
                && resolved.TryGetValue("targetHeight", out var rawH))
            {
                var w = Convert.ToInt32(rawW, System.Globalization.CultureInfo.InvariantCulture);
                var h = Convert.ToInt32(rawH, System.Globalization.CultureInfo.InvariantCulture);
                if (w > 0 && h > 0) return (w, h);
            }
        }
        // No implicit Rasterize — the recipe has no terminal raster encoder
        // (e.g. a future SVG-out pipeline) or the planner did not size.
        // Fall back to the SVG's intrinsic viewBox dimensions.
        if (svgProbe.Width > 0 && svgProbe.Height > 0)
        {
            return (svgProbe.Width, svgProbe.Height);
        }
        throw new SvgRasterizationException(
            "Cannot determine rasterization target: no sizing step and SVG has no usable intrinsic dimensions.");
    }

    /// <summary>
    /// Run the planner against the given probe and step list. Throws
    /// <see cref="MediaPipelineKindMismatchException"/> on failure;
    /// returns the plan on success. Per MEDIA-0005 §5 strict gate.
    /// </summary>
    private static PlanResult PlanOrThrow(MediaInfo probe, IReadOnlyList<MediaStep> steps)
    {
        var encoderAccepts = ResolveTerminalEncoderAccepts(steps, probe.Format);
        var plan = MediaPipelinePlanner.Plan(probe, steps, encoderAccepts);
        if (!plan.Ok)
        {
            var err = plan.Error!;
            throw new MediaPipelineKindMismatchException(
                stepIndex: err.StepIndex,
                expectedKinds: err.ExpectedKinds,
                gotKind: err.GotKind,
                suggestion: err.Suggestion);
        }
        return plan;
    }

    /// <summary>
    /// Resolve the terminal encoder's admission set. The terminal step
    /// is the last <see cref="EncodeStep"/> / <see cref="FlattenToStep"/>;
    /// when absent or null-format (preserve source), fall back to the
    /// source's own format slug.
    /// </summary>
    private static KindSet ResolveTerminalEncoderAccepts(IReadOnlyList<MediaStep> steps, string sourceFormatSlug)
    {
        string? targetSlug = null;
        for (var i = steps.Count - 1; i >= 0; i--)
        {
            switch (steps[i])
            {
                case FlattenToStep flatten:
                    targetSlug = flatten.Format;
                    i = -1;
                    break;
                case EncodeStep encode:
                    targetSlug = encode.Format ?? sourceFormatSlug;
                    i = -1;
                    break;
            }
        }
        targetSlug ??= sourceFormatSlug;
        var accepts = EncoderAccepts.AcceptsFor(targetSlug);
        // Unregistered encoders fall through with KindSet.None; if we got
        // None for an unknown slug, leave it null so the planner doesn't
        // refuse the source kind against an empty set (the existing
        // EncoderSelector.For will throw NotSupportedException downstream).
        return accepts.IsEmpty ? KindSet.All : accepts;
    }

    /// <summary>
    /// Peek the first <see cref="SvgFormat.HeaderSniffBytes"/> bytes of the
    /// source and, if the prefix matches the SVG sniff, return the entire
    /// payload as a byte array. Returns null when the stream is not SVG
    /// (the source is rewound for the existing ImageSharp path). The
    /// returned bytes are subject to <see cref="SvgValidator.MaxSourceBytes"/>;
    /// payloads beyond the cap are still read, then rejected at validation
    /// time so the failure mode is one validator, one allowlist.
    /// </summary>
    private static async Task<byte[]?> TryReadSvgBytesAsync(Stream source, CancellationToken ct)
    {
        if (source is null) return null;

        if (!source.CanSeek)
        {
            // Without seek we cannot rewind for the non-SVG fallback;
            // route the entire stream through a buffered MemoryStream so
            // the sniff can read freely. This is the legacy-stream slow
            // path; CanSeek streams (the common case) take the cheap one.
            // Note: callers that hand us non-seekable streams will incur
            // a full copy here, but the overall cost is dominated by the
            // decoder anyway.
            return await BufferIfSvgAsync(source, ct).ConfigureAwait(false);
        }

        var origin = source.Position;
        var header = new byte[SvgFormat.HeaderSniffBytes];
        var read = await source.ReadAtLeastAsync(header, SvgFormat.HeaderSniffBytes, throwOnEndOfStream: false, ct).ConfigureAwait(false);
        source.Position = origin;
        if (!SvgFormat.MatchHeader(header.AsSpan(0, read)))
        {
            return null;
        }

        // SVG confirmed — read the full payload. We buffer here so the
        // raster fallback can replace the stream with PNG bytes without
        // touching the original source again.
        using var ms = new MemoryStream();
        await source.CopyToAsync(ms, ct).ConfigureAwait(false);
        return ms.ToArray();
    }

    private static async Task<byte[]?> BufferIfSvgAsync(Stream source, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await source.CopyToAsync(ms, ct).ConfigureAwait(false);
        var bytes = ms.ToArray();
        return SvgFormat.MatchHeader(bytes.AsSpan(0, Math.Min(bytes.Length, SvgFormat.HeaderSniffBytes)))
            ? bytes
            : null;
    }

    /// <summary>
    /// Build a Vector-kind <see cref="MediaInfo"/> from the SVG document
    /// head — viewBox preferred, width/height fallback. Per MEDIA-0006
    /// §Decision.1.
    /// </summary>
    private static MediaInfo BuildSvgMediaInfo(byte[] svgBytes)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = SvgValidator.MaxCharactersInDocument,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            CloseInput = true,
        };

        int width = 0;
        int height = 0;
        try
        {
            using var stream = new MemoryStream(svgBytes, writable: false);
            using var reader = XmlReader.Create(stream, settings);
            (width, height) = SvgFormat.ReadViewBoxOrIntrinsicSize(reader);
        }
        catch (XmlException ex)
        {
            throw new SvgValidationException(
                $"SVG is not well-formed XML: {ex.Message}",
                kind: SvgValidationKind.MalformedXml,
                inner: ex);
        }

        if (width <= 0 || height <= 0)
        {
            throw new SvgValidationException(
                "SVG has no parseable viewBox or width/height — cannot resolve display extents.",
                kind: SvgValidationKind.MalformedXml);
        }

        return new MediaInfo(
            Format: SvgFormat.Slug,
            Width: width,
            Height: height,
            FrameCount: 1,
            HasAlpha: true,        // SVG is always alpha-capable
            ColorDepth: 32,
            ExifOrientation: null,
            HasIccProfile: false);
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
        catch (SixLabors.ImageSharp.UnknownImageFormatException)
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
            // MEDIA-0006: SVG bundle path. Validate once, then rasterize
            // per-distinct-target across variants (dedupe key = (w, h)
            // tuple). Each branch re-enters the raster pipeline against
            // its dedicated PNG decode.
            var svgBytes = await TryReadSvgBytesAsync(_source, ct).ConfigureAwait(false);
            if (svgBytes is not null)
            {
                return await MaterializeSvgAsync(svgBytes, builder, ct).ConfigureAwait(false);
            }

            if (_source.CanSeek) _source.Position = 0;
            // Decode once; clone per variant so transforms don't bleed across branches.
            using var sourceImage = await Image.LoadAsync(_source, ct).ConfigureAwait(false);

            // MEDIA-0005: one probe drives planning for every branch (the
            // source kind is invariant across variants; only the steps
            // and terminal encoder differ).
            var probe = BuildMediaInfo(sourceImage);
            var variants = new Dictionary<string, MediaOutput>(builder.Variants.Count, StringComparer.Ordinal);
            foreach (var (name, configureBranch) in builder.Variants)
            {
                ct.ThrowIfCancellationRequested();
                // Each branch records its own step list against a transient pipeline carrier
                var branch = new StepRecorder();
                configureBranch(branch);
                var plan = PlanOrThrow(probe, branch.Steps);
                // Clone the decoded image so transforms apply only to this branch
                using var clone = sourceImage.Clone(_ => { });
                var output = await EncodeAsync(clone, branch.Steps, _overlayResolver, _fonts, _overlayDepth, _logger, plan, ct).ConfigureAwait(false);
                variants[name] = output;
            }
            return new MediaBundle(variants);
        }
        finally
        {
            await DisposeSourceAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// MEDIA-0006 multi-variant SVG materialisation. Rasterizes once per
    /// distinct target dimension across all variants (dedupe key =
    /// resolved (w, h) tuple) so two variants requesting 600×750 share a
    /// rasterization and a third requesting 1200×1500 triggers a second.
    /// </summary>
    private async Task<MediaBundle> MaterializeSvgAsync(
        byte[] svgBytes,
        MediaBundleBuilder builder,
        CancellationToken ct)
    {
        SvgValidator.ValidateOrThrow(svgBytes);
        var svgProbe = BuildSvgMediaInfo(svgBytes);

        // Cache rasterized PNG bytes by target dimensions so co-sized
        // variants reuse a single Skia render pass.
        var rasterCache = new Dictionary<(int, int), byte[]>();
        var variants = new Dictionary<string, MediaOutput>(builder.Variants.Count, StringComparer.Ordinal);

        foreach (var (name, configureBranch) in builder.Variants)
        {
            ct.ThrowIfCancellationRequested();
            var branch = new StepRecorder();
            configureBranch(branch);

            var plan = PlanOrThrow(svgProbe, branch.Steps);
            var (targetW, targetH) = ResolveRasterizeTarget(plan, svgProbe);

            if (!rasterCache.TryGetValue((targetW, targetH), out var pngBytes))
            {
                pngBytes = SvgRasterizer.RenderToPng(svgBytes, targetW, targetH);
                rasterCache[(targetW, targetH)] = pngBytes;
            }

            using var rasterized = new MemoryStream(pngBytes, writable: false);
            using var image = await Image.LoadAsync(rasterized, ct).ConfigureAwait(false);
            var rasterProbe = BuildMediaInfo(image);
            var rasterPlan = PlanOrThrow(rasterProbe, branch.Steps);
            var output = await EncodeAsync(
                image, branch.Steps, _overlayResolver, _fonts, _overlayDepth, _logger, rasterPlan, ct).ConfigureAwait(false);
            variants[name] = output with
            {
                SourceFormat = SvgFormat.Slug,
                KindTrace = plan.KindTrace,
            };
        }

        return new MediaBundle(variants);
    }

    // ----- engine -----

    /// <summary>
    /// Buffered legacy entry point used by <see cref="MaterializeAsync"/>
    /// — each variant materialises into a self-contained byte buffer so
    /// the returned <see cref="MediaBundle"/> can be inspected by name
    /// without retaining the decoded image. Delegates to
    /// <see cref="StreamEncodeAsync"/> for the actual mutate+encode and
    /// captures the bytes through an intermediate <see cref="MemoryStream"/>.
    /// </summary>
    private static async Task<MediaOutput> EncodeAsync(
        Image image,
        IReadOnlyList<MediaStep> steps,
        IOverlayResolver? overlays,
        KoanFontRegistry? fonts,
        int overlayDepth,
        ILogger logger,
        PlanResult? plan,
        CancellationToken ct)
    {
        await using var ms = new MemoryStream();
        var output = await StreamEncodeAsync(
            image, steps, overlays, fonts, overlayDepth, logger, plan, ms, ct).ConfigureAwait(false);
        var bytes = ms.ToArray();
#pragma warning disable CS0618 // Bytes is obsolete; buffered Materialize branches still surface bytes for backward compatibility.
        return output with
        {
            Bytes = bytes,
            WriteToAsync = (dest, dct) => dest.WriteAsync(bytes, dct).AsTask(),
        };
#pragma warning restore CS0618
    }

    /// <summary>
    /// Streaming engine. Runs all <paramref name="steps"/> against
    /// <paramref name="image"/> in canonical <see cref="PipelineStage"/>
    /// order and writes the encoded output directly into
    /// <paramref name="destination"/>. Caller owns image disposal. Per
    /// MEDIA-0008: the encoder's <see cref="Image.SaveAsync"/> writes
    /// straight into the destination — no intermediate buffer.
    /// </summary>
    private static async Task<MediaOutput> StreamEncodeAsync(
        Image image,
        IReadOnlyList<MediaStep> steps,
        IOverlayResolver? overlays,
        KoanFontRegistry? fonts,
        int overlayDepth,
        ILogger logger,
        PlanResult? plan,
        Stream destination,
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
#pragma warning disable CS0618 // ExtractFrameStep retained as obsolete alias; engine still handles it.
                case ExtractFrameStep ef:
                    ApplyExtractFrame(image, ef.Index);
                    break;
#pragma warning restore CS0618
                case SampleStep sample:
                    ApplySample(image, sample.Selector);
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

        // Per MEDIA-0008: stream the encoder output directly into the
        // destination. No intermediate MemoryStream — animated and
        // high-resolution encodes flush through chunk-by-chunk.
        await working.SaveAsync(destination, encoder, ct).ConfigureAwait(false);

        var sourceSlug = EncoderSelector.CanonicalSlug(sourceFormat);
#pragma warning disable CS0618 // Streaming terminal: Bytes is intentionally empty; callers consume bytes via WriteToAsync during the encode.
        return new MediaOutput(
            Bytes: Array.Empty<byte>(),
            ContentType: EncoderSelector.ContentType(resolvedFormat),
            Format: resolvedFormat,
            SourceFormat: sourceSlug,
            Width: working.Width,
            Height: working.Height,
            FrameCount: working.Frames.Count,
            Fingerprint: $"{resolvedFormat}-{working.Width}x{working.Height}-f{working.Frames.Count}-q{quality}")
        {
            KindTrace = plan?.KindTrace ?? Array.Empty<MediaKind>(),
            WriteToAsync = (_, _) => throw new InvalidOperationException(
                "MediaOutput.WriteToAsync was already consumed by the streaming terminal. " +
                "Re-render the recipe to write the bytes again, or call IMediaPipeline.ToBytesAsync " +
                "for a buffered output that supports re-emission."),
        };
#pragma warning restore CS0618
    }

    /// <summary>
    /// Apply a <see cref="SampleStep"/> to the running ImageSharp image.
    /// No-op on a single-frame raster; selector-driven frame extraction
    /// on an animated raster; Vector/Timeline paths are not reachable
    /// here in MEDIA-0005 because no decoder produces those kinds yet.
    /// </summary>
    private static void ApplySample(Image image, FrameSelector selector)
    {
        if (image.Frames.Count <= 1) return; // Raster path — no-op per MEDIA-0005 §2.
        switch (selector)
        {
            case FrameSelector.Index idx:
                ApplyExtractFrame(image, idx.Frame);
                break;
            case FrameSelector.HeuristicBest:
                // Heuristic best on AnimatedRaster degrades to first frame
                // (no decoder-provided thumbnail on the ImageSharp surface).
                ApplyExtractFrame(image, 0);
                break;
            case FrameSelector.Time:
                // Time selector is meaningful only for Timeline (no decoder
                // today). On AnimatedRaster, degrade to first frame so
                // a recipe authored against a future Timeline source still
                // round-trips when fed an animated raster.
                ApplyExtractFrame(image, 0);
                break;
        }
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
        [Obsolete("Use Sample(FrameSelector.Index(n)) or the Sample.Frame(n) factory.")]
        public IMediaPipeline ExtractFrame(int index = 0) => Add(new SampleStep(new FrameSelector.Index(index)));
        public IMediaPipeline Sample(FrameSelector selector)
        {
            if (selector is null) throw new ArgumentNullException(nameof(selector));
            return Add(new SampleStep(selector));
        }
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
        public Task<MediaOutput> WriteToAsync(Stream destination, CancellationToken ct = default) =>
            throw new NotSupportedException("WriteTo is implicit inside Materialize; the bundle returns each branch's output.");
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
