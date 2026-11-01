using System.Collections.Immutable;
using System.Xml;
using Koan.Media.Abstractions.Recipes;
using Koan.Media.Core.Fonts;
using Koan.Media.Core.Formats;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace Koan.Media.Core.Pipeline;

/// <summary>
/// Default <see cref="IMediaPipeline"/> implementation. Per
/// MEDIA-0004:
/// <list type="bullet">
///   <item>Single decode, multi-output via <see cref="MaterializeAsync"/></item>
///   <item>Format-preserving by default; <see cref="FlattenTo"/> opts into destructive change</item>
///   <item>Every transform maps over every frame, so animation survives resize/crop/rotate</item>
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

    public IMediaPipeline Trim(int frames)
    {
        if (frames < 1) throw new ArgumentOutOfRangeException(nameof(frames), "Trim frame count must be >= 1.");
        return AddStep(new TrimStep(Frames: frames));
    }

    public IMediaPipeline Trim(double seconds)
    {
        if (!(seconds > 0)) throw new ArgumentOutOfRangeException(nameof(seconds), "Trim duration must be > 0 seconds.");
        return AddStep(new TrimStep(Seconds: seconds));
    }

    public IMediaPipeline Freeze(int at = 0)
    {
        if (at < 0) throw new ArgumentOutOfRangeException(nameof(at), "Freeze frame index must be >= 0.");
        return AddStep(new SampleStep(new FrameSelector.Index(at)));
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

    public IMediaPipeline Resize(int? width = null, int? height = null, double dpr = 1.0, bool upscale = false) =>
        AddStep(new ResizeStep(width, height, dpr, Upscale: upscale));

    public IMediaPipeline ResizeFit(int maxWidth, int maxHeight) =>
        Resize(maxWidth, maxHeight).Shape(fit: Fit.Contain);

    public IMediaPipeline ResizeCover(int width, int height, Position? position = null) =>
        // Resize+Fit.Cover already scales then crops. Adding an explicit CropSpec.Pixels
        // here would cause the Shape stage (40) to do a literal pixel-rect
        // crop BEFORE the Size stage (50) ever runs — producing a center
        // chunk of the source instead of a cover-scaled thumbnail.
        //
        // upscale=true: covering a target box requires enlarging a smaller
        // source by definition; the no-upscale default would silently
        // under-fill the box for sub-target inputs.
        Resize(width, height, upscale: true).Shape(fit: Fit.Cover, position: position ?? Position.Center);

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
            var source = await MediaDecoder.ReadSourceAsync(_source, _limits, ct).ConfigureAwait(false);

            // MEDIA-0006: SVG branch. The header sniff is the first thing
            // every terminal does — Content-Type / extension cannot be trusted.
            if (IsSvg(source))
            {
                SvgValidator.ValidateOrThrow(source);
                return BuildSvgMediaInfo(source);
            }
            return MediaDecoder.Probe(source, _limits);
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
    /// Streaming terminal. Decodes the source, plans the pipeline, runs the
    /// mutating stages, and writes the encoded output into
    /// <paramref name="destination"/> asynchronously, so an HTTP response
    /// body that refuses synchronous writes is a valid destination. Per
    /// MEDIA-0008, with the buffering MEDIA-0010 states: the encoders are
    /// synchronous, so the encoded output sits in native memory until copied.
    /// </summary>
    public async Task<MediaOutput> WriteToAsync(Stream destination, CancellationToken ct = default)
    {
        if (destination is null) throw new ArgumentNullException(nameof(destination));
        EnsureUnconsumed();
        _consumed = true;
        try
        {
            var source = await MediaDecoder.ReadSourceAsync(_source, _limits, ct).ConfigureAwait(false);

            // MEDIA-0006: SVG branch. Validate, plan against a synthetic
            // Vector probe, rasterize at the planner's forward-derived
            // target, and hand the PNG to the raster chain. The raw SVG is
            // the source of truth in storage; rasterization only fires when
            // a recipe demands a Raster target.
            if (IsSvg(source))
            {
                return await StreamEncodeSvgAsync(source, destination, ct).ConfigureAwait(false);
            }

            using var image = MediaDecoder.Decode(source, _limits, ct);

            // MEDIA-0005: plan before execute. The planner is a pure
            // function over (probe, steps, encoderAccepts); a failing plan
            // throws synchronously and never reaches the encode pass.
            var plan = PlanOrThrow(image.Info, _steps);
            return await StreamEncodeAsync(
                image, _steps, _overlayResolver, _fonts, _limits, _overlayDepth, _logger, plan, destination, ct).ConfigureAwait(false);
        }
        finally
        {
            await DisposeSourceAsync().ConfigureAwait(false);
        }
    }

    private static bool IsSvg(byte[] source) =>
        SvgFormat.MatchHeader(source.AsSpan(0, Math.Min(source.Length, SvgFormat.HeaderSniffBytes)));

    /// <summary>
    /// MEDIA-0006 streaming SVG terminal: validate, plan, rasterize, then
    /// run the recipe's stages and terminal encode on the rasterized pixels.
    /// </summary>
    private async Task<MediaOutput> StreamEncodeSvgAsync(byte[] svgBytes, Stream destination, CancellationToken ct)
    {
        SvgValidator.ValidateOrThrow(svgBytes);

        var probe = BuildSvgMediaInfo(svgBytes);
        var plan = PlanOrThrow(probe, _steps);

        var (targetW, targetH) = ResolveRasterizeTarget(plan, probe);
        MediaDecoder.EnsureOutputFits(_limits, targetW, targetH);
        var pngBytes = SvgRasterizer.RenderToPng(svgBytes, targetW, targetH);

        // Strip the implicit Rasterize step before re-entering the raster
        // pipeline — the rasterizer has already produced bytes at the
        // planner's forward-derived target. Author steps (Resize, Shape,
        // Encode) flow through unchanged.
        using var image = MediaDecoder.Decode(pngBytes, _limits, ct);

        // Re-plan against the raster-sided probe so KindTrace records the
        // full Vector -> Raster -> Encode transition.
        var rasterPlan = PlanOrThrow(image.Info, _steps);
        var output = await StreamEncodeAsync(
            image, _steps, _overlayResolver, _fonts, _limits, _overlayDepth, _logger, rasterPlan, destination, ct).ConfigureAwait(false);
        return output with
        {
            SourceFormat = SvgFormat.Slug,
            KindTrace = plan.KindTrace,
        };
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
    /// format preserving the source writes.
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
                    targetSlug = encode.Format ?? EncoderSelector.PreservedFormat(sourceFormatSlug);
                    i = -1;
                    break;
            }
        }
        targetSlug ??= EncoderSelector.PreservedFormat(sourceFormatSlug);
        var accepts = EncoderAccepts.AcceptsFor(targetSlug);
        // Unregistered encoders fall through with KindSet.None; if we got
        // None for an unknown slug, leave it null so the planner doesn't
        // refuse the source kind against an empty set (EncoderSelector.Encode
        // throws NotSupportedException downstream).
        return accepts.IsEmpty ? KindSet.All : accepts;
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
            var source = await MediaDecoder.ReadSourceAsync(_source, _limits, ct).ConfigureAwait(false);

            // MEDIA-0006: SVG bundle path. Validate once, then rasterize
            // per-distinct-target across variants (dedupe key = (w, h)
            // tuple). Each branch re-enters the raster pipeline against
            // its dedicated PNG decode.
            if (IsSvg(source))
            {
                return await MaterializeSvgAsync(source, builder, ct).ConfigureAwait(false);
            }

            // Decode once; clone per variant so transforms don't bleed across branches.
            using var sourceImage = MediaDecoder.Decode(source, _limits, ct);

            // MEDIA-0005: one probe drives planning for every branch (the
            // source kind is invariant across variants; only the steps
            // and terminal encoder differ).
            var variants = new Dictionary<string, MediaOutput>(builder.Variants.Count, StringComparer.Ordinal);
            foreach (var (name, configureBranch) in builder.Variants)
            {
                ct.ThrowIfCancellationRequested();
                // Each branch records its own step list against a transient pipeline carrier
                var branch = new StepRecorder();
                configureBranch(branch);
                var plan = PlanOrThrow(sourceImage.Info, branch.Steps);
                // Clone the decoded image so transforms apply only to this branch
                using var clone = sourceImage.Clone();
                variants[name] = await EncodeAsync(clone, branch.Steps, _overlayResolver, _fonts, _limits, _overlayDepth, _logger, plan, ct).ConfigureAwait(false);
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
            MediaDecoder.EnsureOutputFits(_limits, targetW, targetH);

            if (!rasterCache.TryGetValue((targetW, targetH), out var pngBytes))
            {
                pngBytes = SvgRasterizer.RenderToPng(svgBytes, targetW, targetH);
                rasterCache[(targetW, targetH)] = pngBytes;
            }

            using var image = MediaDecoder.Decode(pngBytes, _limits, ct);
            var rasterPlan = PlanOrThrow(image.Info, branch.Steps);
            var output = await EncodeAsync(
                image, branch.Steps, _overlayResolver, _fonts, _limits, _overlayDepth, _logger, rasterPlan, ct).ConfigureAwait(false);
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
    /// Buffered entry point used by <see cref="MaterializeAsync"/> — each
    /// variant materialises into a self-contained byte buffer so the
    /// returned <see cref="MediaBundle"/> can be inspected by name without
    /// retaining the decoded image. Delegates to
    /// <see cref="StreamEncodeAsync"/> for the actual mutate+encode and
    /// captures the bytes through an intermediate <see cref="MemoryStream"/>.
    /// </summary>
    private static async Task<MediaOutput> EncodeAsync(
        RasterImage image,
        IReadOnlyList<MediaStep> steps,
        IOverlayResolver? overlays,
        KoanFontRegistry? fonts,
        MediaPipelineLimits limits,
        int overlayDepth,
        ILogger logger,
        PlanResult? plan,
        CancellationToken ct)
    {
        await using var ms = new MemoryStream();
        var output = await StreamEncodeAsync(
            image, steps, overlays, fonts, limits, overlayDepth, logger, plan, ms, ct).ConfigureAwait(false);
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
    /// The engine. Runs all <paramref name="steps"/> against
    /// <paramref name="image"/> in canonical <see cref="PipelineStage"/>
    /// order and writes the encoded output into
    /// <paramref name="destination"/>. Caller owns image disposal. The token
    /// is honoured between steps and frames; no decode or encode call takes one.
    /// </summary>
    private static async Task<MediaOutput> StreamEncodeAsync(
        RasterImage image,
        IReadOnlyList<MediaStep> steps,
        IOverlayResolver? overlays,
        KoanFontRegistry? fonts,
        MediaPipelineLimits limits,
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
                {
                    var (orientedW, orientedH) = FrameTransforms.OrientedSize(image.Width, image.Height, image.Origin);
                    image.Map(orientedW, orientedH, frame => FrameTransforms.Orient(frame, image.Origin), ct);
                    break;
                }
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
                case TrimStep trim:
                    ApplyTrim(image, trim);
                    break;
                case RotateStep rs:
                {
                    var (rotatedW, rotatedH) = FrameTransforms.RotatedSize(image.Width, image.Height, rs.Degrees);
                    image.Map(rotatedW, rotatedH, frame => FrameTransforms.Rotate(frame, rs.Degrees), ct);
                    break;
                }
                    break;
                case FlipStep fs:
                    image.Map(image.Width, image.Height, frame => FrameTransforms.Flip(frame, fs.Axis), ct);
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

        // Order: shape (crop + fit + position + bg) → size.
        if (pendingShape is not null || pendingResize is not null)
        {
            ApplyShapeAndResize(image, pendingShape, pendingResize, ct);
        }

        // Background compose: when bg is non-transparent + Fit.Contain has
        // a fully-defined target canvas, every frame lands on a sized,
        // painted canvas (solid color / dominant / auto-border / cover-blur)
        // so overlays, strip, and encode all work on the final pixels.
        BackgroundComposer.Apply(image, pendingShape, pendingResize, ct);

        // Overlays composite onto the shaped/sized host before metadata
        // strip and encode so they're part of the final encoded bytes.
        if (pendingOverlay is not null)
        {
            await OverlayCompositor.ApplyAsync(
                image, pendingOverlay, overlays, fonts, limits, overlayDepth, logger, ct).ConfigureAwait(false);
        }

        // A re-encode never carries Exif, XMP, IPTC or format text; the color
        // profile is the one thing that survives unless it is stripped.
        if (pendingStrip is not null && pendingStrip.Kinds.HasFlag(MetadataKinds.Icc))
        {
            image.Map(image.Width, image.Height, FrameTransforms.DropColorProfile, ct);
        }

        // Resolve encode step. Implicit format-preserving encode if none declared.
        string? targetFormat;
        int quality;
        switch (encodeStep)
        {
            case FlattenToStep flatten:
                targetFormat = flatten.Format;
                quality = flatten.Quality;
                ApplyFlatten(image);
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

        var resolvedFormat = targetFormat is null
            ? EncoderSelector.PreservedFormat(image.Info.Format)
            : EncoderSelector.CanonicalizeSlug(targetFormat);

        ct.ThrowIfCancellationRequested();
        using (var encoded = EncoderSelector.Encode(image, resolvedFormat, quality))
        await using (var bytes = encoded.AsStream())
        {
            ct.ThrowIfCancellationRequested();
            await bytes.CopyToAsync(destination, ct).ConfigureAwait(false);
        }

#pragma warning disable CS0618 // Streaming terminal: Bytes is intentionally empty; callers consume bytes via WriteToAsync during the encode.
        return new MediaOutput(
            Bytes: Array.Empty<byte>(),
            ContentType: EncoderSelector.ContentType(resolvedFormat),
            Format: resolvedFormat,
            SourceFormat: image.Info.Format,
            Width: image.Width,
            Height: image.Height,
            FrameCount: image.Frames.Count,
            Fingerprint: $"{resolvedFormat}-{image.Width}x{image.Height}-f{image.Frames.Count}-q{quality}")
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
    /// Apply a <see cref="SampleStep"/> to the running image. No-op on a
    /// single-frame raster; selector-driven frame extraction on an animated
    /// raster.
    /// </summary>
    private static void ApplySample(RasterImage image, FrameSelector selector)
    {
        if (image.Frames.Count <= 1) return; // Raster path — no-op per MEDIA-0005 §2.
        switch (selector)
        {
            case FrameSelector.Index idx:
                ApplyExtractFrame(image, idx.Frame);
                break;
            case FrameSelector.HeuristicBest:
                // Heuristic best on AnimatedRaster degrades to first frame
                // (the decoder provides no thumbnail).
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

    private static void ApplyExtractFrame(RasterImage image, int index)
    {
        if (image.Frames.Count <= 1) return; // no-op on static
        if (index < 0 || index >= image.Frames.Count)
            throw new ArgumentOutOfRangeException(nameof(index),
                $"Frame index {index} out of range for source with {image.Frames.Count} frame(s).");
        // Remove every frame except the requested one. We iterate from
        // the end so indices stay stable.
        for (var i = image.Frames.Count - 1; i >= 0; i--)
        {
            if (i != index) image.RemoveFrame(i);
        }
    }

    /// <summary>
    /// Apply a <see cref="TrimStep"/> to the running image. No-op on a
    /// static source (frame count &lt;= 1). The frames-variant trims
    /// every index past the cap; the seconds-variant sums per-frame
    /// durations until the cap is reached, then trims at that frame.
    /// </summary>
    private static void ApplyTrim(RasterImage image, TrimStep step)
    {
        if (image.Frames.Count <= 1) return;

        var cutoffFrames = step.Frames ?? FramesUnderDuration(image, step.Seconds ?? 0);
        if (cutoffFrames <= 0) return;
        if (cutoffFrames >= image.Frames.Count) return; // already short enough

        // Remove from the tail so earlier indices stay stable.
        for (var i = image.Frames.Count - 1; i >= cutoffFrames; i--)
        {
            image.RemoveFrame(i);
        }
    }

    /// <summary>
    /// Sum playback time until <paramref name="seconds"/> is met. Returns
    /// the first index whose cumulative time meets or exceeds the cap (so
    /// frames [0..returnedIndex) are kept).
    /// </summary>
    private static int FramesUnderDuration(RasterImage image, double seconds)
    {
        if (seconds <= 0) return 0;
        var capMs = seconds * 1000.0;

        double cumulativeMs = 0;
        for (var i = 0; i < image.Frames.Count; i++)
        {
            // Many encoders set a per-frame delay of 0 to mean "use default"
            // (typically ~100ms). Without that fallback a 0-delay anim would
            // satisfy any cap at frame 1 and trim almost everything away.
            double frameMs = image.Frames[i].DurationMs;
            if (frameMs <= 0) frameMs = 100;

            cumulativeMs += frameMs;
            if (cumulativeMs >= capMs)
            {
                // Keep frames [0..i] inclusive → cutoffFrames = i + 1.
                return i + 1;
            }
        }
        // Cap exceeds full playback length — nothing to trim.
        return image.Frames.Count;
    }

    private static void ApplyShapeAndResize(
        RasterImage image,
        ShapeStep? shape,
        ResizeStep? resize,
        CancellationToken ct)
    {
        // Compute shape pass (crop to aspect / explicit pixels)
        if (shape?.Crop is { } crop)
        {
            var area = CropArea(image.Width, image.Height, crop, shape.Position);
            image.Map(area.Width, area.Height, frame => FrameTransforms.Crop(frame, area), ct);
        }

        if (resize is not { } rz) return;

        // Resize pass
        var (currentW, currentH) = (image.Width, image.Height);
        var (rw, rh) = ResolveResizeDimensions(currentW, currentH, rz);
        if (rw is not { } targetW || rh is not { } targetH) return;

        // No-upscale guard: when the source already fits inside the target on
        // both axes and the caller did NOT opt into upscaling, skip the resize
        // entirely. Silently enlarging small sources is expensive on animated
        // ones, because every frame gets re-encoded at the inflated size.
        if (!rz.Upscale && currentW <= targetW && currentH <= targetH) return;

        switch (shape?.Fit)
        {
            case Fit.None:
                return;
            case Fit.Fill:
                image.Map(targetW, targetH, frame => FrameTransforms.Scale(frame, targetW, targetH), ct);
                return;
            case Fit.Cover:
            {
                var (scaledW, scaledH, offsetX, offsetY) = ResizePlan.Cover(currentW, currentH, targetW, targetH);
                var box = SKRectI.Create(offsetX, offsetY, targetW, targetH);
                image.Map(targetW, targetH, frame => FrameTransforms.ScaleToBox(frame, scaledW, scaledH, box), ct);
                return;
            }
            default:
            {
                var (fitW, fitH) = ResizePlan.Contain(currentW, currentH, targetW, targetH);
                image.Map(fitW, fitH, frame => FrameTransforms.Scale(frame, fitW, fitH), ct);
                return;
            }
        }
    }

    private static SKRectI CropArea(int sourceW, int sourceH, CropSpec crop, Position position)
    {
        switch (crop.Kind)
        {
            case CropSpecKind.Aspect:
            {
                var (cw, ch) = LargestFitting(sourceW, sourceH, (double)crop.Width / crop.Height);
                var (cx, cy) = AnchorOffset(sourceW, sourceH, cw, ch, position);
                return SKRectI.Create(cx, cy, Math.Max(1, cw), Math.Max(1, ch));
            }
            case CropSpecKind.Pixels:
            {
                var (cw, ch) = (Math.Min(crop.Width, sourceW), Math.Min(crop.Height, sourceH));
                var (cx, cy) = AnchorOffset(sourceW, sourceH, cw, ch, position);
                return SKRectI.Create(cx, cy, cw, ch);
            }
            default:
            {
                var x = Math.Clamp(crop.OffsetX, 0, Math.Max(0, sourceW - crop.Width));
                var y = Math.Clamp(crop.OffsetY, 0, Math.Max(0, sourceH - crop.Height));
                return SKRectI.Create(x, y, Math.Min(crop.Width, sourceW - x), Math.Min(crop.Height, sourceH - y));
            }
        }
    }

    private static (int? Width, int? Height) ResolveResizeDimensions(int currentW, int currentH, ResizeStep rz)
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
            h = Math.Max(1, (int)Math.Round(currentH * (w.Value / (double)currentW)));
        }
        else if (h.HasValue && !w.HasValue)
        {
            w = Math.Max(1, (int)Math.Round(currentW * (h.Value / (double)currentH)));
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

    /// <summary>
    /// FlattenTo behavior: collapse to a single frame. A target without an
    /// alpha channel composites what transparency remains onto black at
    /// encode; the HTTP layer resolves a different fill through the Shape
    /// step's background before it gets there.
    /// </summary>
    private static void ApplyFlatten(RasterImage image)
    {
        for (var i = image.Frames.Count - 1; i >= 1; i--)
        {
            image.RemoveFrame(i);
        }
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
        public IMediaPipeline Trim(int frames)
        {
            if (frames < 1) throw new ArgumentOutOfRangeException(nameof(frames), "Trim frame count must be >= 1.");
            return Add(new TrimStep(Frames: frames));
        }
        public IMediaPipeline Trim(double seconds)
        {
            if (!(seconds > 0)) throw new ArgumentOutOfRangeException(nameof(seconds), "Trim duration must be > 0 seconds.");
            return Add(new TrimStep(Seconds: seconds));
        }
        public IMediaPipeline Freeze(int at = 0)
        {
            if (at < 0) throw new ArgumentOutOfRangeException(nameof(at), "Freeze frame index must be >= 0.");
            return Add(new SampleStep(new FrameSelector.Index(at)));
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
        public IMediaPipeline Resize(int? width = null, int? height = null, double dpr = 1.0, bool upscale = false) =>
            Add(new ResizeStep(width, height, dpr, Upscale: upscale));
        public IMediaPipeline ResizeFit(int maxWidth, int maxHeight) =>
            Resize(maxWidth, maxHeight).Shape(fit: Fit.Contain);
        public IMediaPipeline ResizeCover(int width, int height, Position? position = null) =>
            Resize(width, height, upscale: true).Shape(fit: Fit.Cover, position: position ?? Position.Center);
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
