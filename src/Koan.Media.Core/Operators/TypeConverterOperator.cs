using Koan.Storage.Abstractions;

namespace Koan.Media.Core.Operators;

using Microsoft.Extensions.Primitives;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using Koan.Media.Core.Options;
using Koan.Storage;

public sealed class TypeConverterOperator : IMediaOperator
{
    public string Id => "typeConverter@1";
    public MediaOperatorPlacement Placement => MediaOperatorPlacement.Terminal;

    public IReadOnlyDictionary<string, IReadOnlySet<string>> ParameterAliases { get; } = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
    {
        ["format"] = new HashSet<string>(new[] { "format", "f" }, StringComparer.OrdinalIgnoreCase),
        ["q"] = new HashSet<string>(new[] { "q", "quality" }, StringComparer.OrdinalIgnoreCase),
        ["bg"] = new HashSet<string>(new[] { "bg", "background" }, StringComparer.OrdinalIgnoreCase),
        ["cs"] = new HashSet<string>(new[] { "cs", "colorspace" }, StringComparer.OrdinalIgnoreCase)
    };

    public IReadOnlyList<string> SupportedContentTypes { get; } = new[] { "image/" };

    public IReadOnlyDictionary<string, string>? Normalize(IDictionary<string, StringValues> query, ObjectStat sourceStat, MediaTransformOptions options, bool strict)
    {
        if (!TryGet(query, ParameterAliases["format"], out var fmt)) return null;
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["format"] = fmt.ToLowerInvariant()
        };
        if (TryGet(query, ParameterAliases["q"], out var q) && int.TryParse(q, out var qi))
        {
            qi = Math.Clamp(qi, 1, options.MaxQuality);
            map["q"] = qi.ToString();
        }
        if (TryGet(query, ParameterAliases["bg"], out var bg)) map["bg"] = bg;
        if (TryGet(query, ParameterAliases["cs"], out var cs)) map["cs"] = cs.ToLowerInvariant();
        return map;
    }

    public async Task<(string? ContentType, long BytesWritten)> Execute(Stream source, string sourceContentType, Stream destination, IReadOnlyDictionary<string, string> parameters, MediaTransformOptions options, CancellationToken ct)
    {
        if (source.CanSeek)
        {
            try { source.Position = 0; } catch { /* ignore */ }
        }
        Image image;
        try
        {
            image = await Image.LoadAsync(source, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("typeConverter: failed to decode source image stream", ex);
        }
        using var _ = image;
        var fmt = parameters.TryGetValue("format", out var f) ? f : "png";
        int? q = parameters.TryGetValue("q", out var sq) && int.TryParse(sq, out var qi) ? qi : null;

        switch (fmt)
        {
            case "jpg" or "jpeg":
                var encJpeg = new JpegEncoder { Quality = q ?? 82 };
                await image.SaveAsJpegAsync(destination, encJpeg, ct).ConfigureAwait(false);
                return ("image/jpeg", destination.CanSeek ? destination.Length : 0);
            case "png":
                var encPng = new PngEncoder();
                await image.SaveAsPngAsync(destination, encPng, ct).ConfigureAwait(false);
                return ("image/png", destination.CanSeek ? destination.Length : 0);
            case "webp":
                var encWebp = new WebpEncoder { Quality = q ?? 82 };
                await image.SaveAsWebpAsync(destination, encWebp, ct).ConfigureAwait(false);
                return ("image/webp", destination.CanSeek ? destination.Length : 0);
            default:
                throw new NotSupportedException($"Unsupported format '{fmt}'.");
        }
    }

    private static bool TryGet(IDictionary<string, StringValues> query, IReadOnlySet<string> aliases, out string value)
    {
        foreach (var a in aliases)
        {
            if (query.TryGetValue(a, out var sv) && sv.Count > 0)
            {
                var s0 = sv[0];
                value = s0 ?? string.Empty;
                return true;
            }
        }
        value = string.Empty;
        return false;
    }
}
