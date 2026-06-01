using System.Collections.Immutable;

namespace Koan.Media.Abstractions.Recipes;

/// <summary>
/// Fluent builder for <see cref="MediaRecipe"/>. Each verb produces a
/// new builder instance (immutable-friendly chaining). Terminal
/// methods <see cref="Build"/> and the implicit conversion to
/// <see cref="MediaRecipe"/> materialise the result.
///
/// The builder enforces:
/// <list type="bullet">
///   <item>At most one step per single-slot stage (shape, size, encode, frame, orient).</item>
///   <item>A terminal encode step — if none was declared, an implicit
///   <see cref="EncodeStep"/> with null format (preserve source) is appended at <see cref="Build"/>.</item>
///   <item>Last-call-wins for <c>.Name()</c> and <c>.Primary()</c> — they apply to the most recently added step.</item>
/// </list>
/// </summary>
public sealed class MediaRecipeBuilder
{
    private readonly List<MediaStep> _steps = new();
    private string? _name;
    private string? _description;
    private int _version = 1;
    private MutatorKind _mutators = MutatorKind.None;
    private bool _eager;

    internal MediaRecipeBuilder() { }

    public MediaRecipeBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public MediaRecipeBuilder WithDescription(string description)
    {
        _description = description;
        return this;
    }

    public MediaRecipeBuilder WithVersion(int version)
    {
        _version = version;
        return this;
    }

    public MediaRecipeBuilder Mutators(MutatorKind kinds)
    {
        _mutators = kinds;
        return this;
    }

    public MediaRecipeBuilder WithEager(bool eager = true)
    {
        _eager = eager;
        return this;
    }

    // ----- Stage 2: Orient -----

    /// <summary>EXIF-based auto-orient. Default-on behavior is provided
    /// by the engine when no orient step is declared; call this
    /// explicitly to keep EXIF orientation untouched.</summary>
    public MediaRecipeBuilder AutoOrient(bool keep = false) => Add(new AutoOrientStep(Keep: keep));

    // ----- Stage 3: Frame -----

    [Obsolete("Use Sample(FrameSelector.Index(n)) or the Sample.Frame(n) factory.")]
    public MediaRecipeBuilder ExtractFrame(int index = 0) =>
        // Delegate to Sample so all canonical fingerprints stabilize on the
        // MEDIA-0005 vocabulary; ExtractFrame is preserved purely for
        // source compatibility with v1 recipes.
        Add(new SampleStep(new FrameSelector.Index(index)));

    /// <summary>
    /// Kind-agnostic collapse to a single raster. Per MEDIA-0005 §2.
    /// Use the <see cref="Sample"/> factory for ergonomic selector
    /// construction (<c>Sample.First</c>, <c>Sample.Frame(n)</c>).
    /// </summary>
    public MediaRecipeBuilder Sample(FrameSelector selector)
    {
        if (selector is null) throw new ArgumentNullException(nameof(selector));
        return Add(new SampleStep(selector));
    }

    // ----- Stage 4: Rotate / Flip -----

    public MediaRecipeBuilder Rotate(int degrees) => Add(new RotateStep(degrees));
    public MediaRecipeBuilder FlipHorizontal() => Add(new FlipStep(FlipAxis.Horizontal));
    public MediaRecipeBuilder FlipVertical() => Add(new FlipStep(FlipAxis.Vertical));

    // ----- Stage 5: Shape -----

    public MediaRecipeBuilder Crop(string aspect) => Shape(crop: ParseCrop(aspect));
    public MediaRecipeBuilder Crop(CropSpec spec) => Shape(crop: spec);

    public MediaRecipeBuilder Fit(
        Fit mode = Recipes.Fit.Cover,
        int? width = null,
        int? height = null,
        Position? position = null,
        Background? bg = null)
    {
        var shape = new ShapeStep(
            Crop: width is not null && height is not null
                ? CropSpec.Pixels(width.Value, height.Value)
                : null,
            Fit: mode,
            Position: position ?? Position.Center,
            Background: bg ?? Background.Transparent());
        ReplaceOrAdd(shape, PipelineStage.Shape);
        return this;
    }

