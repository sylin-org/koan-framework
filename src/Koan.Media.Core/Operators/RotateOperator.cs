using Koan.Storage.Abstractions;

namespace Koan.Media.Core.Operators;

using Microsoft.Extensions.Primitives;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Processing;
using Koan.Media.Core.Options;
using Koan.Storage;

public sealed class RotateOperator : IMediaOperator
{
    public string Id => "rotate@1";
    public MediaOperatorPlacement Placement => MediaOperatorPlacement.Pre;

    public IReadOnlyDictionary<string, IReadOnlySet<string>> ParameterAliases { get; } = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
    {
        ["angle"] = new HashSet<string>(new[] { "angle", "a" }, StringComparer.OrdinalIgnoreCase),
        ["exif"] = new HashSet<string>(new[] { "exif", "autoorient", "orient" }, StringComparer.OrdinalIgnoreCase)
    };

    public IReadOnlyList<string> SupportedContentTypes { get; } = new[] { "image/" };

    public IReadOnlyDictionary<string, string>? Normalize(IDictionary<string, StringValues> query, ObjectStat sourceStat, MediaTransformOptions options, bool strict)
    {
        var hasAny = ParameterAliases.Any(k => k.Value.Any(a => query.ContainsKey(a)));
        if (!hasAny) return null;

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (TryGet(query, ParameterAliases["angle"], out var angle))
        {
            if (!int.TryParse(angle, out var a) || (a != 0 && a != 90 && a != 180 && a != 270))
            {
                if (strict) throw new ArgumentException("rotate: angle must be one of 0,90,180,270.");
            }
            else map["angle"] = a.ToString();
        }
        if (TryGet(query, ParameterAliases["exif"], out var exif))
            map["exif"] = exif.ToLowerInvariant();
        else map["exif"] = "true";

        return map;
    }

    public async Task<(string? ContentType, long BytesWritten)> Execute(Stream source, string sourceContentType, Stream destination, IReadOnlyDictionary<string, string> parameters, MediaTransformOptions options, CancellationToken ct)
    {
        Image image;
        try
        {
            image = await Image.LoadAsync(source, ct);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("rotate: failed to decode source image stream", ex);
        }
        using var _ = image;

        var exif = parameters.TryGetValue("exif", out var se) && bool.TryParse(se, out var b) ? b : true;
        if (exif)
        {
            image.Mutate(x => x.AutoOrient());
        }
        if (parameters.TryGetValue("angle", out var sa) && int.TryParse(sa, out var angle) && angle != 0)
        {
            image.Mutate(x => x.Rotate(angle));
        }

        await image.SaveAsPngAsync(destination, cancellationToken: ct);
        return ("image/png", destination.CanSeek ? destination.Length : 0);
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
