using System.Text;

namespace Koan.Context.Services;

/// <summary>
/// Token counting service - estimates token usage using ~4 characters per token heuristic
/// </summary>
public sealed class TokenCounter
{
    private const int CharactersPerToken = 4;

    public int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        return Math.Max(1, (text.Length + CharactersPerToken - 1) / CharactersPerToken);
    }

    public int EstimateTokens(IEnumerable<string> texts)
    {
        if (texts is null)
        {
            return 0;
        }

        var total = 0;
        foreach (var text in texts)
        {
            total += EstimateTokens(text);
        }

        return total;
    }
}
