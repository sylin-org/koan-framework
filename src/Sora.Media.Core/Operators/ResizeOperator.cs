namespace Sora.Media.Core.Operators;

using Microsoft.Extensions.Primitives;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using Sora.Media.Core.Options;
using Sora.Storage;

public sealed class ResizeOperator : IMediaOperator
{
    public string Id => "resize@1";
    public MediaOperatorPlacement Placement => MediaOperatorPlacement.Free;

    public IReadOnlyDictionary<string, IReadOnlySet<string>> ParameterAliases { get; } = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
    {
        ["w"] = new HashSet<string>(new[] { "w", "width" }, StringComparer.OrdinalIgnoreCase),
        ["h"] = new HashSet<string>(new[] { "h", "height" }, StringComparer.OrdinalIgnoreCase),
        ["fit"] = new HashSet<string>(new[] { "fit", "mode" }, StringComparer.OrdinalIgnoreCase),
        ["q"] = new HashSet<string>(new[] { "q", "quality" }, StringComparer.OrdinalIgnoreCase),
        ["bg"] = new HashSet<string>(new[] { "bg", "background" }, StringComparer.OrdinalIgnoreCase),
        ["up"] = new HashSet<string>(new[] { "up", "upscale" }, StringComparer.OrdinalIgnoreCase)
    };

    public IReadOnlyList<string> SupportedContentTypes { get; } = new[] { "image/" };

    public IReadOnlyDictionary<string, string>? Normalize(IDictionary<string, StringValues> query, ObjectStat sourceStat, MediaTransformOptions options, bool strict)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        if (TryGet(query, ParameterAliases["w"], out var w)) map["w"] = w;
        if (TryGet(query, ParameterAliases["h"], out var h)) map["h"] = h;
        if (TryGet(query, ParameterAliases["fit"], out var fit)) map["fit"] = fit.ToLowerInvariant();
        if (TryGet(query, ParameterAliases["q"], out var q)) map["q"] = q;
        if (TryGet(query, ParameterAliases["bg"], out var bg)) map["bg"] = bg;
        if (TryGet(query, ParameterAliases["up"], out var up)) map["up"] = up.ToLowerInvariant();

        if (map.Count == 0) return null;

        if (!map.ContainsKey("w") && !map.ContainsKey("h"))
        {
            if (strict) throw new ArgumentException("resize: at least one of w or h is required.");
            return null;
        }

        if (map.TryGetValue("w", out var sw) && int.TryParse(sw, out var wi))
        {
            wi = Math.Clamp(wi, 1, options.MaxWidth);
            map["w"] = wi.ToString();
        }
        if (map.TryGetValue("h", out var sh) && int.TryParse(sh, out var hi))
        {
            hi = Math.Clamp(hi, 1, options.MaxHeight);
            map["h"] = hi.ToString();
        }
        if (map.TryGetValue("q", out var sq) && int.TryParse(sq, out var qi))
        {
            qi = Math.Clamp(qi, 1, options.MaxQuality);
            map["q"] = qi.ToString();
        }

        return map;
    }

    public async Task<(string? ContentType, long BytesWritten)> Execute(Stream source, string sourceContentType, Stream destination, IReadOnlyDictionary<string, string> parameters, MediaTransformOptions options, CancellationToken ct)
    {
        if (source.CanSeek)
        {
            try { source.Position = 0; } catch { /* best effort */ }
        }
        Image image;
        try
        {
            image = await Image.LoadAsync(source, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("resize: failed to decode source image stream", ex);
        }
        using var _ = image;

        int? w = parameters.TryGetValue("w", out var sw) && int.TryParse(sw, out var wi) ? wi : null;
        int? h = parameters.TryGetValue("h", out var sh) && int.TryParse(sh, out var hi) ? hi : null;
        var fit = parameters.TryGetValue("fit", out var sf) ? sf : "contain";

        var resizeOptions = new ResizeOptions
        {
            Mode = fit switch
            {
                "cover" => ResizeMode.Crop,
                "fill" => ResizeMode.Stretch,
                "inside" => ResizeMode.Min,
                "outside" => ResizeMode.Max,
                _ => ResizeMode.Pad,
            },
            Size = new Size(w ?? 0, h ?? 0)
        };
        image.Mutate(x => x.Resize(resizeOptions));

        // Preserve source format; quality handled by typeConverter when converting
        await image.SaveAsPngAsync(destination, cancellationToken: ct).ConfigureAwait(false);
        return ("image/png", destination.CanSeek ? destination.Length : 0);
    }

    private static bool TryGet(IDictionary<string, StringValues> query, IReadOnlySet<string> aliases, out string value)
    {
        foreach (var a in aliases)
        {
            if (query.TryGetValue(a, out var sv) && sv.Count > 0)
            {
                value = sv[0];
                return true;
            }
        }
        value = string.Empty;
        return false;
    }
}
