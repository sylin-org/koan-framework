using System.Text;

namespace Koan.Context.Services;

/// <summary>
/// Estimates token usage for text payloads using a lightweight heuristic.
/// </summary>
public interface ITokenCountingService
{
    int EstimateTokens(string text);

    int EstimateTokens(IEnumerable<string> texts);
}

/// <summary>
/// Basic token counter assuming ~4 characters per token. Can be swapped with a true tokenizer later.
/// </summary>
public sealed class TokenCountingService : ITokenCountingService
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
