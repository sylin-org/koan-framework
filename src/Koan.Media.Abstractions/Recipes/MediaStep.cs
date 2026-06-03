using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Koan.Media.Abstractions.Recipes;

/// <summary>
/// Single immutable step in a <see cref="MediaRecipe"/>. Each step
/// pins to a <see cref="PipelineStage"/> via <see cref="Stage"/>;
/// the pipeline engine executes steps in stage order regardless of
/// declaration order. Per MEDIA-0004 §1.
/// </summary>
/// <param name="Stage">Canonical stage this step runs in.</param>
/// <param name="Name">Optional addressable name for URL overrides (<c>?stepName.param=</c>).</param>
/// <param name="Primary">When true, unprefixed URL overrides target this step.</param>
public abstract record MediaStep(PipelineStage Stage, string? Name, bool Primary)
{
    /// <summary>Append the step's discriminator and canonical params to the fingerprint payload.</summary>
    public abstract void WriteFingerprint(StringBuilder sb);

    /// <summary>
    /// Kinds this step accepts as input. Default <see cref="KindSet.All"/> —
    /// the step does not constrain its input. Per MEDIA-0005 §4 planner
    /// contract: if <c>currentKind</c> is not in <see cref="AcceptsFrom"/>
    /// the planner returns <c>KindMismatch</c> with a <c>Sample.First</c>
    /// suggestion.
    /// </summary>
    public virtual KindSet AcceptsFrom => KindSet.All;

    /// <summary>
    /// Kind this step produces, or null when the step is kind-preserving
    /// (output kind = input kind). Per MEDIA-0005 §4: a non-null value
    /// rewrites <c>currentKind</c>; null leaves it unchanged.
    /// </summary>
    public virtual MediaKind? ProducesTo => null;
}

/// <summary>EXIF-based auto-orient. Stage <see cref="PipelineStage.Orient"/>.</summary>
public sealed record AutoOrientStep(bool Keep = false) : MediaStep(PipelineStage.Orient, null, false)
{
    public override void WriteFingerprint(StringBuilder sb) =>
        sb.Append("orient(").Append(Keep ? "keep" : "auto").Append(')');

    // Per MEDIA-0005 §4: EXIF orientation applies to raster sources only.
    // Vector and Timeline sources have no EXIF block; reject upstream so
    // the planner produces a typed mismatch with a Sample suggestion.
    public override KindSet AcceptsFrom => KindSet.Of(MediaKind.Raster, MediaKind.AnimatedRaster);
}

/// <summary>
/// Animated → still extraction. Stage <see cref="PipelineStage.Frame"/>.
/// No-op on static sources.
/// </summary>
/// <remarks>
/// Per MEDIA-0005 §Migration: superseded by <see cref="SampleStep"/>.
/// Existing call sites keep compiling; the canonical fingerprint
/// representation is emitted via <see cref="SampleStep"/>'s
/// <see cref="FrameSelector.Index"/> form.
/// </remarks>
[Obsolete("Use SampleStep(new FrameSelector.Index(n)) or the Sample.Frame(n) factory.")]
public sealed record ExtractFrameStep(int Index, string? Name = null, bool Primary = false)
    : MediaStep(PipelineStage.Frame, Name, Primary)
{
    public override void WriteFingerprint(StringBuilder sb) =>
        sb.Append("frame(").Append(Index).Append(')');

    public override KindSet AcceptsFrom => KindSet.Of(MediaKind.Raster, MediaKind.AnimatedRaster);
    public override MediaKind? ProducesTo => MediaKind.Raster;
}

/// <summary>
/// Kind-agnostic, selector-discriminated collapse from any source
/// kind into a single <see cref="MediaKind.Raster"/>. Per MEDIA-0005 §2.
///
/// Plan-time behavior per source kind:
/// <list type="bullet">
///   <item><see cref="MediaKind.Raster"/> — no-op.</item>
///   <item><see cref="MediaKind.AnimatedRaster"/> — apply the selector.</item>
///   <item><see cref="MediaKind.Vector"/> — deferred to <c>Rasterize</c> at the encoder boundary.</item>
///   <item><see cref="MediaKind.Timeline"/> — apply the selector.</item>
/// </list>
/// </summary>
public sealed record SampleStep(FrameSelector Selector, string? Name = null, bool Primary = false)
    : MediaStep(PipelineStage.Frame, Name, Primary)
{
    public override void WriteFingerprint(StringBuilder sb) =>
        sb.Append("sample(").Append(Selector.ToCanonical()).Append(')');

    public override KindSet AcceptsFrom => KindSet.Of(
        MediaKind.Raster,
        MediaKind.AnimatedRaster,
        MediaKind.Vector,
        MediaKind.Timeline);

    public override MediaKind? ProducesTo => MediaKind.Raster;
}

