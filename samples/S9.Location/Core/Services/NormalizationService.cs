using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using S9.Location.Core.Options;

namespace S9.Location.Core.Services;

public interface INormalizationService
{
    NormalizationResult Normalize(string sourceSystem, string address);
    string ComputeHash(string normalizedAddress);
}

public sealed record NormalizationResult(
    string Normalized,
    string Hash,
    double Confidence,
    IReadOnlyDictionary<string, string> Tokens
);

public sealed class NormalizationService : INormalizationService
{
    private readonly LocationOptions _options;
    private readonly ILogger<NormalizationService> _logger;
    private static readonly Regex PunctuationRegex = new(@"[^\w\s]", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    public NormalizationService(IOptions<LocationOptions> options, ILogger<NormalizationService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public NormalizationResult Normalize(string sourceSystem, string address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return new NormalizationResult(string.Empty, string.Empty, 0, new Dictionary<string, string>());
        }

        var work = address.Trim();
        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["source"] = sourceSystem
        };

        if (_options.Normalization.RemovePunctuation)
        {
            work = PunctuationRegex.Replace(work, " ");
        }

        if (_options.Normalization.CompressWhitespace)
        {
            work = WhitespaceRegex.Replace(work, " ");
        }

        work = ApplyCaseMode(work);
        work = ExpandAbbreviations(work, tokens);

        var segments = work.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length > 0)
        {
            tokens["line1"] = segments[0];
        }
        if (segments.Length > 1)
        {
            tokens["line2"] = string.Join(", ", segments[1..]);
        }

        if (!tokens.ContainsKey("country"))
        {
            tokens["country"] = _options.Normalization.DefaultCountry;
        }

        var hash = ComputeHash(work);
        var confidence = CalculateConfidence(work, tokens);

        return new NormalizationResult(work, hash, confidence, tokens);
    }

    public string ComputeHash(string normalizedAddress)
    {
        if (string.IsNullOrEmpty(normalizedAddress))
        {
            return string.Empty;
        }

        using var sha = SHA512.Create();
        var bytes = Encoding.UTF8.GetBytes(normalizedAddress);
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }

    private string ApplyCaseMode(string input) => _options.Normalization.CaseMode switch
    {
        "Lower" => input.ToLowerInvariant(),
        "Upper" => input.ToUpperInvariant(),
        _ => input
    };

    private string ExpandAbbreviations(string input, IDictionary<string, string> tokens)
    {
        if (_options.Normalization.Abbreviations.Count == 0)
        {
            return input;
        }

        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            if (_options.Normalization.Abbreviations.TryGetValue(parts[i], out var replacement))
            {
                tokens[$"abbr:{parts[i]}"] = replacement;
                parts[i] = replacement;
            }
        }

        return string.Join(' ', parts);
    }

    private double CalculateConfidence(string normalizedAddress, IReadOnlyDictionary<string, string> tokens)
    {
        if (string.IsNullOrWhiteSpace(normalizedAddress))
        {
            return 0;
        }

        var confidence = 0.5;
        if (tokens.ContainsKey("line1")) confidence += 0.2;
        if (tokens.ContainsKey("line2")) confidence += 0.1;
        if (tokens.ContainsKey("country")) confidence += 0.1;

        return Math.Clamp(confidence, 0, 1);
    }
}
