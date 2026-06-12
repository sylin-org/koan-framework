namespace Koan.Media.Abstractions.Recipes;

/// <summary>
/// Discriminated union of selectors for the <see cref="SampleStep"/>.
/// Sealed hierarchy — every selector kind is a sealed record below.
/// Per MEDIA-0005 §2.
/// </summary>
public abstract record FrameSelector
{
    private protected FrameSelector() { }

    /// <summary>Explicit frame index. Valid for <see cref="MediaKind.AnimatedRaster"/>.</summary>
    public sealed record Index(int Frame) : FrameSelector
    {
        public override string ToCanonical() =>
            $"index({Frame.ToString(System.Globalization.CultureInfo.InvariantCulture)})";
    }

    /// <summary>Time offset. Valid for <see cref="MediaKind.Timeline"/>.</summary>
    public sealed record Time(TimeSpan At) : FrameSelector
    {
        public override string ToCanonical() =>
            $"time({At.TotalMilliseconds.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)}ms)";
    }

    /// <summary>Decoder-provided thumbnail / canonical pose, falling back to first.</summary>
    public sealed record HeuristicBest() : FrameSelector
    {
        public override string ToCanonical() => "thumbnail";
    }

    /// <summary>Canonical fingerprint encoding for this selector.</summary>
    public abstract string ToCanonical();
}

/// <summary>
/// Static factory for <see cref="FrameSelector"/> values. Per
/// MEDIA-0005 §2 — the named collapse-point primitives a recipe author
/// reaches for.
/// </summary>
public static class Sample
{
    /// <summary>First available frame / earliest time. Equivalent to <c>FrameSelector.Index(0)</c>.</summary>
    public static FrameSelector First { get; } = new FrameSelector.Index(0);

    /// <summary>Explicit frame index. Equivalent to <c>FrameSelector.Index(n)</c>.</summary>
    public static FrameSelector Frame(int n) => new FrameSelector.Index(n);

    /// <summary>Sample at a given time offset. Equivalent to <c>FrameSelector.Time(t)</c>.</summary>
    public static FrameSelector At(TimeSpan t) => new FrameSelector.Time(t);

    /// <summary>Decoder-provided thumbnail if present, else first frame.</summary>
    public static FrameSelector Thumbnail { get; } = new FrameSelector.HeuristicBest();
}
