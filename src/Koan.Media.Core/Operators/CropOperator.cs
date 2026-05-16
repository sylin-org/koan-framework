using Koan.Storage.Abstractions;

namespace Koan.Media.Core.Operators;

using Microsoft.Extensions.Primitives;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using Koan.Media.Core.Options;
using Koan.Storage;

/// <summary>
/// Crops the source image to a smaller region. Two modes:
/// <list type="bullet">
/// <item>
///   <b>Aspect-ratio crop</b> (<c>crop=1:1</c>, <c>crop=16:9</c>, or named <c>crop=square</c>):
///   selects the largest region of that ratio centered on the image. The anchor can be biased
///   via <c>anchor=top|bottom|left|right|topleft|topright|bottomleft|bottomright|center</c>.
/// </item>
/// <item>
///   <b>Absolute crop</b> (<c>cx=…&amp;cy=…&amp;cw=…&amp;ch=…</c>): pixel-precise rectangle. All
///   four values must be present and within the source bounds.
/// </item>
/// </list>
/// In a chained pipeline Crop sits between <c>rotate@1</c> and <c>resize@1</c>: orient the bytes,
/// pick the region, then size it. Output is PNG so downstream operators can re-encode losslessly.
/// </summary>
public sealed class CropOperator : IMediaOperator
{
    public string Id => "crop@1";
    public MediaOperatorPlacement Placement => MediaOperatorPlacement.Free;

    public IReadOnlyDictionary<string, IReadOnlySet<string>> ParameterAliases { get; } = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
    {
        ["crop"] = new HashSet<string>(new[] { "crop", "ratio" }, StringComparer.OrdinalIgnoreCase),
        ["anchor"] = new HashSet<string>(new[] { "anchor", "gravity" }, StringComparer.OrdinalIgnoreCase),
        ["cx"] = new HashSet<string>(new[] { "cx" }, StringComparer.OrdinalIgnoreCase),
        ["cy"] = new HashSet<string>(new[] { "cy" }, StringComparer.OrdinalIgnoreCase),
        ["cw"] = new HashSet<string>(new[] { "cw" }, StringComparer.OrdinalIgnoreCase),
        ["ch"] = new HashSet<string>(new[] { "ch" }, StringComparer.OrdinalIgnoreCase),
    };

    public IReadOnlyList<string> SupportedContentTypes { get; } = new[] { "image/" };

    public IReadOnlyDictionary<string, string>? Normalize(IDictionary<string, StringValues> query, ObjectStat sourceStat, MediaTransformOptions options, bool strict)
    {
        var hasAny = ParameterAliases.Any(k => k.Value.Any(a => query.ContainsKey(a)));
        if (!hasAny) return null;

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var hasAbsolute = false;

        foreach (var canon in new[] { "cx", "cy", "cw", "ch" })
        {
            if (TryGet(query, ParameterAliases[canon], out var raw))
            {
                if (!int.TryParse(raw, out var v) || v < 0)
                {
                    if (strict) throw new ArgumentException($"crop: {canon} must be a non-negative integer.");
                    return null;
                }
                map[canon] = v.ToString();
                hasAbsolute = true;
            }
        }

        if (hasAbsolute && (!map.ContainsKey("cx") || !map.ContainsKey("cy") || !map.ContainsKey("cw") || !map.ContainsKey("ch")))
        {
            if (strict) throw new ArgumentException("crop: absolute crop requires all four of cx, cy, cw, ch.");
            return null;
        }

        if (TryGet(query, ParameterAliases["crop"], out var ratio))
        {
            if (hasAbsolute)
            {
                if (strict) throw new ArgumentException("crop: aspect ratio and absolute rectangle are mutually exclusive.");
                return null;
            }
            var normalized = NormalizeRatio(ratio);
            if (normalized is null)
            {
                if (strict) throw new ArgumentException($"crop: unrecognised aspect '{ratio}'. Use w:h (e.g. 16:9) or a name (square, portrait, landscape).");
                return null;
            }
            map["crop"] = normalized;
        }

        if (TryGet(query, ParameterAliases["anchor"], out var anchor))
        {
            var normalized = NormalizeAnchor(anchor);
            if (normalized is null)
            {
                if (strict) throw new ArgumentException($"crop: unrecognised anchor '{anchor}'.");
                return null;
            }
            map["anchor"] = normalized;
        }

        if (map.Count == 0) return null;
        return map;
    }

