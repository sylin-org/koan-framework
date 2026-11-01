using SkiaSharp;

namespace Koan.Media.Core.Pipeline;

/// <summary>
/// Which formats the framework can write, and the writing of them. The heart of the format-preservation
/// guarantee: a recipe that does not pin a target keeps the source's format, or the writable format closest to
/// it when the source's own cannot be written (<see cref="PreservedFormat"/>).
/// </summary>
public static class EncoderSelector
{
    /// <summary>Canonical lowercase format slugs the encoder picker recognises.</summary>
    public static IReadOnlyList<string> SupportedFormats { get; } = new[]
    {
        "jpeg", "png", "webp",
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

    /// <summary>
    /// The format a recipe that pins no target writes for a source of this format. GIF becomes WebP, which
    /// keeps its animation and transparency; BMP, ICO and WBMP become PNG, which keeps every pixel.
    /// </summary>
    public static string PreservedFormat(string sourceFormat) => CanonicalizeSlug(sourceFormat) switch
    {
        "gif" => "webp",
        "bmp" or "ico" or "wbmp" => "png",
        var slug => slug,
    };

    /// <summary>True when the named format supports an alpha channel.</summary>
    public static bool SupportsAlpha(string format) => CanonicalizeSlug(format) switch
    {
        "png" or "webp" => true,
        _ => false,
    };

    /// <summary>True when the named format can carry multiple frames.</summary>
    public static bool SupportsAnimation(string format) => CanonicalizeSlug(format) == "webp";

    public static string ContentType(string format) => CanonicalizeSlug(format) switch
    {
        "jpeg" => "image/jpeg",
        "png" => "image/png",
        "webp" => "image/webp",
        _ => "application/octet-stream",
    };

    /// <summary>
    /// Encode into native memory. The encoders are synchronous and take no token, so the caller honours its
    /// token around this call and copies the result to its destination asynchronously. The result is bounded by
    /// the output pixels: the sizes the limits allowed, at most what an uncompressed frame of them would take.
    /// Exif, XMP, IPTC and format text are never written; the only metadata an encoder writes is the color
    /// profile of a bitmap that carries one.
    /// </summary>
    internal static SKData Encode(RasterImage image, string format, int quality)
    {
        var data = CanonicalizeSlug(format) switch
        {
            "jpeg" => EncodeJpeg(image, quality),
            "png" => EncodePng(image, quality),
            "webp" => EncodeWebp(image, quality),
            _ => throw new NotSupportedException(
                $"EncoderSelector: format '{format}' is not supported. Use one of: {string.Join(", ", SupportedFormats)}."),
        };
        return data ?? throw new InvalidOperationException($"The {format} encoder produced no output.");
    }

    private static SKData? EncodeJpeg(RasterImage image, int quality)
    {
        using var pixels = image.Frames[0].Bitmap.PeekPixels();
        return pixels.Encode(new SKJpegEncoderOptions(
            NormalizeQuality(quality, defaultQ: 80),
            SKJpegEncoderDownsample.Downsample420,
            SKJpegEncoderAlphaOption.BlendOnBlack));
    }

    private static SKData? EncodePng(RasterImage image, int quality)
    {
        var bitmap = image.Frames[0].Bitmap;
        var options = new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, MapPngCompression(quality));
        // A picture with no transparency in it is written without an alpha channel.
        if (IsOpaque(bitmap))
        {
            using var opaque = new SKPixmap(bitmap.Info.WithAlphaType(SKAlphaType.Opaque), bitmap.GetPixels(), bitmap.RowBytes);
            return opaque.Encode(options);
        }
        using var pixels = bitmap.PeekPixels();
        return pixels.Encode(options);
    }

    private static SKData? EncodeWebp(RasterImage image, int quality)
    {
        var options = quality < 0
            ? new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossless, 100)
            : new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, NormalizeQuality(quality, defaultQ: 80));

        if (image.Frames.Count == 1)
        {
            using var pixels = image.Frames[0].Bitmap.PeekPixels();
            return pixels.Encode(options);
        }

        var frames = image.Frames
            .Select(f => new SKWebpEncoderFrame(f.Bitmap, TimeSpan.FromMilliseconds(f.DurationMs)))
            .ToArray();
        return SKWebpEncoder.EncodeAnimated(frames, options);
    }

    private static bool IsOpaque(SKBitmap bitmap)
    {
        ReadOnlySpan<byte> pixels = bitmap.GetPixelSpan();
        for (var i = bitmap.BytesPerPixel - 1; i < pixels.Length; i += bitmap.BytesPerPixel)
        {
            if (pixels[i] != byte.MaxValue) return false;
        }
        return true;
    }

    private static int NormalizeQuality(int q, int defaultQ)
    {
        if (q < 0) return defaultQ;   // lossless sentinel → defer to encoder default
        if (q == 0) return 1;          // 0 is invalid for JPEG/WebP; treat as minimum
        return Math.Min(q, 100);
    }

    private static int MapPngCompression(int quality)
    {
        // Higher "quality" intent → spend more on compression (smaller output).
        // For PNG we treat quality as zlib effort.
        if (quality < 0) return 9;
        return quality switch
        {
            <= 10 => 0,
            <= 30 => 1,
            <= 85 => 6,
            _ => 9,
        };
    }
}
