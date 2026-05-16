using Koan.Storage.Abstractions;

namespace Koan.Media.Core.Operators;

using Microsoft.Extensions.Primitives;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Koan.Media.Core.Options;
using Koan.Storage;

/// <summary>
/// Adds letterbox/pillarbox padding around the source so the output reaches a target shape.
/// Three modes:
/// <list type="bullet">
/// <item>
///   <b>Named</b> (<c>pad=square</c>, <c>pad=16:9</c>, <c>pad=portrait</c>): pad to that aspect
///   ratio while keeping the source content unchanged. The output's smaller axis matches the
///   source dimensions; the longer axis grows.
/// </item>
/// <item>
///   <b>Absolute</b> (<c>padw=…&amp;padh=…</c>): pad to a specific WxH target. Source content is
///   centered inside that box.
/// </item>
/// </list>
/// The padding colour defaults to <see cref="MediaTransformOptions.DefaultBackground"/>; override
/// per-request with <c>padbg=#rrggbb</c> (or <c>padbg=transparent</c>).
/// </summary>
public sealed class PadOperator : IMediaOperator
{
    public string Id => "pad@1";
    public MediaOperatorPlacement Placement => MediaOperatorPlacement.Free;

    public IReadOnlyDictionary<string, IReadOnlySet<string>> ParameterAliases { get; } = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
    {
        ["pad"] = new HashSet<string>(new[] { "pad" }, StringComparer.OrdinalIgnoreCase),
        ["padw"] = new HashSet<string>(new[] { "padw" }, StringComparer.OrdinalIgnoreCase),
        ["padh"] = new HashSet<string>(new[] { "padh" }, StringComparer.OrdinalIgnoreCase),
        ["padbg"] = new HashSet<string>(new[] { "padbg", "padcolor", "padcolour" }, StringComparer.OrdinalIgnoreCase),
    };

    public IReadOnlyList<string> SupportedContentTypes { get; } = new[] { "image/" };

    public IReadOnlyDictionary<string, string>? Normalize(IDictionary<string, StringValues> query, ObjectStat sourceStat, MediaTransformOptions options, bool strict)
    {
        var hasAny = ParameterAliases.Any(k => k.Value.Any(a => query.ContainsKey(a)));
        if (!hasAny) return null;

        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        if (TryGet(query, ParameterAliases["pad"], out var aspectRaw))
        {
            var normalized = NormalizeAspect(aspectRaw);
            if (normalized is null)
            {
                if (strict) throw new ArgumentException($"pad: unrecognised aspect '{aspectRaw}'.");
                return null;
            }
            map["pad"] = normalized;
        }

        if (TryGet(query, ParameterAliases["padw"], out var rw) || TryGet(query, ParameterAliases["padh"], out var rh))
        {
            if (TryGet(query, ParameterAliases["padw"], out var pw))
            {
                if (!int.TryParse(pw, out var w) || w < 1)
                {
                    if (strict) throw new ArgumentException("pad: padw must be a positive integer.");
                    return null;
                }
                map["padw"] = Math.Min(w, options.MaxWidth).ToString();
            }
            if (TryGet(query, ParameterAliases["padh"], out var ph))
            {
                if (!int.TryParse(ph, out var h) || h < 1)
                {
                    if (strict) throw new ArgumentException("pad: padh must be a positive integer.");
                    return null;
                }
                map["padh"] = Math.Min(h, options.MaxHeight).ToString();
            }
        }

        if (map.ContainsKey("pad") && (map.ContainsKey("padw") || map.ContainsKey("padh")))
        {
            if (strict) throw new ArgumentException("pad: named aspect and absolute padw/padh are mutually exclusive.");
            return null;
        }

        if (TryGet(query, ParameterAliases["padbg"], out var bg))
        {
            map["padbg"] = bg.Trim().ToLowerInvariant();
        }

        if (!map.ContainsKey("pad") && !map.ContainsKey("padw") && !map.ContainsKey("padh"))
        {
            // padbg without a target is meaningless; quietly skip.
            return null;
        }
        return map;
    }

    public async Task<(string? ContentType, long BytesWritten)> Execute(Stream source, string sourceContentType, Stream destination, IReadOnlyDictionary<string, string> parameters, MediaTransformOptions options, CancellationToken ct)
    {
        if (source.CanSeek) { try { source.Position = 0; } catch { } }
        using var image = await Image.LoadAsync(source, ct);

        int targetW = image.Width;
        int targetH = image.Height;

        if (parameters.TryGetValue("pad", out var aspectCanonical))
        {
            (targetW, targetH) = AspectDimensions(image.Width, image.Height, ParseCanonicalAspect(aspectCanonical));
        }
        else
        {
            if (parameters.TryGetValue("padw", out var sw) && int.TryParse(sw, out var w)) targetW = Math.Max(image.Width, w);
            if (parameters.TryGetValue("padh", out var sh) && int.TryParse(sh, out var h)) targetH = Math.Max(image.Height, h);
        }

        if (targetW > image.Width || targetH > image.Height)
        {
            var bg = ResolveColor(parameters.TryGetValue("padbg", out var pb) ? pb : options.DefaultBackground);
            image.Mutate(x => x.Pad(targetW, targetH, bg));
        }

        await image.SaveAsPngAsync(destination, cancellationToken: ct);
        return ("image/png", destination.CanSeek ? destination.Length : 0);
    }

    private static (int W, int H) AspectDimensions(int currentW, int currentH, double aspect)
    {
        var currentAspect = currentW / (double)currentH;
        if (Math.Abs(currentAspect - aspect) < 0.001) return (currentW, currentH);
        if (currentAspect > aspect)
        {
            // Wider than target — pad height.
            var targetH = (int)Math.Round(currentW / aspect);
            return (currentW, targetH);
        }
        else
        {
            // Taller than target — pad width.
            var targetW = (int)Math.Round(currentH * aspect);
            return (targetW, currentH);
        }
    }

    private static string? NormalizeAspect(string raw)
    {
        var lower = raw.Trim().ToLowerInvariant();
        switch (lower)
        {
            case "square": return "1:1";
            case "portrait": return "3:4";
            case "landscape": return "4:3";
            case "wide": return "16:9";
            case "ultrawide": return "21:9";
            case "9:16": case "3:4": case "1:1": case "4:3": case "16:9": case "21:9": return lower;
        }
        var parts = lower.Split(':', '/');
        if (parts.Length != 2) return null;
        if (!double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w) ||
            !double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h))
            return null;
        if (w <= 0 || h <= 0) return null;
        return $"{w.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{h.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }

    private static double ParseCanonicalAspect(string canonical)
    {
        var parts = canonical.Split(':');
        var w = double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
        var h = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
        return w / h;
    }

    private static Color ResolveColor(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Color.Transparent;
        var trimmed = raw.Trim();
        if (string.Equals(trimmed, "transparent", StringComparison.OrdinalIgnoreCase)) return Color.Transparent;
        try { return Color.Parse(trimmed); }
        catch { return Color.Transparent; }
    }

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
