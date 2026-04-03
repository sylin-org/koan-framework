using Koan.AI.Contracts.Models;
using Koan.Rag.Abstractions;
using Koan.Rag.Content.Strategies;
using Microsoft.Extensions.Logging;

namespace Koan.Rag.Content;

/// <summary>
/// Base class for content adapters. Provides the multi-round extraction protocol:
/// <list type="number">
///   <item>Classify: what kind of content is this?</item>
///   <item>Select strategy: pre-determined → corpus-cached → auto-generated</item>
///   <item>Interpret: execute the strategy's interpretation prompt</item>
///   <item>Enrich (optional): extract implicit information</item>
/// </list>
/// <para>
/// Subclasses override content-specific preparation (e.g., PDF page extraction,
/// audio transcription) then call <see cref="InterpretVisual"/> or
/// <see cref="InterpretText"/> for the multi-round protocol.
/// </para>
/// </summary>
internal abstract class ContentAdapterBase : IContentAdapter
{
    private readonly StrategyGenerator _strategyGenerator;
    private readonly ILogger _logger;

    protected ContentAdapterBase(
        StrategyGenerator strategyGenerator,
        ILogger logger)
    {
        _strategyGenerator = strategyGenerator;
        _logger = logger;
    }

    public abstract string Id { get; }
    public abstract IReadOnlySet<string> SupportedExtensions { get; }
    public abstract IReadOnlySet<Modality> SupportedModalities { get; }

    public virtual bool CanProcess(ContentExtractionRequest request)
    {
        if (request.FilePath is not null)
        {
            var ext = Path.GetExtension(request.FilePath).ToLowerInvariant();
            return SupportedExtensions.Contains(ext);
        }

        return SupportedModalities.Contains(request.Modality);
    }

    public abstract Task<ContentExtractionResult> Extract(
        ContentExtractionRequest request, CancellationToken ct = default);

    // ── Multi-Round Protocol for Visual Content ─────────────────────────

    /// <summary>
    /// Run the full multi-round extraction on image/visual content.
    /// Used by image and PDF (scanned page) adapters.
    /// </summary>
    protected async Task<ContentExtractionResult> InterpretVisual(
        byte[] imageBytes,
        string? documentTitle,
        string? directive,
        string? corpusName,
        CancellationToken ct)
    {
        // Round 1: Classify
        var classification = await ClassifyVisual(imageBytes, ct);

        _logger.LogDebug(
            "Round 1 classified visual content as '{Category}': {Description}",
            classification.Category, classification.Description);

        // Select strategy: pre-determined > corpus-cached > auto-generated
        var strategy = BuiltInStrategies.Match(classification)
            ?? await _strategyGenerator.GetOrGenerate(
                classification, corpusName, directive, ct);

        _logger.LogDebug("Using strategy '{Strategy}' ({Origin})",
            strategy.Id, strategy.Origin);

        // Round 2: Interpret
        var interpretation = await ExecuteVisualStrategy(
            strategy, imageBytes, directive, ct);

        var rounds = 2;

        // Round 3: Enrich (if strategy requires it)
        if (strategy.RequiresEnrichment && strategy.EnrichmentPrompt is not null)
        {
            var enrichment = await EnrichInterpretation(
                strategy.EnrichmentPrompt, interpretation, ct);

            if (!string.IsNullOrWhiteSpace(enrichment))
            {
                interpretation = $"{interpretation}\n\n--- Additional Context ---\n\n{enrichment}";
                rounds = 3;
            }
        }

        return new ContentExtractionResult
        {
            Text = interpretation,
            Classification = classification,
            StrategyUsed = strategy.Id,
            RoundsExecuted = rounds
        };
    }

    /// <summary>
    /// Run the multi-round extraction on text content that needs interpretation
    /// (e.g., code, structured data, mixed content).
    /// </summary>
    protected async Task<ContentExtractionResult> InterpretText(
        string textContent,
        string? documentTitle,
        string? directive,
        string? corpusName,
        CancellationToken ct)
    {
        // Round 1: Classify
        var classification = await ClassifyText(textContent, ct);

        var strategy = BuiltInStrategies.Match(classification)
            ?? await _strategyGenerator.GetOrGenerate(
                classification, corpusName, directive, ct);

        // Round 2: Interpret
        var interpretation = await ExecuteTextStrategy(
            strategy, textContent, directive, ct);

        var rounds = 2;

        if (strategy.RequiresEnrichment && strategy.EnrichmentPrompt is not null)
        {
            var enrichment = await EnrichInterpretation(
                strategy.EnrichmentPrompt, interpretation, ct);

            if (!string.IsNullOrWhiteSpace(enrichment))
            {
                interpretation = $"{interpretation}\n\n--- Additional Context ---\n\n{enrichment}";
                rounds = 3;
            }
        }

        return new ContentExtractionResult
        {
            Text = interpretation,
            Classification = classification,
            StrategyUsed = strategy.Id,
            RoundsExecuted = rounds
        };
    }

