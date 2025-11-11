using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Koan.Context.Models;
using Koan.Data.Core;
using Koan.Data.Vector;
using Koan.Service.KoanContext.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Koan.Context.Services;

/// <summary>
/// Service for semantic and hybrid search over indexed documents with AI-optimized response payloads.
/// </summary>
public interface ISearchService
{
    Task<SearchResult> SearchAsync(
        string projectId,
        SearchRequestContext request,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ISearchService"/>
public class Search : ISearchService
{
    private const int MinTokenBudget = 1000;
    private const int MaxTokenBudget = 10000;
    private const int EstimatedTokensPerChunk = 350;
    private const int MaxTopK = 100; // Increased from 20 to support continuation

    private readonly Embedding _embedding;
    private readonly TokenCounter _tokenCounter;
    private readonly Pagination _Pagination;
    private readonly IMemoryCache _embeddingCache;
    private readonly ILogger<Search> _logger;

    public Search(
        Embedding embedding,
        TokenCounter tokenCounter,
        Pagination Pagination,
        IMemoryCache embeddingCache,
        ILogger<Search> logger)
    {
        _embedding = embedding ?? throw new ArgumentNullException(nameof(embedding));
        _tokenCounter = tokenCounter ?? throw new ArgumentNullException(nameof(tokenCounter));
        _Pagination = Pagination ?? throw new ArgumentNullException(nameof(Pagination));
        _embeddingCache = embeddingCache ?? throw new ArgumentNullException(nameof(embeddingCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SearchResult> SearchAsync(
        string projectId,
        SearchRequestContext request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var warnings = new List<string>();

        var persona = await ResolvePersonaAsync(request.PersonaId, cancellationToken);
        var tokenBudget = Math.Clamp(
            Math.Min(request.MaxTokens, persona.MaxTokens),
            MinTokenBudget,
            MaxTokenBudget);

        var effectiveAny = request.TagsAny.Count > 0
            ? request.TagsAny
            : persona.GetDefaultTagsAny();

        var effectiveAll = request.TagsAll.Count > 0
            ? request.TagsAll
            : persona.GetDefaultTagsAll();

        var effectiveExclude = request.TagsExclude.Count > 0
            ? request.TagsExclude
            : persona.GetDefaultTagsExclude();

        var mergedBoosts = MergeBoosts(persona.GetTagBoosts(), request.TagBoosts);

        ContinuationTokenData? continuationData = null;
        var currentPage = 0;
        var shouldSkip = false;

        if (!string.IsNullOrWhiteSpace(request.ContinuationToken))
        {
            continuationData = _Pagination.ParseToken(request.ContinuationToken);
            if (continuationData == null)
            {
                warnings.Add("Invalid or expired continuation token; starting from first page.");
            }
            else if (!string.Equals(continuationData.ProjectId, projectId, StringComparison.Ordinal) ||
                     !string.Equals(continuationData.Query, request.Query, StringComparison.Ordinal))
            {
                warnings.Add("Continuation token does not match current query; starting from first page.");
                continuationData = null;
            }
            else
            {
                currentPage = continuationData.Page;
                shouldSkip = true;
            }
        }

        var alpha = Math.Clamp(persona.SemanticWeight, 0f, 1f);
        var totalWeight = Math.Max(persona.SemanticWeight + persona.TagWeight + persona.RecencyWeight, 0.001f);

        _logger.LogInformation(
            "Searching project {ProjectId} for query: {Query} (alpha={Alpha}, persona={Persona}, maxTokens={MaxTokens})",
            projectId,
            request.Query,
            alpha,
            persona.Name,
            tokenBudget);

        try
        {
            using (EntityContext.Partition(projectId))
            {
                var cacheKey = $"embedding:{request.Query}";
                var queryEmbedding = await _embeddingCache.GetOrCreateAsync(
                    cacheKey,
                    async entry =>
                    {
                        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
                        _logger.LogInformation("Generating embedding for query: {Query}", request.Query);
                        var embedding = await _embedding.EmbedAsync(request.Query, cancellationToken);
                        _logger.LogInformation("Generated embedding with dimension: {Dimension}", embedding.Length);
                        return embedding;
                    });

                if (queryEmbedding is null || queryEmbedding.Length == 0)
                {
                    stopwatch.Stop();
                    warnings.Add("Embedding provider returned an empty vector for the supplied query.");
                    return CreateEmptyResult(request, stopwatch.Elapsed, warnings);
                }

                var topK = MaxTopK;
                _logger.LogInformation(
                    "Calling Vector<Chunk>.Search with topK={TopK}, alpha={Alpha}, page={Page}, providerHint={HasHint}",
                    topK,
                    alpha,
                    currentPage,
                    continuationData?.ProviderHint != null ? "present" : "null");

                var vectorResult = await Vector<Chunk>.Search(
                    vector: queryEmbedding,
                    text: request.Query,
                    alpha: alpha,
                    topK: topK,
                    continuationToken: continuationData?.ProviderHint,
                    ct: cancellationToken);

                _logger.LogInformation(
                    "Vector search returned {MatchCount} matches, providerHint: {HasHint}",
                    vectorResult.Matches.Count,
                    vectorResult.ContinuationToken != null ? "present" : "null");

                var matchIds = vectorResult.Matches.Select(static m => m.Id).ToList();
                var fetchedChunks = await Chunk.Get(matchIds, cancellationToken);
                var chunkLookup = fetchedChunks
                    .Where(static c => c != null)
                    .ToDictionary(static c => c!.Id, static c => c!, StringComparer.Ordinal);

                var chunks = new List<SearchResultChunk>();
                var sources = new List<SourceFile>();
                var sourceIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var tokensReturned = 0;
                var skippedCount = 0;
                string? lastChunkId = continuationData?.LastChunkId;

                foreach (var match in vectorResult.Matches)
                {
                    if (!chunkLookup.TryGetValue(match.Id, out var documentChunk))
                    {
                        continue;
                    }

                    if (shouldSkip)
                    {
                        if (string.Equals(documentChunk.Id, continuationData!.LastChunkId, StringComparison.Ordinal))
                        {
                            shouldSkip = false;
                            _logger.LogInformation(
                                "Found continuation point at chunk {ChunkId}, skipped {SkippedCount} chunks",
                                documentChunk.Id,
                                skippedCount);
                        }

                        skippedCount++;
                        continue;
                    }

                    if (request.Languages != null && request.Languages.Count > 0)
                    {
                        if (string.IsNullOrWhiteSpace(documentChunk.Language) ||
                            !request.Languages.Contains(documentChunk.Language.Trim().ToLowerInvariant()))
                        {
                            continue;
                        }
                    }

                    var chunkTags = ExtractChunkTags(documentChunk);
                    if (!PassesTagFilters(chunkTags, effectiveAny, effectiveAll, effectiveExclude))
                    {
                        continue;
                    }

                    var chunkTokens = documentChunk.TokenCount > 0
                        ? documentChunk.TokenCount
                        : _tokenCounter.EstimateTokens(documentChunk.SearchText);

                    if (tokensReturned + chunkTokens > tokenBudget && chunks.Count > 0)
                    {
                        break;
                    }

                    tokensReturned += chunkTokens;

                    var sourceKey = $"{documentChunk.FilePath}|{documentChunk.CommitSha}";
                    if (!sourceIndex.TryGetValue(sourceKey, out var index))
                    {
                        index = sources.Count;
                        sources.Add(new SourceFile(
                            FilePath: documentChunk.FilePath,
                            Title: documentChunk.Title ?? Path.GetFileName(documentChunk.FilePath),
                            Url: documentChunk.SourceUrl,
                            CommitSha: documentChunk.CommitSha ?? string.Empty));
                        sourceIndex[sourceKey] = index;
                    }

                    var semanticScore = (float)match.Score;
                    var tagScore = ComputeTagScore(chunkTags, effectiveAny, effectiveAll, effectiveExclude, mergedBoosts);
                    var recencyScore = ComputeRecencyScore(documentChunk);
                    var combinedScore = (semanticScore * persona.SemanticWeight) +
                                       (tagScore * persona.TagWeight) +
                                       (recencyScore * persona.RecencyWeight);
                    combinedScore /= totalWeight;

                    var reasoning = request.IncludeReasoning
                        ? BuildReasoning(semanticScore, alpha)
                        : null;

                    var startLine = documentChunk.StartLine <= 0 ? 1 : documentChunk.StartLine;
                    var endLine = documentChunk.EndLine <= 0 ? startLine : documentChunk.EndLine;

                    chunks.Add(new SearchResultChunk(
                        Id: documentChunk.Id,
                        Text: documentChunk.SearchText,
                        Score: combinedScore,
                        Provenance: new ChunkProvenance(
                            SourceIndex: index,
                            StartByteOffset: documentChunk.StartByteOffset,
                            EndByteOffset: documentChunk.EndByteOffset,
                            StartLine: startLine,
                            EndLine: endLine,
                            Language: documentChunk.Language),
                        Reasoning: reasoning));

                    lastChunkId = documentChunk.Id;
                }

                var hasMoreResults = chunks.Count > 0 &&
                    (skippedCount + chunks.Count) < vectorResult.Matches.Count;

                stopwatch.Stop();

                var metadata = new SearchMetadata(
                    TokensRequested: tokenBudget,
                    TokensReturned: tokensReturned,
                    Page: currentPage + 1,
                    Model: _embedding.GetType().Name,
                    VectorProvider: "default",
                    Timestamp: DateTime.UtcNow,
                    Duration: stopwatch.Elapsed);

                var searchSources = new SearchSources(
                    TotalFiles: sources.Count,
                    Files: sources);

                var insights = request.IncludeInsights
                    ? BuildInsights(chunks, searchSources)
                    : null;

                string? continuationToken = null;
                if (hasMoreResults && !string.IsNullOrEmpty(lastChunkId))
                {
                    var tokenData = new ContinuationTokenData(
                        ProjectId: projectId,
                        Query: request.Query,
                        Alpha: alpha,
                        TokensRemaining: tokenBudget,
                        LastChunkId: lastChunkId,
                        CreatedAt: DateTime.UtcNow,
                        Page: currentPage + 1,
                        ProviderHint: vectorResult.ContinuationToken);

                    continuationToken = _Pagination.CreateToken(tokenData);
                }

                return new SearchResult(
                    Chunks: chunks,
                    Metadata: metadata,
                    Sources: searchSources,
                    Insights: insights,
                    ContinuationToken: continuationToken,
                    Warnings: warnings);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Search failed for project {ProjectId}", projectId);
            warnings.Add($"Search failed: {ex.Message}");
            return CreateEmptyResult(request, stopwatch.Elapsed, warnings);
        }
    }

    // Note: CalculateTopK removed - now using fixed MaxTopK=100 to fetch more results for continuation support

    private static RetrievalReasoning BuildReasoning(float score, float alpha)
    {
        var semanticWeight = Math.Clamp(alpha, 0f, 1f);
        var keywordWeight = 1f - semanticWeight;

        var strategy = semanticWeight switch
        {
            <= 0.05f => "keyword",
            >= 0.95f => "vector",
            _ => "hybrid"
        };

        return new RetrievalReasoning(
            SemanticScore: score * semanticWeight,
            KeywordScore: score * keywordWeight,
            Strategy: strategy);
    }

    private static SearchInsights BuildInsights(
        IReadOnlyList<SearchResultChunk> chunks,
        SearchSources sources)
    {
        if (chunks.Count == 0)
        {
            return new SearchInsights(
                Topics: new Dictionary<string, int>(),
                CompletenessLevel: "insufficient",
                MissingTopics: Array.Empty<string>());
        }

        var topics = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var chunk in chunks)
        {
            var source = sources.Files[chunk.Provenance.SourceIndex];
            var topic = ExtractTopic(source.FilePath);

            if (topics.TryGetValue(topic, out var count))
            {
                topics[topic] = count + 1;
            }
            else
            {
                topics[topic] = 1;
            }
        }

        var completeness = chunks.Count switch
        {
            >= 8 => "comprehensive",
            >= 3 => "partial",
            _ => "insufficient"
        };

        return new SearchInsights(
            Topics: topics,
            CompletenessLevel: completeness,
            MissingTopics: Array.Empty<string>());
    }

    private static string ExtractTopic(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return "general";
        }

        var sanitized = filePath.Replace('\\', '/');
        var parts = sanitized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? sanitized : parts[0];
    }

    private async Task<SearchPersona> ResolvePersonaAsync(string? personaId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(personaId))
        {
            return SearchPersona.Create(
                name: "general",
                displayName: "General",
                description: "Default persona",
                semanticWeight: 0.65f,
                tagWeight: 0.25f,
                recencyWeight: 0.10f,
                maxTokens: 6000);
        }

        var normalizedPersonaId = personaId.Trim().ToLowerInvariant();
        var cacheKey = Constants.CacheKeys.Persona(normalizedPersonaId);

        var persona = await _embeddingCache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);

            try
            {
                var matches = await SearchPersona.Query(
                    p => p.Name == normalizedPersonaId && p.IsActive,
                    ct);

                return matches.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load persona '{Persona}' from database", normalizedPersonaId);
                return null;
            }
        });

        if (persona == null)
        {
            _logger.LogWarning("Persona '{Persona}' not found or inactive, using general defaults", normalizedPersonaId);
            return SearchPersona.Create(
                name: normalizedPersonaId,
                displayName: personaId!,
                description: "Fallback persona",
                semanticWeight: 0.65f,
                tagWeight: 0.25f,
                recencyWeight: 0.10f,
                maxTokens: 6000);
        }

        return persona;
    }

