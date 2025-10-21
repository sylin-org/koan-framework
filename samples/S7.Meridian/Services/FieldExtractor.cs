using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Koan.AI;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Options;
using Koan.Data.Core;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;

namespace Koan.Samples.Meridian.Services;

public interface IFieldExtractor
{
    Task<List<ExtractedField>> ExtractAsync(DocumentPipeline pipeline, IReadOnlyList<Passage> passages, MeridianOptions options, CancellationToken ct);
}

/// <summary>
/// CARVE: Previous implementation was a regex-based stub (0% of proposal).
///
/// REQUIRED IMPLEMENTATION (Per Proposal):
/// 1. Per-field RAG query generation from schema field paths
/// 2. Hybrid vector search (BM25 + semantic) via VectorWorkflow<Passage>.Query()
/// 3. MMR (Maximal Marginal Relevance) for passage diversity
/// 4. Token budget management (tournament selection if >2000 tokens)
/// 5. LLM-based extraction from retrieved passages (Koan.AI.Ai.Chat())
/// 6. Parse AI response for: value, confidence (0.0-1.0), passageIndex
/// 7. Schema validation of extracted values
/// 8. Text span localization within passage for highlighting
///
/// REFERENCE:
/// - Proposal lines 1865-2300 (Field Extraction via RAG)
/// - S5.Recs for vector search patterns
/// - S6.SnapVault for AI prompt engineering
/// </summary>
public sealed class FieldExtractor : IFieldExtractor
{
    private readonly ILogger<FieldExtractor> _logger;

    public FieldExtractor(ILogger<FieldExtractor> logger)
    {
        _logger = logger;
    }

