using System.Text.RegularExpressions;

namespace S18.Prism.Services.SourcePulling;

/// <summary>
/// Basic HTML tag stripping for content extraction.
/// Removes scripts, styles, nav, footer, and all HTML tags, then normalizes whitespace.
/// </summary>
public static partial class HtmlStripper
{
    public static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "";

        // Remove entire script/style/nav/footer blocks
        var cleaned = ScriptPattern().Replace(html, " ");
        cleaned = StylePattern().Replace(cleaned, " ");
        cleaned = NavPattern().Replace(cleaned, " ");
        cleaned = FooterPattern().Replace(cleaned, " ");
        cleaned = HeaderPattern().Replace(cleaned, " ");
        cleaned = SvgPattern().Replace(cleaned, " ");

        // Remove all remaining HTML tags
        cleaned = TagPattern().Replace(cleaned, " ");

        // Decode common HTML entities
        cleaned = cleaned
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Replace("&#39;", "'")
            .Replace("&nbsp;", " ")
            .Replace("&#x27;", "'")
            .Replace("&#x2F;", "/");

        // Collapse whitespace
        cleaned = WhitespacePattern().Replace(cleaned, " ");

        return cleaned.Trim();
    }

    [GeneratedRegex(@"<script[^>]*>[\s\S]*?</script>", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptPattern();

    [GeneratedRegex(@"<style[^>]*>[\s\S]*?</style>", RegexOptions.IgnoreCase)]
    private static partial Regex StylePattern();

    [GeneratedRegex(@"<nav[^>]*>[\s\S]*?</nav>", RegexOptions.IgnoreCase)]
    private static partial Regex NavPattern();

    [GeneratedRegex(@"<footer[^>]*>[\s\S]*?</footer>", RegexOptions.IgnoreCase)]
    private static partial Regex FooterPattern();

    [GeneratedRegex(@"<header[^>]*>[\s\S]*?</header>", RegexOptions.IgnoreCase)]
    private static partial Regex HeaderPattern();

    [GeneratedRegex(@"<svg[^>]*>[\s\S]*?</svg>", RegexOptions.IgnoreCase)]
    private static partial Regex SvgPattern();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex WhitespacePattern();
}
