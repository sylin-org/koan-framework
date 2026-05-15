using System.Text;

namespace Koan.Tagging;

/// <summary>
/// Slug derivation for tag identifiers. Lowercases, strips apostrophes, collapses contiguous
/// non-alphanumerics to single hyphens, trims hyphens. The canonical form used in
/// <see cref="Tag.Id"/> across the platform.
/// </summary>
/// <example>
/// <code>
/// TagSlug.From("Au Ra")              → "au-ra"
/// TagSlug.From("Au'Ra")              → "aura"     // apostrophe stripped, no hyphen
/// TagSlug.From("Miqo'te")            → "miqote"
/// TagSlug.From("Seeker of the Sun")  → "seeker-of-the-sun"
/// TagSlug.From("__Final Fantasy XIV__") → "final-fantasy-xiv"
/// </code>
/// </example>
public static class TagSlug
{
    /// <summary>
    /// Returns the canonical kebab-case slug for <paramref name="raw"/>. Null / whitespace input
    /// returns an empty string.
    /// </summary>
    public static string From(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var sb = new StringBuilder(raw.Length);
        var lastWasHyphen = true; // treat leading non-alnum as "already hyphenated" so we trim
        foreach (var ch in raw)
        {
            if (ch == '\'' || ch == '’') continue; // strip apostrophes outright, no hyphen
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                lastWasHyphen = false;
            }
            else
            {
                if (!lastWasHyphen)
                {
                    sb.Append('-');
                    lastWasHyphen = true;
                }
            }
        }

        // Trim trailing hyphen if any.
        while (sb.Length > 0 && sb[^1] == '-') sb.Length--;
        return sb.ToString();
    }
}
