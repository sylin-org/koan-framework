namespace Koan.Media.Abstractions.Recipes;

/// <summary>
/// Result of <c>IMediaPipeline.Probe(ct)</c>. Lets callers branch on
/// source properties without re-decoding. Per MEDIA-0004 §4.
/// </summary>
public sealed record MediaInfo(
    string Format,
    int Width,
    int Height,
    int FrameCount,
    bool HasAlpha,
    int ColorDepth,
    int? ExifOrientation,
    bool HasIccProfile)
{
    /// <summary>True when <see cref="FrameCount"/> is greater than 1.</summary>
    public bool IsAnimated => FrameCount > 1;
}
