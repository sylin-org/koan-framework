using Koan.Media.Abstractions.Recipes;

namespace Koan.Media.Core.Pipeline;

/// <summary>
/// Declared input admission for each registered encoder. Per
/// MEDIA-0005 §3 — encoder capability is data, not a switch case in
/// the picker.
///
/// Adding a new encoder is a one-line registration here plus the
/// encoder binding in <see cref="EncoderSelector"/>.
/// </summary>
public static class EncoderAccepts
{
    private static readonly IReadOnlyDictionary<string, KindSet> _map = new Dictionary<string, KindSet>(StringComparer.OrdinalIgnoreCase)
    {
        // Still-only encoders.
        ["jpeg"] = KindSet.Of(MediaKind.Raster),
        ["png"] = KindSet.Of(MediaKind.Raster),
        ["bmp"] = KindSet.Of(MediaKind.Raster),
        ["tiff"] = KindSet.Of(MediaKind.Raster),

        // Animated-capable encoders.
        ["webp"] = KindSet.Of(MediaKind.Raster, MediaKind.AnimatedRaster),
        ["gif"] = KindSet.Of(MediaKind.Raster, MediaKind.AnimatedRaster),

        // AVIF: ImageSharp's AvifEncoder is not in the current package
        // surface; documented as still-only for forward-compat. When
        // animated-AVIF lands, this is a one-line change.
        ["avif"] = KindSet.Of(MediaKind.Raster),
    };

    /// <summary>
    /// Set of kinds the named encoder accepts. Returns
    /// <see cref="KindSet.None"/> for unknown slugs (planner will
    /// produce an EncoderRefused-equivalent error against an empty set).
    /// </summary>
    public static KindSet AcceptsFor(string formatSlug)
    {
        if (string.IsNullOrWhiteSpace(formatSlug)) return KindSet.None;
        return _map.TryGetValue(formatSlug, out var set) ? set : KindSet.None;
    }

    /// <summary>True when the named encoder admits <see cref="MediaKind.AnimatedRaster"/>.</summary>
    public static bool IsAnimatedCapable(string formatSlug) =>
        AcceptsFor(formatSlug).Contains(MediaKind.AnimatedRaster);

    /// <summary>True when the encoder slug is registered.</summary>
    public static bool IsRegistered(string formatSlug) =>
        !string.IsNullOrWhiteSpace(formatSlug) && _map.ContainsKey(formatSlug);
}
