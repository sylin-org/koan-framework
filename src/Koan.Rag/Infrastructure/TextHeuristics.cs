namespace Koan.Rag.Infrastructure;

internal static class TextHeuristics
{
    public static bool IsHeading(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith('#')) return true;
        if (trimmed.Length is > 3 and < 80 &&
            trimmed.Contains(' ') && // Must have multiple words to be a heading
            trimmed == trimmed.ToUpperInvariant() &&
            trimmed.Any(char.IsLetter))
            return true;
        return false;
    }

    /// <summary>
    /// Sanitize a string before embedding it in an LLM prompt.
    /// Removes characters that could break prompt structure.
    /// </summary>
    public static string SanitizeForPrompt(string input, int maxLength = 200)
    {
        if (string.IsNullOrEmpty(input)) return "";
        var sanitized = input
            .Replace("\"", "'")
            .Replace("\n", " ")
            .Replace("\r", " ")
            .Replace("---", "—"); // Prevent delimiter breakout
        return sanitized.Length > maxLength ? sanitized[..maxLength] : sanitized;
    }
}
