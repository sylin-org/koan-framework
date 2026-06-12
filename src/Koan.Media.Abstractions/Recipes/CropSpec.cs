using System.Globalization;
using System.Text.RegularExpressions;

namespace Koan.Media.Abstractions.Recipes;

/// <summary>
/// Output shape declaration for the shape stage. Forms accepted per
/// MEDIA-0004 §5:
/// <list type="bullet">
///   <item><c>square</c> → 1:1 ratio</item>
///   <item><c>16:9</c>, <c>4:3</c>, <c>21:9</c>, ... → aspect ratio</item>
///   <item><c>400x200</c> → literal pixel dimensions (anchor-respecting)</item>
///   <item><c>400x200+100,50</c> → literal pixel crop at explicit offset (anchor ignored)</item>
/// </list>
/// The shorthand <c>?aspect=</c> is accepted as an alias of
/// <c>?crop=</c> for the aspect-ratio forms; resolved during URL
/// parsing so the canonical recipe always uses a <see cref="CropSpec"/>.
/// </summary>
public readonly record struct CropSpec
{
    public CropSpecKind Kind { get; init; }

    /// <summary>For Aspect: width side of ratio. For Pixels: width in px. Zero otherwise.</summary>
    public int Width { get; init; }

    /// <summary>For Aspect: height side of ratio. For Pixels: height in px. Zero otherwise.</summary>
    public int Height { get; init; }

    /// <summary>For PixelsWithOffset only: explicit X offset.</summary>
    public int OffsetX { get; init; }

    /// <summary>For PixelsWithOffset only: explicit Y offset.</summary>
    public int OffsetY { get; init; }

    public static CropSpec Square { get; } = new() { Kind = CropSpecKind.Aspect, Width = 1, Height = 1 };

    public static CropSpec Aspect(int w, int h) => new() { Kind = CropSpecKind.Aspect, Width = w, Height = h };

    public static CropSpec Pixels(int w, int h) => new() { Kind = CropSpecKind.Pixels, Width = w, Height = h };

    public static CropSpec PixelsAt(int w, int h, int x, int y) => new()
    {
        Kind = CropSpecKind.PixelsWithOffset,
        Width = w,
        Height = h,
        OffsetX = x,
        OffsetY = y,
    };

    private static readonly Regex AspectRegex = new(
        @"^(?<w>\d{1,4}):(?<h>\d{1,4})$",
        RegexOptions.Compiled);

    private static readonly Regex PixelsRegex = new(
        @"^(?<w>\d{1,5})x(?<h>\d{1,5})(?:\+(?<ox>\d{1,5}),(?<oy>\d{1,5}))?$",
        RegexOptions.Compiled);

    public static bool TryParse(string? value, out CropSpec spec)
    {
        spec = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var v = value.Trim().ToLowerInvariant();

        if (v == "square")
        {
            spec = Square;
            return true;
        }

        var am = AspectRegex.Match(v);
        if (am.Success)
        {
            var w = int.Parse(am.Groups["w"].Value, CultureInfo.InvariantCulture);
            var h = int.Parse(am.Groups["h"].Value, CultureInfo.InvariantCulture);
            if (w == 0 || h == 0) return false;
            spec = Aspect(w, h);
            return true;
        }

        var pm = PixelsRegex.Match(v);
        if (pm.Success)
        {
            var w = int.Parse(pm.Groups["w"].Value, CultureInfo.InvariantCulture);
            var h = int.Parse(pm.Groups["h"].Value, CultureInfo.InvariantCulture);
            if (w == 0 || h == 0) return false;
            if (pm.Groups["ox"].Success)
            {
                var ox = int.Parse(pm.Groups["ox"].Value, CultureInfo.InvariantCulture);
                var oy = int.Parse(pm.Groups["oy"].Value, CultureInfo.InvariantCulture);
                spec = PixelsAt(w, h, ox, oy);
            }
            else
            {
                spec = Pixels(w, h);
            }
            return true;
        }

        return false;
    }

    public string ToCanonical() => Kind switch
    {
        CropSpecKind.Aspect when Width == 1 && Height == 1 => "square",
        CropSpecKind.Aspect => string.Create(CultureInfo.InvariantCulture, $"{Width}:{Height}"),
        CropSpecKind.Pixels => string.Create(CultureInfo.InvariantCulture, $"{Width}x{Height}"),
        CropSpecKind.PixelsWithOffset => string.Create(CultureInfo.InvariantCulture, $"{Width}x{Height}+{OffsetX},{OffsetY}"),
        _ => "",
    };
}

public enum CropSpecKind
{
    Aspect = 0,
    Pixels = 1,
    PixelsWithOffset = 2,
}
