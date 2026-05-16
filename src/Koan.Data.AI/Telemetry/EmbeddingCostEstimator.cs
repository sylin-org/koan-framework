namespace Koan.Data.AI.Telemetry;

/// <summary>
/// Estimates costs for embedding operations based on model pricing.
/// Part of ADR AI-0020: Entity-First AI Integration and Transaction Coordination (Phase 4).
/// </summary>
/// <remarks>
/// Pricing data as of January 2025. Update periodically to reflect current rates.
/// Local/self-hosted models (Ollama, LM Studio) return $0 cost.
/// </remarks>
public static class EmbeddingCostEstimator
{
    // Cost per 1M tokens in USD
    private static readonly Dictionary<string, decimal> ModelCosts = new(StringComparer.OrdinalIgnoreCase)
    {
        // OpenAI Embedding Models
        ["text-embedding-3-small"] = 0.020m,    // $0.02 per 1M tokens
        ["text-embedding-3-large"] = 0.130m,    // $0.13 per 1M tokens
        ["text-embedding-ada-002"] = 0.100m,    // $0.10 per 1M tokens (legacy)

        // Azure OpenAI (typically same as OpenAI)
        ["azure/text-embedding-3-small"] = 0.020m,
        ["azure/text-embedding-3-large"] = 0.130m,

        // Anthropic (hypothetical - Anthropic doesn't offer embedding models yet)
        // Included for future compatibility
        ["claude-embed-v1"] = 0.080m,

        // Cohere Embedding Models
        ["embed-english-v3.0"] = 0.100m,
        ["embed-multilingual-v3.0"] = 0.100m,
        ["embed-english-light-v3.0"] = 0.020m,

        // Voyage AI
        ["voyage-2"] = 0.100m,
        ["voyage-large-2"] = 0.120m,
        ["voyage-code-2"] = 0.100m,
    };

    /// <summary>
    /// Estimates cost for an embedding operation.
    /// </summary>
    /// <param name="model">Model name (e.g., "text-embedding-3-large")</param>
    /// <param name="provider">Provider name (e.g., "openai", "ollama")</param>
    /// <param name="tokens">Number of tokens processed</param>
    /// <returns>Estimated cost in USD</returns>
    public static double EstimateCost(string? model, string? provider, int tokens)
    {
        // Local/self-hosted providers have zero cost
        if (IsLocalProvider(provider))
        {
            return 0.0;
        }

        // Unknown model - return zero to avoid false cost data
        if (string.IsNullOrWhiteSpace(model) || !ModelCosts.TryGetValue(model, out var costPerMillion))
        {
            return 0.0;
        }

        // Calculate cost: (tokens / 1,000,000) * cost_per_million
        var cost = (tokens / 1_000_000.0) * (double)costPerMillion;
        return Math.Round(cost, 6); // Round to 6 decimal places (micro-cents)
    }

    /// <summary>
    /// Checks if a provider is local/self-hosted (zero cost).
    /// </summary>
    private static bool IsLocalProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
            return false;

        return provider.ToLowerInvariant() switch
        {
            "ollama" => true,
            "lmstudio" => true,
            "localai" => true,
            "llamacpp" => true,
            _ => false
        };
    }

    /// <summary>
    /// Gets cost per million tokens for a specific model.
    /// </summary>
    /// <param name="model">Model name</param>
    /// <returns>Cost per 1M tokens in USD, or null if model is unknown</returns>
    public static decimal? GetModelCostPerMillion(string model)
    {
        return ModelCosts.TryGetValue(model, out var cost) ? cost : null;
    }

    /// <summary>
    /// Gets all known model pricing.
    /// </summary>
    /// <returns>Dictionary of model names to cost per 1M tokens</returns>
    public static IReadOnlyDictionary<string, decimal> GetAllModelCosts()
    {
        return ModelCosts;
    }

    /// <summary>
    /// Registers or updates pricing for a custom model.
    /// </summary>
    /// <param name="model">Model name</param>
    /// <param name="costPerMillion">Cost per 1M tokens in USD</param>
    public static void RegisterModelCost(string model, decimal costPerMillion)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model name cannot be null or empty", nameof(model));

        if (costPerMillion < 0)
            throw new ArgumentException("Cost cannot be negative", nameof(costPerMillion));

        ModelCosts[model] = costPerMillion;
    }
}
