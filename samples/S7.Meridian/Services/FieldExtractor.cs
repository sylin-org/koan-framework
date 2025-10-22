using System;
using System.Security.Cryptography;
using System.Globalization;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.AI;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Options;
using Koan.Data.Core;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;

namespace Koan.Samples.Meridian.Services;

public interface IFieldExtractor
{
    Task<List<ExtractedField>> ExtractAsync(DocumentPipeline pipeline, IReadOnlyList<Passage> passages, MeridianOptions options, ISet<string>? fieldFilter, CancellationToken ct);
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
    private readonly IEmbeddingCache _cache;
    private readonly IRunLogWriter _runLog;
    private const string EmbeddingModel = "granite3.3:8b";
    private static readonly char[] KeywordTrimCharacters = { '.', ',', ';', ':', '!', '?', '"', '\'', '(', ')', '[', ']', '{', '}', '-', '_' };
    private static readonly HashSet<string> KeywordStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "find",
        "information",
        "about",
        "please",
        "show",
        "me",
        "the",
        "a",
        "an",
        "any"
    };

    public FieldExtractor(ILogger<FieldExtractor> logger, IEmbeddingCache cache, IRunLogWriter runLog)
    {
        _logger = logger;
        _cache = cache;
        _runLog = runLog;
    }

    public async Task<List<ExtractedField>> ExtractAsync(
        DocumentPipeline pipeline,
        IReadOnlyList<Passage> passages,
        MeridianOptions options,
        ISet<string>? fieldFilter,
        CancellationToken ct)
    {
        var schema = pipeline.TryParseSchema();
        var results = new List<ExtractedField>();

        if (schema == null)
        {
            _logger.LogWarning("Pipeline {PipelineId} schema invalid; skipping extraction.", pipeline.Id);
            return results;
        }

        var sourceTypes = await LoadSourceTypesAsync(pipeline.Id, ct).ConfigureAwait(false);
        var instructionBlock = BuildInstructionBlock(pipeline, sourceTypes);
        var fieldQueryOverrides = BuildFieldQueryOverrides(sourceTypes);
        var keywordHint = BuildKeywordHint(pipeline, sourceTypes);

        HashSet<string>? filter = null;
        if (fieldFilter is { Count: > 0 })
        {
            filter = new HashSet<string>(fieldFilter, StringComparer.Ordinal);
        }

        if (filter is { Count: 0 })
        {
            return results;
        }

        var fieldPaths = EnumerateLeafSchemas(schema).ToList();
        _logger.LogInformation("Extracting {Count} fields for pipeline {PipelineId}", fieldPaths.Count, pipeline.Id);

        foreach (var (fieldPath, fieldSchema) in fieldPaths)
        {
            if (filter is not null && !filter.Contains(fieldPath))
            {
                continue;
            }

            ct.ThrowIfCancellationRequested();

            try
            {
                var query = BuildRAGQuery(fieldPath, pipeline, fieldQueryOverrides, keywordHint);

                var retrieval = await RetrievePassages(pipeline.Id, query, passages, options, ct);
                if (retrieval.Candidates.Count == 0)
                {
                    _logger.LogDebug("No passages retrieved for field {FieldPath}, skipping extraction", fieldPath);
                    continue;
                }

                var orderedCandidates = retrieval.Candidates
                    .OrderByDescending(c => c.score)
                    .ToList();

                var diverse = ApplyMMR(
                    orderedCandidates,
                    Math.Min(options.Retrieval.TopK, orderedCandidates.Count),
                    options.Retrieval.MmrLambda);

                if (diverse.Count == 0)
                {
                    diverse = orderedCandidates
                        .Select(c => c.passage)
                        .Take(options.Retrieval.TopK)
                        .ToList();
                }

                var budgeted = EnforceTokenBudget(diverse, options.Retrieval.MaxTokensPerField);

                var extractionStarted = DateTime.UtcNow;
                var extractionResult = await ExtractFromPassages(pipeline, fieldPath, fieldSchema, budgeted, options, instructionBlock, ct);
                var extractionFinished = DateTime.UtcNow;

                if (extractionResult?.Field is { } field)
                {
                    results.Add(field);
                }

                var logPassageIds = (extractionResult?.PassageIds ?? Array.Empty<string>())
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id!)
                    .ToList();

                if (logPassageIds.Count == 0)
                {
                    logPassageIds = budgeted
                        .Select(p => p.Id)
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .Select(id => id!)
                        .ToList();
                }

                var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["retrievalCandidates"] = retrieval.Candidates.Count.ToString(CultureInfo.InvariantCulture),
                    ["passageCount"] = budgeted.Count.ToString(CultureInfo.InvariantCulture),
                    ["schemaValid"] = (extractionResult?.SchemaValid ?? false).ToString().ToLowerInvariant()
                };

                if (extractionResult?.Confidence is { } confidenceValue && confidenceValue > 0)
                {
                    metadata["confidence"] = confidenceValue.ToString("0.00", CultureInfo.InvariantCulture);
                }

                if (!string.IsNullOrWhiteSpace(extractionResult?.ErrorMessage))
                {
                    metadata["error"] = extractionResult!.ErrorMessage!;
                }

                await _runLog.AppendAsync(new RunLog
                {
                    PipelineId = pipeline.Id,
                    Stage = "extract-field",
                    DocumentId = extractionResult?.DocumentId ?? budgeted.FirstOrDefault()?.SourceDocumentId,
                    FieldPath = fieldPath,
                    StartedAt = extractionStarted,
                    FinishedAt = extractionFinished,
                    Status = extractionResult?.Field is not null ? "success" : "failed",
                    ModelId = extractionResult?.ModelId,
                    PromptHash = extractionResult?.PromptHash,
                    TokensUsed = extractionResult?.TokenEstimate,
                    TopK = options.Retrieval.TopK,
                    Alpha = options.Retrieval.Alpha,
                    PassageIds = logPassageIds,
                    ErrorMessage = extractionResult?.ErrorMessage,
                    Metadata = metadata
                }, ct);
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

    private async Task<List<SourceType>> LoadSourceTypesAsync(string pipelineId, CancellationToken ct)
    {
        var documents = await SourceDocument.Query(d => d.PipelineId == pipelineId, ct).ConfigureAwait(false);
        var ids = documents
            .Select(d => d.ClassifiedTypeId ?? d.SourceType)
            .Where(id => !string.IsNullOrWhiteSpace(id) && !string.Equals(id, MeridianConstants.SourceTypes.Unclassified, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ids.Count == 0)
        {
            return new List<SourceType>();
        }

        var result = new List<SourceType>();
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            var sourceType = await SourceType.Get(id!, ct).ConfigureAwait(false);
            if (sourceType is null)
            {
                _logger.LogWarning("Source type {SourceTypeId} referenced by pipeline {PipelineId} is missing. Documents may be unclassified.", id, pipelineId);
                continue;
            }

            result.Add(sourceType);
        }

        return result;
    }

    private static IReadOnlyDictionary<string, string> BuildFieldQueryOverrides(IEnumerable<SourceType> sourceTypes)
    {
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in sourceTypes)
        {
            foreach (var kvp in type.FieldQueries)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key) || string.IsNullOrWhiteSpace(kvp.Value))
                {
                    continue;
                }

                var key = NormalizeFieldPath(kvp.Key);
                var value = kvp.Value.Trim();

                if (overrides.TryGetValue(key, out var existing))
                {
                    if (!existing.Contains(value, StringComparison.OrdinalIgnoreCase))
                    {
                        overrides[key] = $"{existing} OR {value}";
                    }
                }
                else
                {
                    overrides[key] = value;
                }
            }
        }

        return overrides;
    }

    private static string BuildInstructionBlock(DocumentPipeline pipeline, IEnumerable<SourceType> sourceTypes)
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(pipeline.AnalysisInstructions))
        {
            builder.AppendLine($"[Analysis] {pipeline.AnalysisInstructions.Trim()}");
        }

        foreach (var type in sourceTypes)
        {
            if (string.IsNullOrWhiteSpace(type.Instructions))
            {
                continue;
            }

            builder.AppendLine($"[{type.Name}] {type.Instructions.Trim()}");
        }

        return builder.ToString().Trim();
    }

    private static string BuildKeywordHint(DocumentPipeline pipeline, IEnumerable<SourceType> sourceTypes)
    {
        var keywords = new List<string>();
        if (pipeline.AnalysisTags is { Count: > 0 })
        {
            keywords.AddRange(pipeline.AnalysisTags);
        }

        if (pipeline.RequiredSourceTypes is { Count: > 0 })
        {
            keywords.AddRange(pipeline.RequiredSourceTypes);
        }

    keywords.AddRange(sourceTypes.SelectMany(type => type.SignalPhrases ?? new List<string>()));
        keywords.AddRange(sourceTypes.SelectMany(type => type.Tags ?? new List<string>()));
    keywords.AddRange(sourceTypes.SelectMany(type => type.DescriptorHints ?? new List<string>()));

        var normalized = keywords
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        if (normalized.Count == 0)
        {
            return string.Empty;
        }

        return $"Prioritize passages mentioning: {string.Join(", ", normalized)}.";
    }

    private static string NormalizeFieldPath(string fieldPath)
    {
        if (string.IsNullOrWhiteSpace(fieldPath))
        {
            return string.Empty;
        }

        var trimmed = fieldPath.Trim();
        if (trimmed.StartsWith("$."))
        {
            return trimmed;
        }

        return trimmed.StartsWith("$", StringComparison.Ordinal)
            ? trimmed
            : $"$.{trimmed.TrimStart('.')}";
    }

    private static bool TryGetQueryOverride(
        string fieldPath,
        IReadOnlyDictionary<string, string> overrides,
        out string? query)
    {
        var normalized = NormalizeFieldPath(fieldPath);
        if (normalized.Length == 0)
        {
            query = null;
            return false;
        }

        if (overrides.TryGetValue(normalized, out query))
        {
            return true;
        }

        if (normalized.EndsWith("[]", StringComparison.Ordinal))
        {
            var singular = normalized[..^2];
            if (overrides.TryGetValue(singular, out query))
            {
                return true;
            }
        }

        query = null;
        return false;
    }

    /// <summary>
    /// Builds a semantic query for RAG retrieval based on the field path and schema.
    /// Converts camelCase field names to natural language queries.
    /// </summary>
    private string BuildRAGQuery(
        string fieldPath,
        DocumentPipeline pipeline,
        IReadOnlyDictionary<string, string> fieldQueryOverrides,
        string keywordHint)
    {
        if (TryGetQueryOverride(fieldPath, fieldQueryOverrides, out var overrideQuery) &&
            !string.IsNullOrWhiteSpace(overrideQuery))
        {
            return overrideQuery!;
        }

        var fieldName = fieldPath.TrimStart('$', '.');
        var spaced = Regex.Replace(fieldName, "([a-z])([A-Z])", "$1 $2").ToLower();

        var builder = new StringBuilder();
        builder.Append("Find information about ").Append(spaced).Append('.');

        if (!string.IsNullOrWhiteSpace(pipeline.BiasNotes))
        {
            builder.Append(' ').Append(pipeline.BiasNotes);
        }

        if (!string.IsNullOrWhiteSpace(keywordHint))
        {
            builder.Append(' ').Append(keywordHint);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Retrieves relevant passages using hybrid vector search (BM25 + semantic).
    /// </summary>
    private async Task<RetrievalResult> RetrievePassages(
        string pipelineId,
        string query,
        IReadOnlyList<Passage> corpus,
        MeridianOptions options,
        CancellationToken ct)
    {
        _logger.LogDebug("Embedding query: {Query}", query);
        var queryEmbedding = await Koan.AI.Ai.Embed(query, ct);

        var candidates = new List<(Passage passage, double score, float[]? vector)>();

        if (VectorWorkflow<Passage>.IsAvailable(MeridianConstants.VectorProfile))
        {
            var results = await VectorWorkflow<Passage>.Query(
                new VectorQueryOptions(
                    queryEmbedding,
                    TopK: options.Retrieval.TopK,
                    SearchText: query,
                    Alpha: options.Retrieval.Alpha),
                profileName: MeridianConstants.VectorProfile,
                ct: ct);

            foreach (var match in results.Matches)
            {
                var passage = await Passage.Get(match.Id, ct);
                if (passage != null && passage.PipelineId == pipelineId)
                {
                    var vector = await GetPassageEmbeddingAsync(passage, ct);
                    candidates.Add((passage, match.Score, vector));
                }
            }
        }
        else
        {
            _logger.LogWarning("Vector profile {Profile} unavailable; falling back to lexical search.", MeridianConstants.VectorProfile);

            foreach (var passage in corpus.Where(p => p.PipelineId == pipelineId))
            {
                var score = ComputeKeywordScore(passage.Text, query);
                if (score <= 0)
                {
                    continue;
                }

                var vector = await GetPassageEmbeddingAsync(passage, ct);
                candidates.Add((passage, score, vector));
            }

            candidates = candidates
                .OrderByDescending(c => c.score)
                .Take(options.Retrieval.TopK)
                .ToList();
        }

        _logger.LogInformation("Retrieved {Count} passages for query: {Query}", candidates.Count, query);
        return new RetrievalResult(candidates, queryEmbedding);
    }

    private sealed record RetrievalResult(List<(Passage passage, double score, float[]? vector)> Candidates, float[] QueryEmbedding);

    private sealed record FieldExtractionResult(
        ExtractedField? Field,
        string? DocumentId,
        string PromptHash,
        IReadOnlyList<string> PassageIds,
        string ModelId,
        int TokenEstimate,
        double Confidence,
        bool SchemaValid,
        string? ErrorMessage);

    private async Task<float[]?> GetPassageEmbeddingAsync(Passage passage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(passage.Text))
        {
            return null;
        }

        var hash = EmbeddingCache.ComputeContentHash(passage.Text);
        try
        {
            var cached = await _cache.GetAsync(hash, EmbeddingModel, nameof(Passage), ct);
            if (cached?.Embedding is { Length: > 0 })
            {
                return cached.Embedding;
            }

            var embedding = await Koan.AI.Ai.Embed(passage.Text, ct);
            await _cache.SetAsync(hash, EmbeddingModel, embedding, nameof(Passage), ct);
            return embedding;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to obtain embedding for passage {PassageId}", passage.Id);
            return null;
        }
    }

    private double ComputeKeywordScore(string text, string query)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(query))
        {
            return 0;
        }

        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0)
        {
            return 0;
        }

        var filteredTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var term in terms)
        {
            var cleaned = term.Trim(KeywordTrimCharacters);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                continue;
            }

            if (KeywordStopWords.Contains(cleaned))
            {
                continue;
            }

            filteredTerms.Add(cleaned.ToLowerInvariant());
        }

        if (filteredTerms.Count == 0)
        {
            return 0;
        }

        var lowerText = text.ToLowerInvariant();
        double score = 0;

        foreach (var termLower in filteredTerms)
        {
            if (lowerText.Contains(termLower))
            {
                score += 1;
            }
        }

        return score;
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
    private string BuildExtractionPrompt(
        List<Passage> passages,
        string fieldPath,
        JSchema fieldSchema,
        string instructionBlock)
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

        if (!string.IsNullOrWhiteSpace(instructionBlock))
        {
            prompt += $"\n\nGuidance:\n{instructionBlock}";
        }

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
    private ExtractionResponse? ParseExtractionResponse(string response)
    {
        JObject? json = null;
        var jsonText = response.Trim();

        json = TryParseJson(jsonText);

        if (json == null)
        {
            jsonText = Regex.Replace(response, @"`(?:json)?\s*|\s*`", "");
            json = TryParseJson(jsonText);
        }

        if (json == null)
        {
            jsonText = ExtractJsonByBalancedBraces(response);
            json = TryParseJson(jsonText);
        }

        if (json == null)
        {
            _logger.LogWarning("All JSON parsing strategies failed for response: {Response}", response);
            return null;
        }

        var valueToken = json["value"];
        var confidence = json["confidence"]?.Value<double?>() ?? 0.0;
        var passageIndex = json["passageIndex"]?.Value<int?>();

        return new ExtractionResponse(valueToken, confidence, passageIndex);
    }

        private sealed record ExtractionResponse(JToken? Value, double Confidence, int? PassageIndex);

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
    private bool ValidateAgainstSchema(JToken? valueToken, JSchema schema, out JToken? normalizedToken, out string? validationError)
    {
        normalizedToken = valueToken;
        validationError = null;

        if (valueToken is null || valueToken.Type == JTokenType.Null)
        {
            return true;
        }

        var working = valueToken.DeepClone();

        if (schema.Type.HasValue)
        {
            var types = schema.Type.Value;

            if ((types.HasFlag(JSchemaType.Number) || types.HasFlag(JSchemaType.Integer)) && working is JValue numericValue)
            {
                if (numericValue.Type == JTokenType.String)
                {
                    var text = numericValue.Value<string>()?.Trim();
                    if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedNumber))
                    {
                        working = types.HasFlag(JSchemaType.Integer)
                            ? new JValue(Convert.ToInt64(Math.Round(parsedNumber)))
                            : new JValue(parsedNumber);
                    }
                }
            }
            else if (types.HasFlag(JSchemaType.Boolean) && working is JValue boolValue && boolValue.Type == JTokenType.String)
            {
                var text = boolValue.Value<string>()?.Trim();
                if (bool.TryParse(text, out var parsedBool))
                {
                    working = new JValue(parsedBool);
                }
            }
            else if (types.HasFlag(JSchemaType.String) && working.Type != JTokenType.String)
            {
                working = new JValue(working.ToString(Formatting.None));
            }
        }

        if (!working.IsValid(schema, out IList<string> errors))
        {
            validationError = string.Join("; ", errors);
            normalizedToken = valueToken;
            return false;
        }

        normalizedToken = working;
        return true;
    }
    /// <summary>
    /// Extracts a field value from passages using LLM.
    /// </summary>
    private async Task<FieldExtractionResult?> ExtractFromPassages(
        DocumentPipeline pipeline,
        string fieldPath,
        JSchema fieldSchema,
        List<Passage> passages,
        MeridianOptions options,
        string instructionBlock,
        CancellationToken ct)
    {
        if (passages.Count == 0)
        {
            return null;
        }

        var prompt = BuildExtractionPrompt(passages, fieldPath, fieldSchema, instructionBlock);
        var promptHash = ComputePromptHash(prompt);
        _logger.LogDebug("Extraction prompt hash for {FieldPath}: {Hash}", fieldPath, promptHash);

        var chatOptions = new AiChatOptions
        {
            Message = prompt,
            Temperature = options.Extraction.Temperature,
            MaxTokens = options.Extraction.MaxOutputTokens,
            Model = options.Extraction.Model ?? "granite3.3:8b"
        };

        _logger.LogDebug("Calling AI for field {FieldPath} with model {Model}", fieldPath, chatOptions.Model);
        var response = await Koan.AI.Ai.Chat(chatOptions, ct).ConfigureAwait(false);

        var tokenEstimate = EstimateTokenCount(prompt) + EstimateTokenCount(response);
        var passageIds = passages
            .Select(p => p.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToList();

        var parsed = ParseExtractionResponse(response);
        if (parsed == null)
        {
            _logger.LogWarning("Failed to parse AI response for field {FieldPath}", fieldPath);
            return new FieldExtractionResult(
                null,
                passages.FirstOrDefault()?.SourceDocumentId,
                promptHash,
                passageIds,
                chatOptions.Model ?? string.Empty,
                tokenEstimate,
                0.0,
                false,
                "response-parse-failed");
        }

        var schemaValid = ValidateAgainstSchema(parsed.Value, fieldSchema, out var normalizedToken, out var validationError);
        if (!schemaValid && !string.IsNullOrWhiteSpace(validationError))
        {
            _logger.LogWarning("Schema validation failed for field {FieldPath}: {Error}", fieldPath, validationError);
        }

        string? normalizedJson = null;
        if (normalizedToken is JValue normalizedValue && normalizedValue.Type == JTokenType.String)
        {
            normalizedJson = JsonConvert.SerializeObject(normalizedValue.Value<string>() ?? string.Empty);
        }
        else if (normalizedToken != null)
        {
            normalizedJson = normalizedToken.ToString(Formatting.None);
        }
        else if (parsed.Value != null)
        {
            normalizedJson = parsed.Value.ToString(Formatting.None);
        }

        var stringValue = normalizedToken switch
        {
            JValue jValue when jValue.Type == JTokenType.String => jValue.Value<string>() ?? string.Empty,
            JValue jValue => jValue.ToString(Formatting.None),
            null => parsed.Value?.ToString(Formatting.None) ?? string.Empty,
            _ => normalizedToken.ToString(Formatting.None)
        };

        var confidence = Math.Clamp(parsed.Confidence, 0.0, 1.0);

        var passageIndex = parsed.PassageIndex ?? 0;
        if (passageIndex < 0 || passageIndex >= passages.Count)
        {
            passageIndex = 0;
        }

        var bestPassage = passages[passageIndex];
        var span = LocateSpanInPassage(bestPassage.Text, stringValue);

        var evidenceMetadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["promptHash"] = promptHash,
            ["schemaValid"] = schemaValid ? "true" : "false"
        };

        if (!string.IsNullOrWhiteSpace(validationError))
        {
            evidenceMetadata["schemaError"] = validationError;
        }

        var extraction = new ExtractedField
        {
            PipelineId = pipeline.Id,
            FieldPath = fieldPath,
            ValueJson = normalizedJson,
            Confidence = confidence,
            SourceDocumentId = bestPassage.SourceDocumentId,
            PassageId = bestPassage.Id,
            Evidence = new TextSpanEvidence
            {
                PassageId = bestPassage.Id,
                SourceDocumentId = bestPassage.SourceDocumentId,
                OriginalText = bestPassage.Text,
                Page = bestPassage.PageNumber,
                Section = bestPassage.Section,
                Span = span,
                Metadata = evidenceMetadata
            },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _logger.LogInformation("Extracted field {FieldPath}: {Value} (confidence: {Confidence:P0})",
            fieldPath, normalizedJson ?? "null", confidence);

        return new FieldExtractionResult(
            extraction,
            bestPassage.SourceDocumentId,
            promptHash,
            passageIds,
            chatOptions.Model ?? string.Empty,
            tokenEstimate,
            confidence,
            schemaValid,
            validationError);
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