    /// <summary>Explicit shape declaration with all four CSS-aligned knobs.</summary>
    public MediaRecipeBuilder Shape(
        CropSpec? crop = null,
        Fit fit = Recipes.Fit.Cover,
        Position? position = null,
        Background? bg = null)
    {
        var shape = new ShapeStep(
            Crop: crop,
            Fit: fit,
            Position: position ?? Position.Center,
            Background: bg ?? Background.Transparent());
        ReplaceOrAdd(shape, PipelineStage.Shape);
        return this;
    }

    // ----- Stage 6: Size -----

    public MediaRecipeBuilder Resize(int? width = null, int? height = null, double dpr = 1.0)
    {
        ReplaceOrAdd(new ResizeStep(width, height, dpr), PipelineStage.Size);
        return this;
    }

    /// <summary>Resize to fit within bounds (preserves aspect; both axes capped).</summary>
    public MediaRecipeBuilder ResizeFit(int maxWidth, int maxHeight) =>
        Resize(maxWidth, maxHeight).Shape(fit: Recipes.Fit.Contain);

    /// <summary>Resize to cover bounds (preserves aspect; crops overflow).</summary>
    public MediaRecipeBuilder ResizeCover(int width, int height, Position? position = null) =>
        // Resize+Fit.Cover -> ImageSharp ResizeMode.Crop, which scales and
        // crops natively. Don't add an explicit Pixels crop here: the Shape
        // stage runs BEFORE Size, so a Pixels crop would chop a literal
        // WxH rectangle out of the source center and the subsequent resize
        // would no-op on the already-sized image.
        Resize(width, height).Shape(fit: Recipes.Fit.Cover, position: position ?? Position.Center);

    // ----- Stage 7: Overlay -----

    /// <summary>
    /// Composite a media-backed overlay layer onto the host image.
    /// Multiple <see cref="Overlay(string, OverlaySize?, Position?, OverlayPadding?, double, int, string?)"/>
    /// calls append additional layers to the single overlay slot, drawn
    /// in declared order (lower index = further back).
    /// </summary>
    public MediaRecipeBuilder Overlay(
        string mediaId,
        OverlaySize? size = null,
        Position? position = null,
        OverlayPadding? padding = null,
        double opacity = 1.0,
        int rotate = 0,
        string? recipeName = null)
    {
        if (string.IsNullOrWhiteSpace(mediaId))
            throw new ArgumentException("Overlay media id is required.", nameof(mediaId));
        var layer = new OverlayLayer(
            Source: new MediaOverlaySource(mediaId, recipeName),
            Size: size ?? OverlaySize.Natural,
            Position: position ?? Recipes.Position.Center,
            Padding: padding ?? OverlayPadding.Zero,
            Opacity: Math.Clamp(opacity, 0.0, 1.0),
            Rotate: rotate);
        return AddOverlayLayer(layer);
    }

