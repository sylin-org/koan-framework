using System.Text.RegularExpressions;

namespace Koan.Admin.Infrastructure;

internal static class KoanAdminPathUtility
{
    private static readonly Regex PrefixPattern = new("^[A-Za-z0-9._-]+$", RegexOptions.Compiled);

    public static string NormalizePrefix(string? prefix)
    {
        var trimmed = string.IsNullOrWhiteSpace(prefix)
            ? KoanAdminDefaults.Prefix
            : prefix.Trim();

        trimmed = trimmed.Trim('/');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            trimmed = KoanAdminDefaults.Prefix;
        }

        return trimmed;
    }

    public static bool IsValidPrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return true;
        var normalized = prefix.Trim('/').Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return true;
        return PrefixPattern.IsMatch(normalized);
    }

    public static string BuildTemplate(string? prefix, string suffix)
    {
        var normalized = NormalizePrefix(prefix);
        return string.IsNullOrWhiteSpace(suffix)
            ? normalized
            : string.Create(normalized.Length + 1 + suffix.Length, (normalized, suffix), (span, state) =>
            {
                var (pref, suf) = state;
                pref.AsSpan().CopyTo(span);
                span[pref.Length] = '/';
                suf.AsSpan().CopyTo(span[(pref.Length + 1)..]);
            });
    }
}
