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
}

/// <summary>EXIF-based auto-orient. Stage <see cref="PipelineStage.Orient"/>.</summary>
public sealed record AutoOrientStep(bool Keep = false) : MediaStep(PipelineStage.Orient, null, false)
{
    public override void WriteFingerprint(StringBuilder sb) =>
        sb.Append("orient(").Append(Keep ? "keep" : "auto").Append(')');
}

/// <summary>
/// Animated → still extraction. Stage <see cref="PipelineStage.Frame"/>.
/// No-op on static sources.
/// </summary>
public sealed record ExtractFrameStep(int Index, string? Name = null, bool Primary = false)
    : MediaStep(PipelineStage.Frame, Name, Primary)
{
    public override void WriteFingerprint(StringBuilder sb) =>
        sb.Append("frame(").Append(Index).Append(')');
}

/// <summary>Explicit rotation. Stage <see cref="PipelineStage.Rotate"/>.</summary>
public sealed record RotateStep(int Degrees, string? Name = null, bool Primary = false)
    : MediaStep(PipelineStage.Rotate, Name, Primary)
{
    public override void WriteFingerprint(StringBuilder sb) =>
        sb.Append("rotate(").Append(Degrees).Append(')');
}

public enum FlipAxis { Horizontal, Vertical }

/// <summary>Horizontal or vertical flip. Stage <see cref="PipelineStage.Rotate"/>.</summary>
public sealed record FlipStep(FlipAxis Axis, string? Name = null, bool Primary = false)
    : MediaStep(PipelineStage.Rotate, Name, Primary)
{
    public override void WriteFingerprint(StringBuilder sb) =>
        sb.Append("flip(").Append(Axis == FlipAxis.Horizontal ? 'h' : 'v').Append(')');
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
}

/// <summary>Resize step — single slot, stage <see cref="PipelineStage.Size"/>.</summary>
public sealed record ResizeStep(
    int? Width,
    int? Height,
    double Dpr = 1.0,
    string? Name = null,
    bool Primary = false)
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
        sb.Append(')');
    }
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
}

/// <summary>
/// Metadata stripping. Stage <see cref="PipelineStage.Metadata"/>.
/// </summary>
public sealed record StripStep(MetadataKinds Kinds, string? Name = null, bool Primary = false)
    : MediaStep(PipelineStage.Metadata, Name, Primary)
{
    public override void WriteFingerprint(StringBuilder sb) =>
        sb.Append("strip(").Append((int)Kinds).Append(')');
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
}
