using System.Text.RegularExpressions;

namespace Sora.Core;

/// <summary>
/// Utilities for de-identifying sensitive values (e.g., connection strings) in logs/health.
/// </summary>
public static class Redaction
{
    private static readonly Regex KeyMaskRegex = new(
        "(?i)(^|;|\\s)(Password|Pwd|User Id|Username|Uid|AccessKey|Secret|SecretKey|AccountKey)\\s*=\\s*([^;\\s]+)",
        RegexOptions.Compiled);

    /// <summary>
    /// Mask sensitive parts of a connection string or URL while preserving structure.
    /// </summary>
    public static string DeIdentify(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "(null)";
        var s = input;
        try
        {
            // Mask credentials in URIs like scheme://user:pass@host
            var idx = s.IndexOf("://");
            if (idx > 0)
            {
                var at = s.IndexOf('@', idx + 3);
                if (at > 0)
                {
                    var prefix = s[..(idx + 3)];
                    var rest = s[(at + 1)..];
                    s = prefix + "***@" + rest;
                }
            }
            // Mask well-known keys in key=value; lists
            s = KeyMaskRegex.Replace(s, m => m.Groups[1].Value + m.Groups[2].Value + "=***");
            return s;
        }
        catch
        {
            return "(masked)";
        }
    }
}
