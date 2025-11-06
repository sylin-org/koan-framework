using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Koan.AI;
using Koan.AI.Contracts.Options;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Samples.Meridian.Services;

public interface IFactCategorizer
{
    Task<FactCategorizationMap> CategorizeAsync(
        FactCatalog catalog,
        AnalysisType analysisType,
        CancellationToken ct);
}

/// <summary>
/// Stage 2: Use LLM to semantically group facts into contextual batches.
/// Results are cached by catalog hash for performance.
/// </summary>
public sealed class FactCategorizer : IFactCategorizer
{
    private static readonly Regex JsonFence = new("```json|```", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly MeridianOptions _options;
    private readonly ILogger<FactCategorizer> _logger;

    public FactCategorizer(IOptions<MeridianOptions> options, ILogger<FactCategorizer> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<FactCategorizationMap> CategorizeAsync(
        FactCatalog catalog,
        AnalysisType analysisType,
        CancellationToken ct)
    {
        // Check cache first
        var catalogHash = FactCategorizationMap.ComputeCatalogHash(catalog);
        var cached = await FactCategorizationMap.GetByCatalogHashAsync(catalogHash, ct);

        if (cached != null)
        {
            _logger.LogDebug("Using cached categorization for catalog hash {Hash} ({BatchCount} batches)",
                catalogHash[..12], cached.Batches.Count);
            return cached;
        }

        _logger.LogInformation("Generating semantic categorization for {FactCount} facts using {Model}",
            catalog.Facts.Count, _options.Facts.ExtractionModel);

        // Build categorization prompt
        var prompt = BuildCategorizationPrompt(catalog, analysisType);

        // Call LLM
        var chatOptions = new AiChatOptions
        {
            Message = prompt,
            Model = _options.Facts.ExtractionModel,
            Temperature = 0.3, // Lower temperature for consistent categorization
            MaxTokens = 0,
            ResponseFormat = "json"
        };

        string raw;
        try
        {
            raw = await Ai.Chat(chatOptions, ct);
            _logger.LogDebug("LLM categorization response length: {Length} characters", raw.Length);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to generate semantic categorization with model {Model}",
                _options.Facts.ExtractionModel);
            throw;
        }

        // Parse response
        var batches = ParseCategorization(raw, catalog);

        if (batches.Count == 0)
        {
            _logger.LogWarning("LLM returned no batches - falling back to single batch containing all facts");
            batches = CreateFallbackBatch(catalog);
        }

        // Save to cache
        var map = await FactCategorizationMap.SaveWithHashAsync(catalogHash, batches, ct);

        _logger.LogInformation("Generated {BatchCount} semantic batches for {FactCount} facts (hash: {Hash})",
            batches.Count, catalog.Facts.Count, catalogHash[..12]);

        foreach (var batch in batches)
        {
            _logger.LogDebug("Batch '{Category}': {FieldCount} fields - {Description}",
                batch.CategoryName, batch.FieldPaths.Count, batch.CategoryDescription);
        }

        return map;
    }

    private string BuildCategorizationPrompt(FactCatalog catalog, AnalysisType analysisType)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are analyzing extraction requirements for a document analysis pipeline.");
        sb.AppendLine();
        sb.AppendLine($"ANALYSIS TYPE: {analysisType.Name}");
        if (!string.IsNullOrWhiteSpace(analysisType.Description))
        {
            sb.AppendLine($"DESCRIPTION: {analysisType.Description}");
        }
        sb.AppendLine();
        sb.AppendLine("TASK:");
        sb.AppendLine("Group the following facts into semantic batches where related facts benefit from shared context during extraction.");
        sb.AppendLine();
        sb.AppendLine("GUIDELINES:");
        sb.AppendLine("1. Create 3-8 batches based on semantic relationships");
        sb.AppendLine("2. Facts in the same batch should be contextually related");
        sb.AppendLine("3. Consider common document structures and information flow");
        sb.AppendLine("4. Balance batch sizes - avoid single-field batches unless semantically isolated");
        sb.AppendLine("5. Each batch needs a clear category name and description");
        sb.AppendLine();
        sb.AppendLine($"FACTS TO CATEGORIZE ({catalog.Facts.Count} total):");
        sb.AppendLine();

        foreach (var fact in catalog.Facts.OrderBy(f => f.Source).ThenBy(f => f.FieldName))
        {
            sb.Append($"- {fact.FieldPath}");
            if (!string.IsNullOrWhiteSpace(fact.Description))
            {
                sb.Append($": {fact.Description}");
            }
            if (fact.Examples.Any())
            {
                sb.Append($" (examples: {string.Join(", ", fact.Examples.Take(3))})");
            }
            sb.Append($" [source: {fact.Source}]");
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("OUTPUT FORMAT (strict JSON):");
        sb.AppendLine("{");
        sb.AppendLine("  \"batches\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"batchId\": \"identity_tracking\",");
        sb.AppendLine("      \"categoryName\": \"Identity & Tracking\",");
        sb.AppendLine("      \"categoryDescription\": \"Identifiers, ticket numbers, and tracking information\",");
        sb.AppendLine("      \"fieldPaths\": [\"$.servicenow_id\", \"$.architect\", \"$.Department\"]");
        sb.AppendLine("    },");
        sb.AppendLine("    {");
        sb.AppendLine("      \"batchId\": \"security_encryption\",");
        sb.AppendLine("      \"categoryName\": \"Security & Encryption\",");
        sb.AppendLine("      \"categoryDescription\": \"Cryptographic protocols, certificates, and security controls\",");
        sb.AppendLine("      \"fieldPaths\": [\"$.encryption_protocol\", \"$.tls_version\"]");
        sb.AppendLine("    }");
        sb.AppendLine("  ]");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private List<SemanticBatch> ParseCategorization(string rawResponse, FactCatalog catalog)
    {
        try
        {
            var cleaned = JsonFence.Replace(rawResponse, string.Empty).Trim();
            var json = JObject.Parse(cleaned);

            var batchesToken = json["batches"];
            if (batchesToken is not JArray array || array.Count == 0)
            {
                _logger.LogWarning("No batches array found in LLM categorization response");
                return new List<SemanticBatch>();
            }

            var batches = new List<SemanticBatch>();
            var allFieldPaths = new HashSet<string>(catalog.Facts.Select(f => f.FieldPath), StringComparer.OrdinalIgnoreCase);

            foreach (var token in array.OfType<JObject>())
            {
                var batchId = token.Value<string>("batchId")?.Trim() ?? string.Empty;
                var categoryName = token.Value<string>("categoryName")?.Trim() ?? string.Empty;
                var categoryDescription = token.Value<string>("categoryDescription")?.Trim() ?? string.Empty;
                var fieldPathsToken = token["fieldPaths"];

                if (string.IsNullOrWhiteSpace(batchId) || string.IsNullOrWhiteSpace(categoryName))
                {
                    _logger.LogWarning("Skipping batch with missing batchId or categoryName");
                    continue;
                }

                if (fieldPathsToken is not JArray fieldPathsArray || fieldPathsArray.Count == 0)
                {
                    _logger.LogWarning("Skipping batch '{CategoryName}' with no field paths", categoryName);
                    continue;
                }

                var fieldPaths = fieldPathsArray
                    .Select(t => t.ToString().Trim())
                    .Where(fp => !string.IsNullOrWhiteSpace(fp) && allFieldPaths.Contains(fp))
                    .ToList();

                if (fieldPaths.Count == 0)
                {
                    _logger.LogWarning("Skipping batch '{CategoryName}' with no valid field paths", categoryName);
                    continue;
                }

                batches.Add(new SemanticBatch
                {
                    BatchId = batchId,
                    CategoryName = categoryName,
                    CategoryDescription = categoryDescription,
                    FieldPaths = fieldPaths
                });
            }

            return batches;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse LLM categorization response");
            return new List<SemanticBatch>();
        }
    }

    private List<SemanticBatch> CreateFallbackBatch(FactCatalog catalog)
    {
        return new List<SemanticBatch>
        {
            new SemanticBatch
            {
                BatchId = "all_facts",
                CategoryName = "All Facts",
                CategoryDescription = "All facts for extraction (fallback batch)",
                FieldPaths = catalog.Facts.Select(f => f.FieldPath).ToList()
            }
        };
    }
}