    private static IReadOnlyDictionary<string, float> MergeBoosts(
        IReadOnlyDictionary<string, float> personaBoosts,
        IReadOnlyDictionary<string, float> requestBoosts)
    {
        if ((personaBoosts == null || personaBoosts.Count == 0) &&
            (requestBoosts == null || requestBoosts.Count == 0))
        {
            return new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        }

        var merged = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        if (personaBoosts != null)
        {
            foreach (var kvp in personaBoosts)
            {
                merged[kvp.Key] = kvp.Value;
            }
        }

        if (requestBoosts != null)
        {
            foreach (var kvp in requestBoosts)
            {
                if (merged.TryGetValue(kvp.Key, out var existing))
                {
                    merged[kvp.Key] = Math.Max(existing, kvp.Value);
                }
                else
                {
                    merged[kvp.Key] = kvp.Value;
                }
            }
        }

        return merged;
    }

    private static bool PassesTagFilters(
        HashSet<string> chunkTags,
        IReadOnlyList<string> tagsAny,
        IReadOnlyList<string> tagsAll,
        IReadOnlyList<string> tagsExclude)
    {
        if (chunkTags.Count == 0)
        {
            return tagsAny.Count == 0 && tagsAll.Count == 0;
        }

        if (tagsExclude.Count > 0 && tagsExclude.Any(chunkTags.Contains))
        {
            return false;
        }

        if (tagsAny.Count > 0 && !tagsAny.Any(chunkTags.Contains))
        {
            return false;
        }

        if (tagsAll.Count > 0 && !tagsAll.All(chunkTags.Contains))
        {
            return false;
        }

        return true;
    }

