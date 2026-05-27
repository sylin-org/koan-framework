using System.Globalization;

namespace Koan.Media.Abstractions.Recipes;

/// <summary>
/// Per-overlay-layer padding (inset from the anchor point). Per
/// MEDIA-0004 §7, accepts either a fraction of the host's longest edge
/// (e.g. <c>0.05</c> = 5% inset) or a literal pixel value (e.g.
/// <c>20px</c>). Defaults to zero.
/// </summary>
public readonly record struct OverlayPadding
{
    /// <summary>True when this padding is expressed as a fraction of the host's longest edge.</summary>
    public bool IsFraction { get; init; }

    /// <summary>For fraction padding: 0.0–0.5 (anything above 0.5 collapses to no useful overlay space).</summary>
    public double FractionValue { get; init; }

    /// <summary>For pixel padding: literal inset in pixels.</summary>
    public int Pixels { get; init; }

    public static OverlayPadding Zero { get; } = default;

    public static OverlayPadding FromFraction(double fraction) => new()
    {
        IsFraction = true,
        FractionValue = Math.Clamp(fraction, 0.0, 0.5),
    };

    public static OverlayPadding FromPixels(int pixels) => new()
    {
        IsFraction = false,
        Pixels = Math.Max(0, pixels),
    };

    /// <summary>
    /// Parse a URL/config padding value. Accepts <c>0</c> (zero), a
    /// decimal fraction (<c>0.05</c>), or a pixel literal with the
    /// <c>px</c> suffix (<c>20px</c>).
    /// </summary>
    public static bool TryParse(string? value, out OverlayPadding padding)
    {
        padding = Zero;
        if (string.IsNullOrWhiteSpace(value)) return true;
        var v = value.Trim();

        if (v.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            var raw = v[..^2];
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var px) && px >= 0)
            {
                padding = FromPixels(px);
                return true;
            }
            return false;
        }

        if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var frac))
        {
            if (frac < 0.0 || frac > 0.5) return false;
            padding = FromFraction(frac);
            return true;
        }

        return false;
    }

    public string ToCanonical()
    {
        if (IsFraction)
        {
            if (FractionValue == 0.0) return "0";
            return FractionValue.ToString("0.###", CultureInfo.InvariantCulture);
        }
        return string.Create(CultureInfo.InvariantCulture, $"{Pixels}px");
    }
}
