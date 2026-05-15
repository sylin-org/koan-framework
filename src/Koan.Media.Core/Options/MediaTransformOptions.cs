namespace Koan.Media.Core.Options;

public sealed class MediaTransformOptions
{
    // Global strictness for unknown params and duplicate handling
    public MediaTransformStrictness Strictness { get; set; } = MediaTransformStrictness.Relaxed;

    // Default pipeline order: orient the bytes, then pick a region, then size it, then pad if a
    // target aspect is asked for, then encode. Each operator self-skips when no relevant params
    // are present, so the only cost of an idle stage is the alias-overlap check.
    public IReadOnlyList<string> Precedence { get; set; } = new[] { "rotate@1", "crop@1", "resize@1", "pad@1", "typeConverter@1" };

    // Limits
    public int MaxWidth { get; set; } = 4096;
    public int MaxHeight { get; set; } = 4096;
    public bool AllowUpscale { get; set; } = false;
    public int MaxQuality { get; set; } = 95;
    public string DefaultBackground { get; set; } = "#ffffff"; // for JPEG from transparent sources

    // Future: per-entity overrides via attributes
}
