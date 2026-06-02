using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Webp;

namespace Koan.Media.Core.Pipeline;

/// <summary>
/// Maps decoded format + caller intent → encoder. The heart of the
/// format-preservation guarantee that fixes the DX-0047 defect: when
/// the recipe doesn't pin an explicit target format, the encoder is
/// chosen from <see cref="Image.Metadata"/>.<c>DecodedImageFormat</c>
/// so animation, alpha, and color depth survive the round trip.
/// </summary>
public static class EncoderSelector
{
    /// <summary>Canonical lowercase format slugs the encoder picker recognises.</summary>
    public static IReadOnlyList<string> SupportedFormats { get; } = new[]
    {
        "jpeg", "png", "webp", "gif", "bmp", "tiff",
    };

    /// <summary>
    /// Normalise a format slug to its canonical producible form: trimmed, lowercased, with the "jpg"
    /// alias folded to "jpeg". This is the ONE canonicalizer — the recipe registry and recipe
    /// validators delegate here so an alias is never re-derived independently (DATA-0098 single source).
    /// </summary>
    public static string CanonicalizeSlug(string? slug) =>
        (slug ?? string.Empty).Trim().ToLowerInvariant() switch { "jpg" => "jpeg", var s => s };

    /// <summary>
    /// True when the framework has a concrete encoder for the given format slug (alias-aware). The
    /// single producibility authority: capability tables, the negotiator's fallthrough, the recipe
    /// shortcut resolver, and boot-time recipe validation all consult this rather than re-deriving it.
    /// </summary>
    public static bool CanProduce(string? slug) =>
        !string.IsNullOrWhiteSpace(slug)
        && SupportedFormats.Contains(CanonicalizeSlug(slug), StringComparer.OrdinalIgnoreCase);

    /// <summary>True when the named format supports an alpha channel.</summary>
    public static bool SupportsAlpha(string format) => format.ToLowerInvariant() switch
    {
        "png" or "webp" or "gif" or "tiff" => true,
        _ => false,
    };

    /// <summary>True when the named format can carry multiple frames.</summary>
    public static bool SupportsAnimation(string format) => format.ToLowerInvariant() switch
    {
        "webp" or "gif" or "png" => true, // PNG = APNG
        _ => false,
    };

    /// <summary>Map decoded format → canonical lowercase slug.</summary>
    public static string CanonicalSlug(IImageFormat? format) => format switch
    {
        JpegFormat => "jpeg",
        PngFormat => "png",
        WebpFormat => "webp",
        GifFormat => "gif",
        BmpFormat => "bmp",
        TiffFormat => "tiff",
        _ => "png", // safe default when input format is unknown — preserves alpha
    };

    public static string ContentType(string format) => format.ToLowerInvariant() switch
    {
        "jpeg" => "image/jpeg",
        "png" => "image/png",
        "webp" => "image/webp",
        "gif" => "image/gif",
        "bmp" => "image/bmp",
        "tiff" => "image/tiff",
        _ => "application/octet-stream",
    };

    /// <summary>
    /// Pick the encoder for the given source format and target format
    /// slug. When <paramref name="targetFormat"/> is null, the source
    /// format is preserved. Quality is honoured per-encoder (JPEG +
    /// WebP take quality; PNG ignores).
    /// </summary>
    public static IImageEncoder For(IImageFormat? sourceFormat, string? targetFormat, int quality)
    {
        var slug = targetFormat is null ? CanonicalSlug(sourceFormat) : CanonicalizeSlug(targetFormat);
        return slug switch
        {
            "jpeg" => new JpegEncoder
            {
                Quality = NormalizeQuality(quality, defaultQ: 80),
            },
            "png" => new PngEncoder
            {
                // PNG is lossless; quality controls compression level (0=none, 9=max).
                // Map "quality" 0-100 to compression 0-9; default to deflate level 6 for balance.
                CompressionLevel = MapPngCompression(quality),
            },
            "webp" => quality < 0
                ? new WebpEncoder { FileFormat = WebpFileFormatType.Lossless }
                : new WebpEncoder
                {
                    Quality = NormalizeQuality(quality, defaultQ: 80),
                    FileFormat = WebpFileFormatType.Lossy,
                },
            "gif" => new GifEncoder(),
            "bmp" => new BmpEncoder(),
            "tiff" => new TiffEncoder(),
            _ => throw new NotSupportedException(
                $"EncoderSelector: format '{targetFormat}' is not supported. Use one of: {string.Join(", ", SupportedFormats)}."),
        };
    }

    private static int NormalizeQuality(int q, int defaultQ)
    {
        if (q < 0) return defaultQ;   // lossless sentinel → defer to encoder default
        if (q == 0) return 1;          // 0 is invalid for JPEG/WebP; treat as minimum
        return Math.Min(q, 100);
    }

    private static PngCompressionLevel MapPngCompression(int quality)
    {
        // Higher "quality" intent → spend more on compression (smaller output).
        // For PNG we treat quality as compression effort.
        if (quality < 0) return PngCompressionLevel.BestCompression;
        return quality switch
        {
            <= 10 => PngCompressionLevel.NoCompression,
            <= 30 => PngCompressionLevel.BestSpeed,
            <= 60 => PngCompressionLevel.DefaultCompression,
            <= 85 => PngCompressionLevel.Level6,
            _ => PngCompressionLevel.BestCompression,
        };
    }
}
