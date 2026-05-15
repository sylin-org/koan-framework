using System.Text.Json;
using Koan.Rag.Abstractions;
using Koan.Rag.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Rag.Content.Strategies;

/// <summary>
/// Generates interpretation strategies for novel content types using the
/// highest-quality reasoning model available. Generated strategies are cached
/// per corpus to amortize the cost across all files of the same type.
/// <para>
/// This is the most leveraged LLM call in the entire pipeline: one strategy
/// shapes the interpretation of every subsequent file of that content type.
/// Route to the best available model via <c>Koan:Rag:Models:StrategyGeneration</c>.
/// </para>
/// </summary>
internal sealed class StrategyGenerator
{
    private readonly ILogger<StrategyGenerator> _logger;
    private readonly string? _strategyModel;

    // Per-corpus strategy cache: (corpus name, category) → generated strategy
    private readonly Dictionary<(string?, string), InterpretationStrategy> _cache = new();
    private readonly object _cacheLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public StrategyGenerator(
        IOptions<RagOptions> options,
        ILogger<StrategyGenerator> logger)
    {
        _logger = logger;
        _strategyModel = options.Value.Models.StrategyGeneration;
    }

    /// <summary>
    /// Get or generate a strategy for the given classification and corpus.
    /// Caches per (corpus, category) to avoid regeneration.
    /// </summary>
    public async Task<InterpretationStrategy> GetOrGenerate(
        ContentClassification classification,
        string? corpusName,
        string? directive,
        CancellationToken ct)
    {
        var cacheKey = (corpusName, classification.Category);

        lock (_cacheLock)
        {
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                // Increment use count for promotion tracking
                _cache[cacheKey] = cached with { UseCount = cached.UseCount + 1 };
                return cached;
            }
        }

        // Generate new strategy using the best available model
        var strategy = await Generate(classification, directive, ct);

        lock (_cacheLock)
        {
            _cache[cacheKey] = strategy;
        }

        _logger.LogInformation(
            "Auto-generated interpretation strategy for '{Category}' in corpus '{Corpus}'",
            classification.Category, corpusName ?? "default");

        return strategy;
    }

    /// <summary>
    /// Generate a new interpretation strategy using the highest-quality reasoning model.
    /// </summary>
    private async Task<InterpretationStrategy> Generate(
        ContentClassification classification,
        string? directive,
        CancellationToken ct)
    {
        var directiveLine = directive is not null
            ? $"\nCorpus domain guidance: {directive}\n"
            : "";

        var prompt = $$"""
            You are an expert content analysis strategist. Given the following content classification,
            design a comprehensive interpretation strategy that will extract the maximum meaningful
            information from this type of content.
            {{directiveLine}}
            Content classification:
            - Category: {{classification.Category}}
            - Description: {{classification.Description}}
            - Format: {{classification.FormatHint ?? "unknown"}}

            Design an interpretation strategy by providing:

            1. INTERPRETATION_PROMPT: A detailed prompt that instructs an AI to deeply analyze this
               specific type of content. The prompt should extract:
               - All structural elements and their roles
               - Relationships between elements
               - Quantitative data with proper context
               - Implicit information that might be lost if the original content were discarded
               Be specific to THIS content type. Generic instructions waste extraction quality.

            2. ENRICHMENT_PROMPT: A follow-up prompt that extracts implicit, inferred, or
               contextual information not covered by the interpretation prompt.
               Set to null if the content type doesn't benefit from enrichment.

            Return a JSON object:
            {
              "interpretationPrompt": "the detailed interpretation prompt",
              "enrichmentPrompt": "the enrichment prompt or null",
              "requiresEnrichment": true
            }

            Return only valid JSON. No markdown, no commentary.
            """;

        try
        {
            // Route to the best reasoning model for strategy generation
            using (_strategyModel is not null ? Koan.AI.Client.Scope(chat: _strategyModel) : null)
            {
                var response = await Koan.AI.Client.Chat(prompt, ct);
                return ParseStrategy(response, classification);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Strategy generation failed for '{Category}', using generic fallback",
                classification.Category);

            return FallbackStrategy(classification);
        }
    }

    private InterpretationStrategy ParseStrategy(
        string response, ContentClassification classification)
    {
        try
        {
            var cleaned = CleanJsonResponse(response);
            var parsed = JsonSerializer.Deserialize<GeneratedStrategyDto>(cleaned, JsonOptions);

            if (parsed?.InterpretationPrompt is null or "")
                return FallbackStrategy(classification);

            return new InterpretationStrategy
            {
                Id = $"auto:{classification.Category}",
                InterpretationPrompt = parsed.InterpretationPrompt,
                EnrichmentPrompt = parsed.EnrichmentPrompt,
                RequiresEnrichment = parsed.RequiresEnrichment,
                Origin = StrategyOrigin.AutoGenerated,
                MatchCategories = [classification.Category]
            };
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Failed to parse generated strategy JSON");
            return FallbackStrategy(classification);
        }
    }

    private static InterpretationStrategy FallbackStrategy(ContentClassification classification)
    {
        return new InterpretationStrategy
        {
            Id = $"fallback:{classification.Category}",
            InterpretationPrompt =
                $"Thoroughly analyze this content (classified as: {classification.Description}). " +
                "Extract all meaningful information including: structure, components, relationships, " +
                "data, labels, and any details that would be lost if the original were discarded. " +
                "Be comprehensive and specific.",
            EnrichmentPrompt =
                "Based on your analysis, identify: implicit assumptions, constraints, " +
                "relationships not explicitly shown, and any contextual information that " +
                "would help someone understand this content without seeing the original.",
            RequiresEnrichment = true,
            Origin = StrategyOrigin.AutoGenerated,
            MatchCategories = [classification.Category]
        };
    }

    private static string CleanJsonResponse(string response)
    {
        var trimmed = response.Trim();
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0) trimmed = trimmed[(firstNewline + 1)..];
            if (trimmed.EndsWith("```")) trimmed = trimmed[..^3];
            trimmed = trimmed.Trim();
        }
        return trimmed;
    }

    private sealed class GeneratedStrategyDto
    {
        public string? InterpretationPrompt { get; set; }
        public string? EnrichmentPrompt { get; set; }
        public bool RequiresEnrichment { get; set; } = true;
    }
}