/// <summary>Explicit rotation. Stage <see cref="PipelineStage.Rotate"/>.</summary>
public sealed record RotateStep(int Degrees, string? Name = null, bool Primary = false)
    : MediaStep(PipelineStage.Rotate, Name, Primary)
{
    public override void WriteFingerprint(StringBuilder sb) =>
        sb.Append("rotate(").Append(Degrees).Append(')');

    // Geometric transform — kind-agnostic, kind-preserving.
    public override KindSet AcceptsFrom => KindSet.All;
    public override MediaKind? ProducesTo => null;
}

public enum FlipAxis { Horizontal, Vertical }

/// <summary>Horizontal or vertical flip. Stage <see cref="PipelineStage.Rotate"/>.</summary>
public sealed record FlipStep(FlipAxis Axis, string? Name = null, bool Primary = false)
    : MediaStep(PipelineStage.Rotate, Name, Primary)
{
    public override void WriteFingerprint(StringBuilder sb) =>
        sb.Append("flip(").Append(Axis == FlipAxis.Horizontal ? 'h' : 'v').Append(')');

    // Geometric transform — kind-agnostic, kind-preserving.
    public override KindSet AcceptsFrom => KindSet.All;
    public override MediaKind? ProducesTo => null;
}

/// <summary>
/// Shape step — CSS-aligned (crop + fit + position + bg). Single slot
/// per pipeline; stage <see cref="PipelineStage.Shape"/>. Per
/// MEDIA-0004 §5.
/// </summary>
public sealed record ShapeStep(
    CropSpec? Crop,
    Fit Fit,
    Position Position,
    Background Background,
    string? Name = null,
    bool Primary = false)
    : MediaStep(PipelineStage.Shape, Name, Primary)
{
    public override void WriteFingerprint(StringBuilder sb)
    {
        sb.Append("shape(");
        sb.Append("crop=").Append(Crop?.ToCanonical() ?? "-");
        sb.Append(",fit=").Append(Fit.ToString().ToLowerInvariant());
        sb.Append(",pos=").Append(Position.ToCanonical());
        sb.Append(",bg=").Append(Background.ToCanonical());
        sb.Append(')');
    }

    // Shape (crop / fit / position) is a geometric transform — kind-agnostic,
    // kind-preserving. Planner forward-derives Rasterize from this step's
    // explicit pixel dimensions when a Vector source reaches the encoder boundary.
    public override KindSet AcceptsFrom => KindSet.All;
    public override MediaKind? ProducesTo => null;
}

/// <summary>
/// Resize step — single slot, stage <see cref="PipelineStage.Size"/>.
///
/// <para><see cref="Upscale"/> defaults to <c>false</c>: when the source
/// already fits inside the target box on both axes, the resize is skipped
/// entirely (source dimensions and bytes survive). This prevents the
/// recipe author from accidentally inflating a small source — historically
/// a silent-and-expensive failure mode for animated WebP / GIF sources
/// where each frame had to be re-encoded at the enlarged size.</para>
/// </summary>
public sealed record ResizeStep(
    int? Width,
    int? Height,
    double Dpr = 1.0,
    string? Name = null,
    bool Primary = false,
    bool Upscale = false)
    : MediaStep(PipelineStage.Size, Name, Primary)
{
    public override void WriteFingerprint(StringBuilder sb)
    {
        sb.Append("resize(w=").Append(Width?.ToString(CultureInfo.InvariantCulture) ?? "-");
        sb.Append(",h=").Append(Height?.ToString(CultureInfo.InvariantCulture) ?? "-");
        if (Math.Abs(Dpr - 1.0) > 0.001)
        {
            sb.Append(",dpr=").Append(Dpr.ToString("F2", CultureInfo.InvariantCulture));
        }
        // Default is false; only emit when true so existing recipe fingerprints
        // don't churn. Existing cached derivations on those fingerprints were
        // produced before the no-upscale behavior — they continue to serve
        // until evicted, and re-renders on cache miss pick up the new default.
        if (Upscale)
        {
            sb.Append(",upscale=true");
        }
        sb.Append(')');
    }

    // Sizing is kind-agnostic and kind-preserving. Per MEDIA-0005 §4
    // the planner uses this step's target dimensions as the forward-derived
    // Rasterize target when a Vector source reaches the encoder boundary.
    public override KindSet AcceptsFrom => KindSet.All;
    public override MediaKind? ProducesTo => null;
}

