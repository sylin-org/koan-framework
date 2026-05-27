using System.Globalization;
using System.Text.RegularExpressions;

namespace Koan.Media.Abstractions.Recipes;

/// <summary>
/// Per-overlay-layer size declaration. Three discriminated shapes per
/// MEDIA-0004 §7:
/// <list type="bullet">
///   <item><see cref="Natural"/> — render at the overlay source's natural pixel size (default)</item>
///   <item><see cref="Fraction"/> — scale to a fraction of the host's longest edge (e.g. 0.1 = 10%)</item>
///   <item><see cref="Pixels"/> — explicit pixel dimensions (height 0 derives from aspect)</item>
/// </list>
/// </summary>
public readonly record struct OverlaySize
{
    public OverlaySizeKind Kind { get; init; }

    /// <summary>For <see cref="OverlaySizeKind.Fraction"/>: 0.0–1.0 of host's longest edge.</summary>
    public double FractionValue { get; init; }

    /// <summary>For <see cref="OverlaySizeKind.Pixels"/>: literal width.</summary>
    public int Width { get; init; }

    /// <summary>For <see cref="OverlaySizeKind.Pixels"/>: literal height (0 = derive from aspect).</summary>
    public int Height { get; init; }

    public static OverlaySize Natural { get; } = new() { Kind = OverlaySizeKind.Natural };

    public static OverlaySize Fraction(double fraction) => new()
    {
        Kind = OverlaySizeKind.Fraction,
        FractionValue = Math.Clamp(fraction, 0.0, 1.0),
    };

    public static OverlaySize Pixels(int width, int height = 0) => new()
    {
        Kind = OverlaySizeKind.Pixels,
        Width = width,
        Height = height,
    };

    private static readonly Regex PixelsRegex = new(
        @"^(?<w>\d{1,5})x(?<h>\d{1,5})?$",
        RegexOptions.Compiled);

    /// <summary>
    /// Parse a URL/config size value. Accepts:
    /// <list type="bullet">
    ///   <item>Empty / null → <see cref="Natural"/></item>
    ///   <item><c>0.1</c> / <c>0.08</c> → <see cref="Fraction"/></item>
    ///   <item><c>100x50</c> → <see cref="Pixels"/> with both dims</item>
    ///   <item><c>100x</c> → <see cref="Pixels"/> with width only, height derived</item>
    /// </list>
    /// </summary>
    public static bool TryParse(string? value, out OverlaySize size)
    {
        size = Natural;
        if (string.IsNullOrWhiteSpace(value)) return true;
        var v = value.Trim();

        // Canonical-form round trip: ToCanonical emits "natural" for the Natural kind.
        if (v.Equals("natural", StringComparison.OrdinalIgnoreCase))
        {
            size = Natural;
            return true;
        }

        // pixel form WxH (or Wx for width-only)
        var pm = PixelsRegex.Match(v);
        if (pm.Success)
        {
            var w = int.Parse(pm.Groups["w"].Value, CultureInfo.InvariantCulture);
            var h = pm.Groups["h"].Success && pm.Groups["h"].Length > 0
                ? int.Parse(pm.Groups["h"].Value, CultureInfo.InvariantCulture)
                : 0;
            if (w == 0) return false;
            size = Pixels(w, h);
            return true;
        }

        // fraction form (single decimal between 0 and 1, exclusive)
        if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var frac))
        {
            if (frac <= 0.0 || frac > 1.0) return false;
            size = Fraction(frac);
            return true;
        }

        return false;
    }

    public string ToCanonical() => Kind switch
    {
        OverlaySizeKind.Natural => "natural",
        OverlaySizeKind.Fraction => FractionValue.ToString("0.###", CultureInfo.InvariantCulture),
        OverlaySizeKind.Pixels when Height == 0 => string.Create(CultureInfo.InvariantCulture, $"{Width}x"),
        OverlaySizeKind.Pixels => string.Create(CultureInfo.InvariantCulture, $"{Width}x{Height}"),
        _ => "natural",
    };
}

public enum OverlaySizeKind
{
    Natural = 0,
    Fraction = 1,
    Pixels = 2,
}