    // ── Classification (Round 1) ────────────────────────────────────────

    private static async Task<ContentClassification> ClassifyVisual(
        byte[] imageBytes, CancellationToken ct)
    {
        try
        {
            // Use Client.Describe for vision classification
            var response = await Koan.AI.Client.Describe(imageBytes, ct);

            // Parse or construct classification from the describe response
            return new ContentClassification
            {
                Category = InferCategory(response),
                Description = response,
                FormatHint = null
            };
        }
        catch
        {
            return new ContentClassification
            {
                Category = "unknown",
                Description = "Visual content that could not be classified",
                Confidence = 0.0
            };
        }
    }

    private static async Task<ContentClassification> ClassifyText(
        string textContent, CancellationToken ct)
    {
        // Take a sample for classification
        var sample = textContent.Length > 2000 ? textContent[..2000] : textContent;

        var prompt = $"""
            Classify this text content. What type of content is it?
            Return a single category from: text/document, text/policy, text/guide,
            code/snippet, table, form, mixed, or a more specific category if appropriate.

            Content sample:
            {sample}

            Return only the category string, nothing else.
            """;

        try
        {
            var category = await Koan.AI.Client.Chat(prompt, ct);
            return new ContentClassification
            {
                Category = category.Trim().ToLowerInvariant(),
                Description = $"Text content classified as {category.Trim()}"
            };
        }
        catch
        {
            return new ContentClassification
            {
                Category = "text/document",
                Description = "Text document (classification failed)"
            };
        }
    }

    // ── Strategy Execution (Round 2) ────────────────────────────────────

    private static async Task<string> ExecuteVisualStrategy(
        InterpretationStrategy strategy,
        byte[] imageBytes,
        string? directive,
        CancellationToken ct)
    {
        // Use Client.Describe for visual interpretation
        // The strategy prompt injects via DescribeOptions.Focus
        var options = new Koan.AI.Contracts.Options.DescribeOptions
        {
            Purpose = Koan.AI.Contracts.Options.DescribePurpose.SearchIndex,
            Focus = directive is not null
                ? $"DOMAIN GUIDANCE: {directive}\n\n{strategy.InterpretationPrompt}"
                : strategy.InterpretationPrompt
        };

        return await Koan.AI.Client.Describe(imageBytes, options, ct);
    }

    private static async Task<string> ExecuteTextStrategy(
        InterpretationStrategy strategy,
        string textContent,
        string? directive,
        CancellationToken ct)
    {
        var prompt = directive is not null
            ? $"DOMAIN GUIDANCE: {directive}\n\n{strategy.InterpretationPrompt}\n\n---\n\n{textContent}"
            : $"{strategy.InterpretationPrompt}\n\n---\n\n{textContent}";

        return await Koan.AI.Client.Chat(prompt, ct);
    }

    // ── Enrichment (Round 3) ────────────────────────────────────────────

    private static async Task<string?> EnrichInterpretation(
        string enrichmentPrompt,
        string interpretation,
        CancellationToken ct)
    {
        try
        {
            var prompt = $"{enrichmentPrompt}\n\nContent interpretation:\n{interpretation}";
            return await Koan.AI.Client.Chat(prompt, ct);
        }
        catch
        {
            return null;
        }
    }

    // ── Utilities ───────────────────────────────────────────────────────

    /// <summary>
    /// Infer a classification category from a free-text description.
    /// </summary>
    private static string InferCategory(string description)
    {
        var lower = description.ToLowerInvariant();

        if (lower.Contains("diagram") || lower.Contains("flowchart") || lower.Contains("architecture"))
            return lower.Contains("sequence") ? "diagram/sequence" : "diagram/architecture";
        if (lower.Contains("table") || lower.Contains("spreadsheet"))
            return "table";
        if (lower.Contains("chart") || lower.Contains("graph") || lower.Contains("plot"))
            return "chart";
        if (lower.Contains("form") || lower.Contains("document") || lower.Contains("scan"))
            return "form";
        if (lower.Contains("screenshot") || lower.Contains("interface") || lower.Contains("ui"))
            return "screenshot";
        if (lower.Contains("code") || lower.Contains("snippet") || lower.Contains("programming"))
            return "code";
        if (lower.Contains("photo") || lower.Contains("image") || lower.Contains("picture"))
            return "photograph";

        return "unknown";
    }
}