    public async Task<(string? ContentType, long BytesWritten)> Execute(Stream source, string sourceContentType, Stream destination, IReadOnlyDictionary<string, string> parameters, MediaTransformOptions options, CancellationToken ct)
    {
        if (source.CanSeek) { try { source.Position = 0; } catch { } }
        using var image = await Image.LoadAsync(source, ct);

        if (parameters.TryGetValue("cx", out var sx) && parameters.TryGetValue("cy", out var sy) &&
            parameters.TryGetValue("cw", out var sw) && parameters.TryGetValue("ch", out var sh) &&
            int.TryParse(sx, out var x) && int.TryParse(sy, out var y) &&
            int.TryParse(sw, out var w) && int.TryParse(sh, out var h))
        {
            // Clamp absolute rect inside the source frame so caller-side off-by-ones don't 500.
            w = Math.Min(w, Math.Max(0, image.Width - x));
            h = Math.Min(h, Math.Max(0, image.Height - y));
            if (w > 0 && h > 0)
            {
                image.Mutate(img => img.Crop(new Rectangle(x, y, w, h)));
            }
        }
        else if (parameters.TryGetValue("crop", out var ratioCanonical))
        {
            var aspect = ParseCanonicalRatio(ratioCanonical);
            var anchor = parameters.TryGetValue("anchor", out var a) ? a : "center";
            ApplyAspectCrop(image, aspect, anchor);
        }

        await image.SaveAsPngAsync(destination, cancellationToken: ct);
        return ("image/png", destination.CanSeek ? destination.Length : 0);
    }

    private static void ApplyAspectCrop(Image image, double aspect, string anchor)
    {
        var current = image.Width / (double)image.Height;
        int cropW, cropH, cropX, cropY;
        if (current > aspect)
        {
            cropH = image.Height;
            cropW = (int)Math.Round(cropH * aspect);
            cropY = 0;
            cropX = HorizontalOffset(anchor, image.Width, cropW);
        }
        else
        {
            cropW = image.Width;
            cropH = (int)Math.Round(cropW / aspect);
            cropX = 0;
            cropY = VerticalOffset(anchor, image.Height, cropH);
        }
        if (cropW > 0 && cropH > 0 && (cropW != image.Width || cropH != image.Height))
        {
            image.Mutate(img => img.Crop(new Rectangle(cropX, cropY, cropW, cropH)));
        }
    }

    private static int HorizontalOffset(string anchor, int total, int crop) => anchor switch
    {
        "left" or "topleft" or "bottomleft" => 0,
        "right" or "topright" or "bottomright" => total - crop,
        _ => (total - crop) / 2,
    };

    private static int VerticalOffset(string anchor, int total, int crop) => anchor switch
    {
        "top" or "topleft" or "topright" => 0,
        "bottom" or "bottomleft" or "bottomright" => total - crop,
        _ => (total - crop) / 2,
    };

    /// <summary>Normalize a user-facing crop ratio to canonical <c>"w:h"</c> form.</summary>
    private static string? NormalizeRatio(string raw)
    {
        var lower = raw.Trim().ToLowerInvariant();
        switch (lower)
        {
            case "square": return "1:1";
            case "portrait": return "3:4";
            case "landscape": return "4:3";
            case "wide": return "16:9";
            case "ultrawide": return "21:9";
        }
        var parts = lower.Split(':', '/');
        if (parts.Length != 2) return null;
        if (!double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w) ||
            !double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h))
            return null;
        if (w <= 0 || h <= 0) return null;
        return $"{w.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{h.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }

    private static double ParseCanonicalRatio(string canonical)
    {
        var parts = canonical.Split(':');
        var w = double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
        var h = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
        return w / h;
    }

    private static string? NormalizeAnchor(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "center" or "c" or "" => "center",
        "top" or "t" or "n" => "top",
        "bottom" or "b" or "s" => "bottom",
        "left" or "l" or "w" => "left",
        "right" or "r" or "e" => "right",
        "topleft" or "tl" or "nw" => "topleft",
        "topright" or "tr" or "ne" => "topright",
        "bottomleft" or "bl" or "sw" => "bottomleft",
        "bottomright" or "br" or "se" => "bottomright",
        _ => null,
    };

    private static bool TryGet(IDictionary<string, StringValues> query, IReadOnlySet<string> aliases, out string value)
    {
        foreach (var a in aliases)
        {
            if (query.TryGetValue(a, out var sv) && sv.Count > 0)
            {
                value = sv[0] ?? "";
                return true;
            }
        }
        value = "";
        return false;
    }
}