    /// <summary>
    /// Composite a text overlay layer onto the host image. Requires a
    /// registered font (default: <c>default</c>).
    /// </summary>
    public MediaRecipeBuilder OverlayText(
        string text,
        string? font = null,
        BackgroundColor? color = null,
        int fontSize = 32,
        Position? position = null,
        OverlayPadding? padding = null,
        double opacity = 1.0,
        int rotate = 0)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Overlay text is required.", nameof(text));
        var layer = new OverlayLayer(
            Source: new TextOverlaySource(text, font, color, fontSize),
            Size: OverlaySize.Natural,
            Position: position ?? Recipes.Position.Center,
            Padding: padding ?? OverlayPadding.Zero,
            Opacity: Math.Clamp(opacity, 0.0, 1.0),
            Rotate: rotate);
        return AddOverlayLayer(layer);
    }

    /// <summary>
    /// Append an explicit <see cref="OverlayLayer"/> instance. Useful when
    /// configuring text + media overlays in a single batch or when binding
    /// from config.
    /// </summary>
    public MediaRecipeBuilder Overlay(OverlayLayer layer) => AddOverlayLayer(layer);

    private MediaRecipeBuilder AddOverlayLayer(OverlayLayer layer)
    {
        var existingIndex = _steps.FindIndex(s => s.Stage == PipelineStage.Overlay);
        if (existingIndex >= 0)
        {
            var existing = (OverlayStep)_steps[existingIndex];
            _steps[existingIndex] = existing with { Layers = existing.Layers.Add(layer) };
        }
        else
        {
            _steps.Add(new OverlayStep(Layers: System.Collections.Immutable.ImmutableArray.Create(layer)));
        }
        return this;
    }

    // ----- Stage 8: Strip -----

    public MediaRecipeBuilder Strip(MetadataKinds kinds = MetadataKinds.All)
    {
        ReplaceOrAdd(new StripStep(kinds), PipelineStage.Metadata);
        return this;
    }

    // ----- Stage 9: Encode -----

    /// <summary>Encode in the source's own format (the default if no encode is declared).</summary>
    public MediaRecipeBuilder PreserveFormat(int quality = Recipes.Quality.Web)
    {
        ReplaceOrAdd(new EncodeStep(Format: null, Quality: quality), PipelineStage.Encode);
        return this;
    }

    /// <summary>Encode as the named format (still preserves animation/alpha if the target supports them).</summary>
    public MediaRecipeBuilder EncodeAs(string format, int quality = Recipes.Quality.Web)
    {
        ReplaceOrAdd(new EncodeStep(Format: format, Quality: quality), PipelineStage.Encode);
        return this;
    }

    /// <summary>
    /// Destructive: change format and drop animation / alpha if the
    /// target doesn't support them. Explicit verb; engine logs at
    /// Information.
    /// </summary>
    public MediaRecipeBuilder FlattenTo(string format, int quality = Recipes.Quality.Web)
    {
        ReplaceOrAdd(new FlattenToStep(Format: format, Quality: quality), PipelineStage.Encode);
        return this;
    }

    // ----- Step decorators (apply to last-added step) -----

    public MediaRecipeBuilder Name(string name)
    {
        if (_steps.Count == 0) throw new InvalidOperationException("Name() requires a preceding step.");
        var last = _steps[^1];
        _steps[^1] = last with { Name = name };
        return this;
    }

    public MediaRecipeBuilder Primary()
    {
        if (_steps.Count == 0) throw new InvalidOperationException("Primary() requires a preceding step.");
        var last = _steps[^1];
        _steps[^1] = last with { Primary = true };
        return this;
    }

    // ----- Materialisation -----

    public MediaRecipe Build()
    {
        // Append implicit format-preserving encode if the caller didn't declare one
        if (!_steps.Any(s => s.Stage == PipelineStage.Encode))
        {
            _steps.Add(new EncodeStep(Format: null));
        }
        return new MediaRecipe
        {
            Name = _name,
            Description = _description,
            Version = _version,
            Steps = _steps.ToImmutableArray(),
            AllowedMutators = _mutators,
            Eager = _eager,
        };
    }

    public static implicit operator MediaRecipe(MediaRecipeBuilder b) => b.Build();

    // ----- internals -----

    private MediaRecipeBuilder Add(MediaStep step)
    {
        // Single-slot stages: replace
        if (step.Stage == PipelineStage.Shape ||
            step.Stage == PipelineStage.Size ||
            step.Stage == PipelineStage.Encode ||
            step.Stage == PipelineStage.Frame ||
            step.Stage == PipelineStage.Orient ||
            step.Stage == PipelineStage.Metadata)
        {
            ReplaceOrAdd(step, step.Stage);
        }
        else
        {
            _steps.Add(step);
        }
        return this;
    }

    private void ReplaceOrAdd(MediaStep step, PipelineStage slot)
    {
        var existing = _steps.FindIndex(s => s.Stage == slot);
        if (existing >= 0) _steps[existing] = step;
        else _steps.Add(step);
    }

    private static CropSpec ParseCrop(string raw)
    {
        if (!CropSpec.TryParse(raw, out var spec))
        {
            throw new ArgumentException($"Invalid crop value '{raw}'. Expected 'square', 'W:H', 'WxH', or 'WxH+X,Y'.", nameof(raw));
        }
        return spec;
    }
}