    private static HashSet<string> ExtractChunkTags(Chunk chunk)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);

        void AddRange(IEnumerable<string> values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    set.Add(value);
                }
            }
        }

        AddRange(chunk.Tags.Primary);
        AddRange(chunk.Tags.Secondary);
        AddRange(chunk.Tags.File);

        return set;
    }

    private static float ComputeTagScore(
        HashSet<string> chunkTags,
        IReadOnlyList<string> tagsAny,
        IReadOnlyList<string> tagsAll,
        IReadOnlyList<string> tagsExclude,
        IReadOnlyDictionary<string, float> tagBoosts)
    {
        if (!PassesTagFilters(chunkTags, tagsAny, tagsAll, tagsExclude))
        {
            return 0f;
        }

        float anyScore = 0f;
        if (tagsAny.Count > 0)
        {
            var matches = tagsAny.Count(chunkTags.Contains);
            anyScore = (float)matches / tagsAny.Count;
        }

        float allScore = 0f;
        if (tagsAll.Count > 0)
        {
            allScore = tagsAll.All(chunkTags.Contains) ? 1f : 0f;
        }

        float boostScore = 0f;
        if (tagBoosts != null && tagBoosts.Count > 0)
        {
            foreach (var tag in chunkTags)
            {
                if (tagBoosts.TryGetValue(tag, out var weight))
                {
                    boostScore += weight;
                }
            }
        }

        var baseScore = (0.6f * anyScore) + (0.4f * allScore) + boostScore;
        return Math.Clamp(baseScore, 0f, 2f);
    }

    private static float ComputeRecencyScore(Chunk chunk)
    {
        var reference = chunk.FileLastModified != default
            ? chunk.FileLastModified
            : chunk.IndexedAt;

        if (reference == default)
        {
            return 0.5f;
        }

        var ageDays = (float)(DateTime.UtcNow - reference).TotalDays;
        if (ageDays <= 0)
        {
            return 1f;
        }

        const float halfLife = 90f;
        var score = MathF.Exp(-ageDays / halfLife);
        return Math.Clamp(score, 0f, 1f);
    }

    private SearchResult CreateEmptyResult(
        SearchRequestContext request,
        TimeSpan? duration = null,
        IReadOnlyList<string>? warnings = null)
    {
        var metadata = new SearchMetadata(
            TokensRequested: request.MaxTokens,
            TokensReturned: 0,
            Page: 1,
            Model: _embedding.GetType().Name,
            VectorProvider: "default",
            Timestamp: DateTime.UtcNow,
            Duration: duration ?? TimeSpan.Zero);

        var insights = request.IncludeInsights
            ? new SearchInsights(
                Topics: new Dictionary<string, int>(),
                CompletenessLevel: "insufficient",
                MissingTopics: Array.Empty<string>())
            : null;

        return new SearchResult(
            Chunks: Array.Empty<SearchResultChunk>(),
            Metadata: metadata,
            Sources: new SearchSources(0, Array.Empty<SourceFile>()),
            Insights: insights,
            ContinuationToken: null,
            Warnings: warnings ?? Array.Empty<string>());
    }
}

