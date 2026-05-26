using System.Globalization;

namespace Koan.Media.Abstractions.Recipes;

/// <summary>
/// Pipeline-level background applied wherever the pipeline introduces
/// blank pixels: <c>fit=contain</c> letterboxing, off-bounds pixel
/// crops, non-orthogonal rotations, explicit pad operations. Per
/// MEDIA-0004 §6 there is one global background per recipe; no
/// per-step <c>padColor</c>/<c>letterboxColor</c>/<c>rotateFill</c>
/// proliferation.
///
/// Discriminated by <see cref="Kind"/>. Use the static factories
/// rather than constructing directly.
/// </summary>
public sealed record Background(
    BackgroundKind Kind,
    BackgroundColor Color,
    int BlurRadius,
    BackgroundColor FallbackColor)
{
    /// <summary>Transparent default; falls back to <see cref="FallbackColor"/> on alpha-incapable outputs (JPEG).</summary>
    public static Background Transparent(BackgroundColor? fallback = null) =>
        new(BackgroundKind.Transparent, BackgroundColor.Transparent, 0, fallback ?? BackgroundColor.White);

    /// <summary>Solid named or hex color.</summary>
    public static Background Solid(BackgroundColor color) =>
        new(BackgroundKind.Solid, color, 0, BackgroundColor.White);

    /// <summary>Sample border pixels of the source, average to a solid color (per-source-hash cached).</summary>
    public static Background Auto(BackgroundColor? fallback = null) =>
        new(BackgroundKind.Auto, BackgroundColor.Transparent, 0, fallback ?? BackgroundColor.White);

    /// <summary>k-means dominant color of the source (per-source-hash cached).</summary>
    public static Background Dominant(BackgroundColor? fallback = null) =>
        new(BackgroundKind.Dominant, BackgroundColor.Transparent, 0, fallback ?? BackgroundColor.White);

    /// <summary>Source upscaled + Gaussian blurred at <paramref name="radius"/>, contained image composited on top.</summary>
    public static Background Blur(int radius = 0) =>
        new(BackgroundKind.Blur, BackgroundColor.Transparent, radius, BackgroundColor.Black);

    public string ToCanonical() => Kind switch
    {
        BackgroundKind.Transparent => $"transparent|fallback={FallbackColor.ToCanonical()}",
        BackgroundKind.Solid => Color.ToCanonical(),
        BackgroundKind.Auto => $"auto|fallback={FallbackColor.ToCanonical()}",
        BackgroundKind.Dominant => $"dominant|fallback={FallbackColor.ToCanonical()}",
        BackgroundKind.Blur => $"blur|r={BlurRadius}",
        _ => "transparent",
    };

    /// <summary>
    /// Parse a URL-style background value. Accepts <c>transparent</c>,
    /// CSS color names (<c>black</c>/<c>white</c>/...), bare hex
    /// (<c>1a1a1a</c> or <c>1a1a1aff</c> — no leading <c>#</c>),
    /// <c>rgba:r,g,b,a</c>, <c>auto</c>, <c>dominant</c>, and
    /// <c>blur</c>. Returns true on success.
    /// </summary>
    public static bool TryParse(string? value, out Background bg)
    {
        bg = Transparent();
        if (string.IsNullOrWhiteSpace(value)) return false;
        var v = value.Trim().ToLowerInvariant();

        switch (v)
        {
            case "transparent": bg = Transparent(); return true;
            case "auto": bg = Auto(); return true;
            case "dominant": bg = Dominant(); return true;
            case "blur": bg = Blur(); return true;
        }

        if (BackgroundColor.TryParse(v, out var color))
        {
            bg = Solid(color);
            return true;
        }
        return false;
    }
}

public enum BackgroundKind
{
    Transparent,
    Solid,
    Auto,
    Dominant,
    Blur,
}

/// <summary>
/// RGBA color with 0–255 channels. Constructed from named colors,
/// hex (with or without alpha), or decimal <c>rgba:</c> form. Equality
/// is value-based; canonical form is lowercase hex.
/// </summary>
public readonly record struct BackgroundColor(byte R, byte G, byte B, byte A)
{
    public static BackgroundColor Transparent { get; } = new(0, 0, 0, 0);
    public static BackgroundColor Black { get; } = new(0, 0, 0, 255);
    public static BackgroundColor White { get; } = new(255, 255, 255, 255);

    public string ToCanonical() =>
        A == 255
            ? string.Create(CultureInfo.InvariantCulture, $"{R:x2}{G:x2}{B:x2}")
            : string.Create(CultureInfo.InvariantCulture, $"{R:x2}{G:x2}{B:x2}{A:x2}");

    public static bool TryParse(string? raw, out BackgroundColor color)
    {
        color = Black;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var v = raw.Trim().ToLowerInvariant();

        // Named: black, white, red, green, blue, gray/grey
        switch (v)
        {
            case "transparent": color = Transparent; return true;
            case "black": color = Black; return true;
            case "white": color = White; return true;
            case "red": color = new BackgroundColor(255, 0, 0, 255); return true;
            case "green": color = new BackgroundColor(0, 128, 0, 255); return true;
            case "blue": color = new BackgroundColor(0, 0, 255, 255); return true;
            case "gray" or "grey": color = new BackgroundColor(128, 128, 128, 255); return true;
            case "silver": color = new BackgroundColor(192, 192, 192, 255); return true;
        }

        // rgba: form
        if (v.StartsWith("rgba:", StringComparison.Ordinal))
        {
            var parts = v[5..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 4) return false;
            if (!byte.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ||
                !byte.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var g) ||
                !byte.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
            {
                return false;
            }
            byte a = 255;
            // alpha may be 0-1 fraction or 0-255 integer
            if (double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var alphaRaw))
            {
                a = alphaRaw <= 1.0
                    ? (byte)Math.Round(Math.Clamp(alphaRaw, 0.0, 1.0) * 255)
                    : (byte)Math.Clamp(alphaRaw, 0.0, 255.0);
            }
            else return false;
            color = new BackgroundColor(r, g, b, a);
            return true;
        }

        // Bare hex: 6 chars (RGB) or 8 chars (RGBA), no leading '#'
        if ((v.Length == 6 || v.Length == 8) &&
            v.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
        {
            byte r = byte.Parse(v.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte g = byte.Parse(v.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte b = byte.Parse(v.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte a = v.Length == 8
                ? byte.Parse(v.AsSpan(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                : (byte)255;
            color = new BackgroundColor(r, g, b, a);
            return true;
        }

        return false;
    }
}
