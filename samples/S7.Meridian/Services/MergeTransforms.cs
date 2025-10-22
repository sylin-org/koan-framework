using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Samples.Meridian.Services;

/// <summary>
/// Registry of merge-time value normalization and enrichment transforms.
/// </summary>
public static class MergeTransforms
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private static readonly Regex CurrencyRegex = new(@"^\s*([€£$])?\s*(?<value>[\d,]*\.?\d+)\s*(?<suffix>[KMB])?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NonAlphaNumeric = new(@"[^A-Za-z0-9]+", RegexOptions.Compiled);

    private static readonly Dictionary<string, decimal> CurrencyToUsd = new(StringComparer.OrdinalIgnoreCase)
    {
        ["$"] = 1.0m,
        ["usd"] = 1.0m,
        ["€"] = 1.07m,
        ["eur"] = 1.07m,
        ["£"] = 1.22m,
        ["gbp"] = 1.22m
    };

    /// <summary>
    /// Applies the named transform to the supplied value token.
    /// </summary>
    public static JToken Apply(string? name, JToken valueToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return valueToken;
        }

        var (transform, argument) = ParseTransformName(name);
        return transform switch
        {
            "normalizetousd" => NormalizeToUsd(valueToken),
            "normalizedateiso" => NormalizeDateIso(valueToken),
            "normalizepercent" => NormalizePercent(valueToken),
            "dedupefuzzy" => DedupeFuzzy(valueToken),
            "stringtoenum" => StringToEnum(valueToken),
            "numberrounding" => NumberRounding(valueToken, argument),
            "round0" => NumberRounding(valueToken, "0"),
            "round1" => NumberRounding(valueToken, "1"),
            "round2" => NumberRounding(valueToken, "2"),
            _ => valueToken
        };
    }

    private static (string Transform, string? Argument) ParseTransformName(string raw)
    {
        var trimmed = raw.Trim();

        if (trimmed.EndsWith(')') && trimmed.Contains('('))
        {
            var idx = trimmed.IndexOf('(');
            var name = trimmed[..idx];
            var arg = trimmed[(idx + 1)..^1];
            return (name.Trim().ToLowerInvariant(), string.IsNullOrWhiteSpace(arg) ? null : arg.Trim());
        }

        var parts = trimmed.Split(':', 2, StringSplitOptions.TrimEntries);
        var transform = parts[0].ToLowerInvariant();
        var argument = parts.Length > 1 ? parts[1] : null;
        return (transform, string.IsNullOrWhiteSpace(argument) ? null : argument);
    }

    private static JToken NormalizeToUsd(JToken token)
    {
        string raw = token.Type switch
        {
            JTokenType.Float or JTokenType.Integer => token.Value<double>().ToString(Culture),
            _ => token.Value<string>() ?? string.Empty
        };

        if (string.IsNullOrWhiteSpace(raw))
        {
            return token;
        }

        var match = CurrencyRegex.Match(raw);
        if (!match.Success)
        {
            if (decimal.TryParse(raw, NumberStyles.Any, Culture, out var parsedBare))
            {
                return new JValue(Math.Round(parsedBare, 2));
            }

            return token;
        }

        var numericPart = match.Groups["value"].Value.Replace(",", string.Empty, StringComparison.Ordinal);
        if (!decimal.TryParse(numericPart, NumberStyles.Any, Culture, out var amount))
        {
            return token;
        }

        var suffix = match.Groups["suffix"].Value.ToUpperInvariant();
        amount *= suffix switch
        {
            "K" => 1_000m,
            "M" => 1_000_000m,
            "B" => 1_000_000_000m,
            _ => 1m
        };

        var symbol = match.Groups[1].Value;
        if (!string.IsNullOrEmpty(symbol) && CurrencyToUsd.TryGetValue(symbol, out var rate))
        {
            amount *= rate;
        }

        return new JValue(Math.Round(amount, 2));
    }

    private static JToken NormalizeDateIso(JToken token)
    {
        var raw = token.Value<string>();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return token;
        }

        if (DateTime.TryParse(raw, Culture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return new JValue(parsed.ToString("yyyy-MM-dd", Culture));
        }

        return token;
    }

    private static JToken NormalizePercent(JToken token)
    {
        string raw = token.Type switch
        {
            JTokenType.Float or JTokenType.Integer => token.Value<double>().ToString(Culture),
            _ => token.Value<string>() ?? string.Empty
        };

        if (string.IsNullOrWhiteSpace(raw))
        {
            return token;
        }

        raw = raw.Trim();
        var hasPercent = raw.EndsWith('%');
        raw = raw.TrimEnd('%');

        if (!double.TryParse(raw, NumberStyles.Any, Culture, out var value))
        {
            return token;
        }

        if (hasPercent || value > 1)
        {
            value /= 100d;
        }

        return new JValue(Math.Round(value, 4));
    }

    private static JToken DedupeFuzzy(JToken token)
    {
        if (token is not JArray array)
        {
            return token;
        }

        var results = new List<JToken>();
        var seen = new List<string>();

        foreach (var item in array)
        {
            var candidate = item.Type == JTokenType.String
                ? item.Value<string>() ?? string.Empty
                : item.ToString(Formatting.None);

            candidate = candidate.Trim();
            if (string.IsNullOrEmpty(candidate))
            {
                continue;
            }

            if (seen.Any(existing => AreSimilar(existing, candidate)))
            {
                continue;
            }

            seen.Add(candidate);
            results.Add(item.Type == JTokenType.String ? new JValue(candidate) : item.DeepClone());
        }

        return new JArray(results);
    }

    private static bool AreSimilar(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var distance = Levenshtein(left.ToLowerInvariant(), right.ToLowerInvariant());
        return distance <= 2;
    }

    private static int Levenshtein(string left, string right)
    {
        var n = left.Length;
        var m = right.Length;
        var d = new int[n + 1, m + 1];

        for (var i = 0; i <= n; i++) d[i, 0] = i;
        for (var j = 0; j <= m; j++) d[0, j] = j;

        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }

    private static JToken StringToEnum(JToken token)
    {
        var raw = token.Value<string>();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return token;
        }

        var normalized = NonAlphaNumeric.Replace(raw.Trim(), "_").Trim('_').ToUpperInvariant();
        return new JValue(normalized);
    }

    private static JToken NumberRounding(JToken token, string? argument)
    {
        if (argument is null)
        {
            return token;
        }

        if (!int.TryParse(argument, NumberStyles.Integer, Culture, out var decimals))
        {
            return token;
        }

        var raw = token.Value<string>() ?? token.ToString(Formatting.None);
        if (!decimal.TryParse(raw, NumberStyles.Any, Culture, out var number))
        {
            return token;
        }

        return new JValue(Math.Round(number, decimals));
    }
}
