namespace Koan.Media.Core.Options;

public sealed class MediaTransformOptions
{
    // Global strictness for unknown params and duplicate handling
    public MediaTransformStrictness Strictness { get; set; } = MediaTransformStrictness.Relaxed;

    // Fixed v1 precedence: rotate -> resize -> typeConverter (can be overridden per-entity later)
    public IReadOnlyList<string> Precedence { get; set; } = new[] { "rotate@1", "resize@1", "typeConverter@1" };

    // Limits
    public int MaxWidth { get; set; } = 4096;
    public int MaxHeight { get; set; } = 4096;
    public bool AllowUpscale { get; set; } = false;
    public int MaxQuality { get; set; } = 95;
    public string DefaultBackground { get; set; } = "#ffffff"; // for JPEG from transparent sources

    // Future: per-entity overrides via attributes
}