/// <summary>
/// Multi-layer overlay composition. Stage
/// <see cref="PipelineStage.Overlay"/>. Layers composite in declared
/// index order (lower index = drawn first / further back).
/// </summary>
public sealed record OverlayStep(
    ImmutableArray<OverlayLayer> Layers,
    string? Name = null,
    bool Primary = false)
    : MediaStep(PipelineStage.Overlay, Name, Primary)
{
    public override void WriteFingerprint(StringBuilder sb)
    {
        sb.Append("overlay(layers=").Append(Layers.Length).Append(':');
        for (var i = 0; i < Layers.Length; i++)
        {
            if (i > 0) sb.Append('|');
            var layer = Layers[i];
            switch (layer.Source)
            {
                case MediaOverlaySource media:
                    sb.Append("m=").Append(media.MediaId);
                    if (!string.IsNullOrEmpty(media.RecipeName))
                        sb.Append(",rcp=").Append(media.RecipeName);
                    break;
                case TextOverlaySource text:
                    sb.Append("t=").Append(text.Text);
                    if (!string.IsNullOrEmpty(text.Font))
                        sb.Append(",font=").Append(text.Font);
                    if (text.Color is { } c)
                        sb.Append(",c=").Append(c.ToCanonical());
                    sb.Append(",fs=").Append(text.FontSize);
                    break;
            }
            sb.Append(",sz=").Append(layer.Size.ToCanonical());
            sb.Append(",pos=").Append(layer.Position.ToCanonical());
            sb.Append(",pad=").Append(layer.Padding.ToCanonical());
            sb.Append(",op=").Append(layer.Opacity.ToString("0.##", CultureInfo.InvariantCulture));
            if (layer.Rotate != 0) sb.Append(",rot=").Append(layer.Rotate);
        }
        sb.Append(')');
    }

    // Overlay composition operates in pixel space. Vector / Timeline
    // sources are rejected upstream — they must Sample to Raster first.
    // Output kind is preserved (animated sources stay animated when the
    // engine drives per-frame composition).
    public override KindSet AcceptsFrom => KindSet.Of(MediaKind.Raster, MediaKind.AnimatedRaster);
    public override MediaKind? ProducesTo => null;
}

/// <summary>
/// Metadata stripping. Stage <see cref="PipelineStage.Metadata"/>.
/// </summary>
public sealed record StripStep(MetadataKinds Kinds, string? Name = null, bool Primary = false)
    : MediaStep(PipelineStage.Metadata, Name, Primary)
{
    public override void WriteFingerprint(StringBuilder sb) =>
        sb.Append("strip(").Append((int)Kinds).Append(')');

    // Metadata strip is a passthrough across all kinds — the engine
    // simply drops the requested metadata blocks where they exist.
    public override KindSet AcceptsFrom => KindSet.All;
    public override MediaKind? ProducesTo => null;
}

[Flags]
public enum MetadataKinds
{
    None = 0,
    Exif = 1 << 0,
    Icc = 1 << 1,
    Xmp = 1 << 2,
    All = Exif | Icc | Xmp,
}

/// <summary>
/// Terminal encode step — always last. Stage <see cref="PipelineStage.Encode"/>.
/// <see cref="Format"/> null means "preserve source format" — the
/// format-preservation default that fixes the DX-0047 defect.
/// </summary>
public sealed record EncodeStep(
    string? Format,
    int Quality = Recipes.Quality.Web,
    string? Name = null,
    bool Primary = false)
    : MediaStep(PipelineStage.Encode, Name, Primary)
{
    /// <summary>True when this step preserves the source format.</summary>
    public bool PreservesFormat => Format is null;

    public override void WriteFingerprint(StringBuilder sb)
    {
        sb.Append("encode(fmt=").Append(Format ?? "source");
        sb.Append(",q=").Append(Quality.ToString(CultureInfo.InvariantCulture)).Append(')');
    }

    // Encode admission is enforced by the planner's terminal-encoder gate
    // (MEDIA-0005 §4) using the EncoderAccepts table — Abstractions cannot
    // reference Core, so the per-step gate stays permissive. The terminal
    // gate is also where the implicit Rasterize bridge fires for Vector
    // sources; lifting admission into this step would short-circuit that
    // forward-derivation.
    public override KindSet AcceptsFrom => KindSet.All;
    public override MediaKind? ProducesTo => null;
}

/// <summary>
/// Format-changing destructive step — explicit caller intent. May
/// drop animation, alpha, and color depth depending on the target
/// format. Engine logs at Information level when invoked.
/// </summary>
public sealed record FlattenToStep(
    string Format,
    int Quality = Recipes.Quality.Web,
    string? Name = null,
    bool Primary = false)
    : MediaStep(PipelineStage.Encode, Name, Primary)
{
    public override void WriteFingerprint(StringBuilder sb)
    {
        sb.Append("flatten(fmt=").Append(Format);
        sb.Append(",q=").Append(Quality.ToString(CultureInfo.InvariantCulture)).Append(')');
    }

    // FlattenTo is the explicit destructive collapse — admits every kind
    // and forces a still Raster result regardless of source animation.
    public override KindSet AcceptsFrom => KindSet.All;
    public override MediaKind? ProducesTo => MediaKind.Raster;
}