    public async Task<List<ExtractedField>> ExtractAsync(
        DocumentPipeline pipeline,
        IReadOnlyList<Passage> passages,
        MeridianOptions options,
        CancellationToken ct)
    {
        var schema = pipeline.TryParseSchema();
        var results = new List<ExtractedField>();

        if (schema == null)
        {
            _logger.LogWarning("Pipeline {PipelineId} schema invalid; skipping extraction.", pipeline.Id);
            return results;
        }

        var fieldPaths = EnumerateLeafSchemas(schema).ToList();
        _logger.LogInformation("Extracting {Count} fields for pipeline {PipelineId}", fieldPaths.Count, pipeline.Id);

        foreach (var (fieldPath, fieldSchema) in fieldPaths)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                // 1. Build RAG query
                var query = BuildRAGQuery(fieldPath, fieldSchema, pipeline);

                // 2. Retrieve relevant passages
                var retrieved = await RetrievePassages(pipeline.Id, query, options, ct);
                if (retrieved.Count == 0)
                {
                    _logger.LogDebug("No passages retrieved for field {FieldPath}, skipping extraction", fieldPath);
                    continue;
                }

                // 3. Apply MMR diversity (skip for now as we need vectors from VectorWorkflow)
                // For Phase 1, we'll use retrieved passages directly
                var diverse = retrieved;

                // 4. Enforce token budget
                var budgeted = EnforceTokenBudget(diverse, options.Retrieval.MaxTokensPerField);

                // 5. Extract from passages
                var extraction = await ExtractFromPassages(pipeline, fieldPath, fieldSchema, budgeted, options, ct);
                if (extraction != null)
                {
                    await extraction.Save(ct);
                    results.Add(extraction);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract field {FieldPath} for pipeline {PipelineId}",
                    fieldPath, pipeline.Id);
            }
        }

        _logger.LogInformation("Extracted {Count} fields for pipeline {PipelineId}", results.Count, pipeline.Id);
        return results;
    }

    /// <summary>
    /// Builds a semantic query for RAG retrieval based on the field path and schema.
    /// Converts camelCase field names to natural language queries.
    /// </summary>
    private string BuildRAGQuery(string fieldPath, JSchema fieldSchema, DocumentPipeline pipeline)
    {
        // Extract field name: $.annualRevenue → "annualRevenue"
        var fieldName = fieldPath.TrimStart('$', '.');

        // Convert camelCase to spaced: annualRevenue → annual revenue
        var spaced = Regex.Replace(fieldName, "([a-z])([A-Z])", "$1 $2").ToLower();

        // Apply bias if present
        var bias = !string.IsNullOrWhiteSpace(pipeline.BiasNotes)
            ? $" {pipeline.BiasNotes}"
            : string.Empty;

        return $"Find information about {spaced}.{bias}";
    }

    /// <summary>
    /// Retrieves relevant passages using hybrid vector search (BM25 + semantic).
    /// </summary>
    private async Task<List<Passage>> RetrievePassages(
        string pipelineId,
        string query,
        MeridianOptions options,
        CancellationToken ct)
    {
        // 1. Embed query
        _logger.LogDebug("Embedding query: {Query}", query);
        var queryEmbedding = await Koan.AI.Ai.Embed(query, ct);

        // 2. Hybrid search via VectorWorkflow
        var results = await VectorWorkflow<Passage>.Query(
            new VectorQueryOptions(
                queryEmbedding,
                TopK: options.Retrieval.TopK,
                SearchText: query,           // Enables BM25 hybrid search
                Alpha: options.Retrieval.Alpha),
            profileName: MeridianConstants.VectorProfile,
            ct: ct);

        // 3. Load passages and filter by pipeline
        var passages = new List<Passage>();
        foreach (var match in results.Matches)
        {
            var passage = await Passage.Get(match.Id, ct);
            if (passage != null && passage.PipelineId == pipelineId)
            {
                passages.Add(passage);
            }
        }

        _logger.LogInformation("Retrieved {Count} passages for query: {Query}", passages.Count, query);
        return passages;
    }

    /// <summary>
    /// Computes cosine similarity between two embedding vectors.
    /// </summary>
    private double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0.0;

        double dot = 0.0, magA = 0.0, magB = 0.0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        var denominator = Math.Sqrt(magA) * Math.Sqrt(magB);
        return denominator > 0 ? dot / denominator : 0.0;
    }

    /// <summary>
    /// Applies Maximal Marginal Relevance (MMR) to select diverse passages.
    /// MMR balances relevance and diversity to avoid redundant information.
    /// </summary>
    private List<Passage> ApplyMMR(
        List<(Passage passage, double score, float[]? vector)> ranked,
        float[] queryEmbedding,
        int maxPassages,
        double lambda)
    {
        var selected = new List<(Passage passage, float[]? vector)>();
        var remaining = ranked.ToList();

        while (selected.Count < maxPassages && remaining.Count > 0)
        {
            double bestScore = double.MinValue;
            (Passage passage, double score, float[]? vector)? bestCandidate = null;
            int bestIndex = -1;

            for (int i = 0; i < remaining.Count; i++)
            {
                var candidate = remaining[i];
                var relevance = candidate.score;

                // Diversity penalty: max similarity to selected
                var maxSimilarity = 0.0;
                if (selected.Count > 0 && candidate.vector is { Length: > 0 })
                {
                    foreach (var sel in selected)
                    {
                        if (sel.vector is { Length: > 0 })
                        {
                            var sim = CosineSimilarity(candidate.vector!, sel.vector!);
                            maxSimilarity = Math.Max(maxSimilarity, sim);
                        }
                    }
                }

                // MMR score: λ * relevance - (1-λ) * max_similarity
                var mmrScore = lambda * relevance - (1 - lambda) * maxSimilarity;

                if (mmrScore > bestScore)
                {
                    bestScore = mmrScore;
                    bestCandidate = candidate;
                    bestIndex = i;
                }
            }

            if (bestCandidate.HasValue)
            {
                selected.Add((bestCandidate.Value.passage, bestCandidate.Value.vector));
                remaining.RemoveAt(bestIndex);
            }
            else break;
        }

        _logger.LogDebug("MMR selected {Count} diverse passages from {Total} candidates",
            selected.Count, ranked.Count);

        return selected.Select(pair => pair.passage).ToList();
    }

    /// <summary>
    /// Estimates token count for text using a rough approximation (1 token ≈ 4 characters).
    /// </summary>
    private int EstimateTokenCount(string text)
    {
        return text.Length / 4;
    }

    /// <summary>
    /// Enforces a token budget on the list of passages, ensuring the total doesn't exceed maxTokens.
    /// Always includes at least 1 passage even if it exceeds the budget.
    /// </summary>
    private List<Passage> EnforceTokenBudget(List<Passage> passages, int maxTokens)
    {
        var estimatedTokens = 0;
        var selected = new List<Passage>();

        foreach (var passage in passages)
        {
            var passageTokens = EstimateTokenCount(passage.Text);
            if (estimatedTokens + passageTokens <= maxTokens)
            {
                selected.Add(passage);
                estimatedTokens += passageTokens;
            }
            else break;
        }

        // Always include at least 1 passage
        if (selected.Count == 0 && passages.Count > 0)
        {
            selected.Add(passages[0]);
            estimatedTokens = EstimateTokenCount(passages[0].Text);
        }

        _logger.LogDebug("Token budget: {Actual} tokens (limit: {MaxTokens}), {Count} passages included",
            estimatedTokens, maxTokens, selected.Count);

        return selected;
    }

    /// <summary>
    /// Builds a prompt for LLM-based field extraction from passages.
    /// </summary>
    private string BuildExtractionPrompt(List<Passage> passages, string fieldPath, JSchema fieldSchema)
    {
        var fieldName = fieldPath.TrimStart('$', '.');
        var fieldType = fieldSchema.Type?.ToString() ?? "string";
        var schemaExcerpt = fieldSchema.ToString();

        var prompt = $@"Extract the value for '{fieldName}' from the following passages.

Field type: {fieldType}
Field schema: {schemaExcerpt}

Passages:
{string.Join("\n\n", passages.Select((p, i) => $"[{i}] {p.Text}"))}

Instructions:
1. Find the passage that best answers the question
2. Extract the EXACT value (do NOT infer or calculate)
3. If the value is not explicitly stated, respond with null
4. Validate the extracted value against the schema
5. Provide confidence based on text clarity (0.0-1.0)

Respond in JSON format:
{{
  ""value"": <extracted value matching schema type>,
  ""confidence"": <0.0-1.0>,
  ""passageIndex"": <0-based index of best passage>
}}

If the field cannot be found, respond with:
{{ ""value"": null, ""confidence"": 0.0, ""passageIndex"": null }}";

        return prompt;
    }

    /// <summary>
    /// Computes a hash of the prompt for reproducibility logging.
    /// </summary>
    private string ComputePromptHash(string prompt)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(prompt);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash)[..12];
    }

    /// <summary>
    /// Parses the AI extraction response using multiple strategies.
    /// </summary>
    private (string? Value, double Confidence, int? PassageIndex)? ParseExtractionResponse(string response)
    {
        JObject? json = null;
        var jsonText = response.Trim();

        // Strategy 1: Direct parse
        json = TryParseJson(jsonText);

        if (json == null)
        {
            // Strategy 2: Strip markdown code blocks
            jsonText = Regex.Replace(response, @"```(?:json)?\s*|\s*```", "");
            json = TryParseJson(jsonText);
        }

        if (json == null)
        {
            // Strategy 3: Extract by balanced braces
            jsonText = ExtractJsonByBalancedBraces(response);
            json = TryParseJson(jsonText);
        }

        if (json == null)
        {
            _logger.LogWarning("All JSON parsing strategies failed for response: {Response}", response);
            return null;
        }

        var value = json["value"]?.ToString();
        var confidence = json["confidence"]?.Value<double>() ?? 0.0;
        var passageIndex = json["passageIndex"]?.Value<int>();

        return (value, confidence, passageIndex);
    }

    private JObject? TryParseJson(string text)
    {
        try { return JObject.Parse(text); }
        catch { return null; }
    }

    private string ExtractJsonByBalancedBraces(string text)
    {
        var depth = 0;
        var startIndex = -1;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                if (depth == 0) startIndex = i;
                depth++;
            }
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0 && startIndex >= 0)
                {
                    return text.Substring(startIndex, i - startIndex + 1);
                }
            }
        }

        return text;
    }

    /// <summary>
    /// Validates the extracted value against the schema and attempts type repair.
    /// </summary>
    private bool ValidateAgainstSchema(string? value, JSchema schema, out string? validationError)
    {
        validationError = null;

        if (value == null)
        {
            return true; // null is valid if field is not required
        }

        try
        {
            // Try to parse value based on schema type
            if (schema.Type == JSchemaType.Number || schema.Type == JSchemaType.Integer)
            {
                if (!double.TryParse(value, out _))
                {
                    validationError = $"Value '{value}' is not a valid number";
                    return false;
                }
            }
            else if (schema.Type == JSchemaType.Boolean)
            {
                if (!bool.TryParse(value, out _))
                {
                    validationError = $"Value '{value}' is not a valid boolean";
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            validationError = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Extracts a field value from passages using LLM.
    /// </summary>
    private async Task<ExtractedField?> ExtractFromPassages(
        DocumentPipeline pipeline,
        string fieldPath,
        JSchema fieldSchema,
        List<Passage> passages,
        MeridianOptions options,
        CancellationToken ct)
    {
        if (passages.Count == 0) return null;

        // 1. Build prompt
        var prompt = BuildExtractionPrompt(passages, fieldPath, fieldSchema);

        // 2. Log prompt hash for reproducibility
        var promptHash = ComputePromptHash(prompt);
        _logger.LogDebug("Extraction prompt hash for {FieldPath}: {Hash}", fieldPath, promptHash);

        // 3. Call LLM
        var chatOptions = new AiChatOptions
        {
            Message = prompt,
            Temperature = options.Extraction.Temperature,
            MaxTokens = options.Extraction.MaxOutputTokens,
            Model = options.Extraction.Model ?? "granite3.3:8b"
        };

        _logger.LogDebug("Calling AI for field {FieldPath} with model {Model}", fieldPath, chatOptions.Model);
        var response = await Koan.AI.Ai.Chat(chatOptions, ct);

        // 4. Parse response
        var parsed = ParseExtractionResponse(response);
        if (parsed == null)
        {
            _logger.LogWarning("Failed to parse AI response for field {FieldPath}", fieldPath);
            return null;
        }

        // 5. Validate against schema
        var schemaValid = ValidateAgainstSchema(parsed.Value.Value, fieldSchema, out var validationError);

        // 6. Get best passage
        var passageIndex = parsed.Value.PassageIndex ?? 0;
        if (passageIndex < 0 || passageIndex >= passages.Count)
            passageIndex = 0;

        var bestPassage = passages[passageIndex];

        // 7. Locate span
        var span = LocateSpanInPassage(bestPassage.Text, parsed.Value.Value ?? "");

        // 8. Create ExtractedField
        var extraction = new ExtractedField
        {
            PipelineId = pipeline.Id,
            FieldPath = fieldPath,
            ValueJson = parsed.Value.Value,
            Confidence = parsed.Value.Confidence,
            SourceDocumentId = bestPassage.SourceDocumentId,
            PassageId = bestPassage.Id,
            Evidence = new TextSpanEvidence
            {
                PassageId = bestPassage.Id,
                SourceDocumentId = bestPassage.SourceDocumentId,
                OriginalText = bestPassage.Text,
                Page = bestPassage.PageNumber,
                Section = bestPassage.Section,
                Span = span
            },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _logger.LogInformation("Extracted field {FieldPath}: {Value} (confidence: {Confidence:P0})",
            fieldPath, parsed.Value.Value ?? "null", parsed.Value.Confidence);

        return extraction;
    }

    /// <summary>
    /// Locates the text span within a passage using multiple strategies.
    /// </summary>
    private TextSpan? LocateSpanInPassage(string passageText, string extractedValue)
    {
        if (string.IsNullOrWhiteSpace(passageText) || string.IsNullOrWhiteSpace(extractedValue))
            return null;

        // Strategy 1: Exact match
        var exactIndex = passageText.IndexOf(extractedValue, StringComparison.OrdinalIgnoreCase);
        if (exactIndex >= 0)
        {
            _logger.LogDebug("Span located via exact match");
            return new TextSpan { Start = exactIndex, End = exactIndex + extractedValue.Length };
        }

        // Strategy 2: Numeric normalization
        if (TryLocateNumeric(passageText, extractedValue, out var numericSpan))
        {
            _logger.LogDebug("Span located via numeric normalization");
            return numericSpan;
        }

        // Strategy 3: Regex patterns
        if (TryExtractWithRegex(passageText, extractedValue, out var regexSpan))
        {
            _logger.LogDebug("Span located via regex pattern");
            return regexSpan;
        }

        _logger.LogDebug("No span found for value: {Value}", extractedValue);
        return null;
    }

    private bool TryLocateNumeric(string passageText, string value, out TextSpan? span)
    {
        span = null;
        var normalizedValue = NormalizeNumeric(value);
        if (normalizedValue == null) return false;

        var numberRegex = new Regex(@"[-+]?\$?[\d,]+\.?\d*[MKB]?");
        foreach (Match match in numberRegex.Matches(passageText))
        {
            if (NormalizeNumeric(match.Value) == normalizedValue)
            {
                span = new TextSpan { Start = match.Index, End = match.Index + match.Length };
                return true;
            }
        }
        return false;
    }

    private string? NormalizeNumeric(string value)
    {
        var normalized = value.Replace("$", "").Replace(",", "").Trim();

        if (normalized.EndsWith("M", StringComparison.OrdinalIgnoreCase))
        {
            if (double.TryParse(normalized.TrimEnd('M', 'm'), out var num))
                return (num * 1_000_000).ToString();
        }
        else if (normalized.EndsWith("K", StringComparison.OrdinalIgnoreCase))
        {
            if (double.TryParse(normalized.TrimEnd('K', 'k'), out var num))
                return (num * 1_000).ToString();
        }
        else if (normalized.EndsWith("B", StringComparison.OrdinalIgnoreCase))
        {
            if (double.TryParse(normalized.TrimEnd('B', 'b'), out var num))
                return (num * 1_000_000_000).ToString();
        }

        return double.TryParse(normalized, out _) ? normalized : null;
    }

    private bool TryExtractWithRegex(string passageText, string value, out TextSpan? span)
    {
        span = null;

        // Currency pattern
        if (value.Contains("$") || value.Contains("M") || value.Contains("K") || value.Contains("B"))
        {
            var currencyRegex = new Regex(@"\$[\d,\.]+[MKB]?", RegexOptions.IgnoreCase);
            var match = currencyRegex.Match(passageText);
            if (match.Success)
            {
                span = new TextSpan { Start = match.Index, End = match.Index + match.Length };
                return true;
            }
        }

        // Date pattern
        if (DateTime.TryParse(value, out _))
        {
            var dateRegex = new Regex(@"\d{4}-\d{2}-\d{2}|\w{3}\s+\d{1,2},?\s+\d{4}|\d{1,2}/\d{1,2}/\d{4}");
            var match = dateRegex.Match(passageText);
            if (match.Success)
            {
                span = new TextSpan { Start = match.Index, End = match.Index + match.Length };
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Helper to enumerate leaf fields from JSON schema for field-by-field extraction.
    /// KEEP: This schema traversal logic is correct and reusable.
    /// </summary>
    private static IEnumerable<(string FieldPath, JSchema Schema)> EnumerateLeafSchemas(JSchema root, string prefix = "$")
    {
        if (root.Type == JSchemaType.Object && root.Properties.Count > 0)
        {
            foreach (var property in root.Properties)
            {
                var childPath = string.IsNullOrEmpty(prefix)
                    ? property.Key
                    : $"{prefix}.{property.Key}";

                foreach (var nested in EnumerateLeafSchemas(property.Value, childPath))
                {
                    yield return nested;
                }
            }
            yield break;
        }

        if (root.Type == JSchemaType.Array && root.Items.Count > 0)
        {
            var next = prefix.EndsWith("[]", StringComparison.Ordinal) ? prefix : $"{prefix}[]";
            foreach (var nested in EnumerateLeafSchemas(root.Items[0], next))
            {
                yield return nested;
            }
            yield break;
        }

        yield return (prefix, root);
    }
}