/// <summary>
/// Enhanced search result with AI-optimized metadata
/// </summary>
public record SearchResult(
    IReadOnlyList<SearchResultChunk> Chunks,
    SearchMetadata Metadata,
    SearchSources Sources,
    SearchInsights? Insights,
    string? ContinuationToken,
    IReadOnlyList<string> Warnings
);

/// <summary>
/// Individual search result chunk with provenance and reasoning
/// </summary>
public record SearchResultChunk(
    string Id,
    string Text,
    float Score,
    ChunkProvenance Provenance,
    RetrievalReasoning? Reasoning
);

/// <summary>
/// Detailed provenance for traceability and citation
/// </summary>
public record ChunkProvenance(
    int SourceIndex,
    long StartByteOffset,
    long EndByteOffset,
    int StartLine,
    int EndLine,
    string? Language
);

/// <summary>
/// Search execution metadata
/// </summary>
public record SearchMetadata(
    int TokensRequested,
    int TokensReturned,
    int Page,
    string Model,
    string VectorProvider,
    DateTime Timestamp,
    TimeSpan Duration
);

/// <summary>
/// Deduplicated source files
/// </summary>
public record SearchSources(
    int TotalFiles,
    IReadOnlyList<SourceFile> Files
);

/// <summary>
/// Source file information
/// </summary>
public record SourceFile(
    string FilePath,
    string? Title,
    string? Url,
    string CommitSha
);

/// <summary>
/// Lean reasoning trace for AI explainability
/// </summary>
public record RetrievalReasoning(
    float SemanticScore,
    float KeywordScore,
    string Strategy
);

/// <summary>
/// Aggregated insights across all chunks
/// </summary>
public record SearchInsights(
    IReadOnlyDictionary<string, int> Topics,
    string CompletenessLevel,
    IReadOnlyList<string>? MissingTopics
);
