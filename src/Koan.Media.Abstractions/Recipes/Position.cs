using System.Globalization;

namespace Koan.Media.Abstractions.Recipes;

/// <summary>
/// CSS-aligned <c>object-position</c> anchor for shape crops and
/// overlay placement. Either a named anchor (<see cref="Anchor"/>),
/// per-axis percentages, or the special <see cref="UseFocus"/> sentinel
/// telling the pipeline to read the source media's stored focus point.
/// </summary>
public readonly record struct Position
{
    /// <summary>Named anchor; null when the position is percentage-based or focus-based.</summary>
    public PositionAnchor? Anchor { get; init; }

    /// <summary>Horizontal anchor in [0.0, 1.0]; ignored when <see cref="Anchor"/> is set.</summary>
    public double X { get; init; }

    /// <summary>Vertical anchor in [0.0, 1.0]; ignored when <see cref="Anchor"/> is set.</summary>
    public double Y { get; init; }

    /// <summary>When true, use the source media's stored focus point.</summary>
    public bool UseFocus { get; init; }

    public static Position Center { get; } = new() { Anchor = PositionAnchor.Center, X = 0.5, Y = 0.5 };
    public static Position Top { get; } = new() { Anchor = PositionAnchor.Top, X = 0.5, Y = 0.0 };
    public static Position Bottom { get; } = new() { Anchor = PositionAnchor.Bottom, X = 0.5, Y = 1.0 };
    public static Position Left { get; } = new() { Anchor = PositionAnchor.Left, X = 0.0, Y = 0.5 };
    public static Position Right { get; } = new() { Anchor = PositionAnchor.Right, X = 1.0, Y = 0.5 };
    public static Position TopLeft { get; } = new() { Anchor = PositionAnchor.TopLeft, X = 0.0, Y = 0.0 };
    public static Position TopRight { get; } = new() { Anchor = PositionAnchor.TopRight, X = 1.0, Y = 0.0 };
    public static Position BottomLeft { get; } = new() { Anchor = PositionAnchor.BottomLeft, X = 0.0, Y = 1.0 };
    public static Position BottomRight { get; } = new() { Anchor = PositionAnchor.BottomRight, X = 1.0, Y = 1.0 };
    public static Position Focus { get; } = new() { UseFocus = true, X = 0.5, Y = 0.5 };

    public static Position Percent(double x, double y) => new()
    {
        Anchor = null,
        X = Math.Clamp(x, 0.0, 1.0),
        Y = Math.Clamp(y, 0.0, 1.0),
    };

    /// <summary>
    /// Parse a URL-style position value. Accepts named anchors
    /// (<c>center</c>, <c>top</c>, <c>top-left</c>, …), single-percent
    /// (<c>33%</c> — applied to the cropped axis at render time),
    /// per-axis percentages (<c>x:33,y:50</c>), and <c>focus</c>.
    /// Returns true on success; false (with <see cref="Center"/>)
    /// when the input cannot be parsed.
    /// </summary>
    public static bool TryParse(string? value, out Position position)
    {
        position = Center;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var v = value.Trim().ToLowerInvariant();

        switch (v)
        {
            case "center": position = Center; return true;
            case "top": position = Top; return true;
            case "bottom": position = Bottom; return true;
            case "left": position = Left; return true;
            case "right": position = Right; return true;
            case "top-left" or "topleft" or "tl": position = TopLeft; return true;
            case "top-right" or "topright" or "tr": position = TopRight; return true;
            case "bottom-left" or "bottomleft" or "bl": position = BottomLeft; return true;
            case "bottom-right" or "bottomright" or "br": position = BottomRight; return true;
            case "focus": position = Focus; return true;
        }

        // Per-axis: x:33,y:50 — bare numbers are always treated as percentages here
        // (33 → 0.33). Explicit "%" suffix is tolerated for symmetry.
        if (v.Contains(':') && v.Contains(','))
        {
            double? x = null, y = null;
            foreach (var part in v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var idx = part.IndexOf(':');
                if (idx <= 0) return false;
                var key = part[..idx];
                var raw = part[(idx + 1)..].TrimEnd('%');
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct)) return false;
                var fraction = Math.Clamp(pct / 100.0, 0.0, 1.0);
                if (key == "x") x = fraction;
                else if (key == "y") y = fraction;
                else return false;
            }
            if (x is null || y is null) return false;
            position = Percent(x.Value, y.Value);
            return true;
        }

        // Single-percent: applied to whichever axis is being cropped.
        // Stored as X (renderer interprets contextually).
        if (TryParsePercentOrFraction(v, out var single))
        {
            position = new Position { Anchor = null, X = single, Y = 0.5 };
            return true;
        }

        return false;
    }

    private static bool TryParsePercentOrFraction(string raw, out double value)
    {
        raw = raw.Trim();
        if (raw.EndsWith('%'))
        {
            if (double.TryParse(raw[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
            {
                value = Math.Clamp(pct / 100.0, 0.0, 1.0);
                return true;
            }
            value = 0.5;
            return false;
        }
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var frac))
        {
            value = Math.Clamp(frac, 0.0, 1.0);
            return true;
        }
        value = 0.5;
        return false;
    }

    /// <summary>
    /// Render canonical form for fingerprint / introspection JSON.
    /// Named anchors round-trip as their slug; otherwise as <c>x:NN,y:NN</c>.
    /// </summary>
    public string ToCanonical()
    {
        if (UseFocus) return "focus";
        if (Anchor is { } a) return a switch
        {
            PositionAnchor.Center => "center",
            PositionAnchor.Top => "top",
            PositionAnchor.Bottom => "bottom",
            PositionAnchor.Left => "left",
            PositionAnchor.Right => "right",
            PositionAnchor.TopLeft => "top-left",
            PositionAnchor.TopRight => "top-right",
            PositionAnchor.BottomLeft => "bottom-left",
            PositionAnchor.BottomRight => "bottom-right",
            _ => "center",
        };
        var xPct = (int)Math.Round(X * 100);
        var yPct = (int)Math.Round(Y * 100);
        return string.Create(CultureInfo.InvariantCulture, $"x:{xPct},y:{yPct}");
    }
}

public enum PositionAnchor
{
    Center,
    Top,
    Bottom,
    Left,
    Right,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}
