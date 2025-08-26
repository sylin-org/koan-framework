using System.Text.RegularExpressions;

namespace Sora.Orchestration;

public static class Redaction
{
    static readonly Regex Sensitive = new(
        pattern: "token|secret|password|pwd|key|connectionstring",
        options: RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string? Maybe(string? key, string? value)
        => key is null ? value : (Sensitive.IsMatch(key) ? "***" : value);

    // Redact occurrences of key=value pairs in free-form text
    static readonly Regex RedactKv = new(
        pattern: "(?<k>token|secret|password|pwd|key|connectionstring)(?<sep>\\s*[:=]\\s*)(?<v>\"[^\"]*\"|'[^']*'|[^\\s,;]+)",
        options: RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string RedactText(string? input)
        => string.IsNullOrEmpty(input)
            ? input ?? string.Empty
            : RedactKv.Replace(input!, m => $"{m.Groups["k"].Value}{m.Groups["sep"].Value}***");
}
