namespace Sora.Media.Core.Operators;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sora.Storage;

public static class SignatureUtility
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static (string Hash, string Json) BuildSignature(string srcId, ObjectStat stat, IReadOnlyList<(IMediaOperator Op, IReadOnlyDictionary<string, string> Params)> ops)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["v"] = 1,
            ["src"] = srcId,
            ["etag"] = stat.ETag,
        };

        var steps = new List<Dictionary<string, object?>>();
        foreach (var (op, pars) in ops)
        {
            var step = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["op"] = op.Id,
            };

            // Sort keys for determinism
            foreach (var kv in pars.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                step[kv.Key] = kv.Value;
            }

            steps.Add(step);
        }
        payload["ops"] = steps;

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
        var base64 = Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return (base64, json);
    }
}
