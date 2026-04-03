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
}
