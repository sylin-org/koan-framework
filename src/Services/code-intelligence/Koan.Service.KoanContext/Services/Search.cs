using System.Diagnostics;
using System.IO;
using Koan.Context.Models;
using Koan.Data.Core;
using Koan.Data.Vector;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Koan.Context.Services;

/// <summary>
/// Service for semantic and hybrid search over indexed documents with AI-optimized response payloads.
/// </summary>
public class Search
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
        string query,
        SearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedOptions = NormalizeOptions(options ?? new SearchOptions());

        // 1. Resolve audience if provided
        if (!string.IsNullOrWhiteSpace(normalizedOptions.Audience))
        {
            var (audienceCategories, audienceAlpha, audienceMaxTokens) =
                await ResolveAudienceAsync(normalizedOptions.Audience, cancellationToken);

            normalizedOptions = normalizedOptions with
            {
                Categories = audienceCategories,
                Alpha = audienceAlpha,
                MaxTokens = audienceMaxTokens
            };

            _logger.LogInformation(
                "Applied audience profile '{Audience}': categories={Categories}, alpha={Alpha}, maxTokens={MaxTokens}",
                normalizedOptions.Audience,
                string.Join(", ", audienceCategories),
                audienceAlpha,
                audienceMaxTokens);
        }

        // 2. Auto-detect intent if no explicit categories
        if ((normalizedOptions.Categories == null || normalizedOptions.Categories.Count == 0) &&
            string.IsNullOrWhiteSpace(normalizedOptions.Audience))
        {
            var (inferredCategories, inferredAlpha) = InferSearchIntent(query);

            if (inferredCategories.Any())
            {
                normalizedOptions = normalizedOptions with
                {
                    Categories = inferredCategories,
                    Alpha = normalizedOptions.Alpha == 0.7f ? inferredAlpha : normalizedOptions.Alpha
                };

                _logger.LogInformation(
                    "Inferred search intent from query: categories={Categories}, alpha={Alpha}",
                    string.Join(", ", inferredCategories),
                    inferredAlpha);
            }
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return CreateEmptyResult(normalizedOptions, warnings: Array.Empty<string>());
        }

        var stopwatch = Stopwatch.StartNew();
        var warnings = new List<string>();

        // Parse continuation token if provided
        ContinuationTokenData? continuationData = null;
        if (!string.IsNullOrWhiteSpace(normalizedOptions.ContinuationToken))
        {
            continuationData = _Pagination.ParseToken(normalizedOptions.ContinuationToken);
            if (continuationData == null)
            {
                warnings.Add("Invalid or expired continuation token; starting from first page.");
            }
            else if (continuationData.ProjectId != projectId || continuationData.Query != query)
            {
                warnings.Add("Continuation token does not match current query; starting from first page.");
                continuationData = null;
            }
        }

        var currentPage = continuationData?.Page ?? 0;

        _logger.LogInformation(
            "Searching project {ProjectId} for query: {Query} (alpha={Alpha}, maxTokens={MaxTokens})",
            projectId,
            query,
            normalizedOptions.Alpha,
            normalizedOptions.MaxTokens);

        try
        {
            _logger.LogInformation("Setting partition context for project: {ProjectId}", projectId);

            using (EntityContext.Partition(projectId))
            {
                // Get or create embedding (cached across all pages)
                var cacheKey = $"embedding:{query}";
                var queryEmbedding = await _embeddingCache.GetOrCreateAsync(
                    cacheKey,
                    async entry =>
                    {
                        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
                        _logger.LogInformation("Generating embedding for query: {Query}", query);
                        var embedding = await _embedding.EmbedAsync(query, cancellationToken);
                        _logger.LogInformation("Generated embedding with dimension: {Dimension}", embedding.Length);
                        return embedding;
                    });

                _logger.LogInformation(
                    "Embedding: {Source} (dimension={Dim})",
                    _embeddingCache.TryGetValue(cacheKey, out _) ? "cache hit" : "generated",
                    queryEmbedding?.Length ?? 0);

                if (queryEmbedding.Length == 0)
                {
                    stopwatch.Stop();
                    warnings.Add("Embedding provider returned an empty vector for the supplied query.");
                    return CreateEmptyResult(normalizedOptions, stopwatch.Elapsed, warnings);
                }

                // Fetch more results to support continuation
                var topK = MaxTopK;
                _logger.LogInformation("Calling Vector<Chunk>.Search with topK={TopK}, alpha={Alpha}, page={Page}, providerHint={HasHint}",
                    topK, normalizedOptions.Alpha, currentPage, continuationData?.ProviderHint != null ? "present" : "null");

                // Call vector search with provider hint (opaque pass-through)
                var vectorResult = await Vector<Chunk>.Search(
                    vector: queryEmbedding,
                    text: query,
                    alpha: normalizedOptions.Alpha,
                    topK: topK,
                    continuationToken: continuationData?.ProviderHint,  // Opaque to framework
                    ct: cancellationToken);

                _logger.LogInformation(
                    "Vector search returned {MatchCount} matches, providerHint: {HasHint}",
                    vectorResult.Matches.Count,
                    vectorResult.ContinuationToken != null ? "present" : "null");

                // OPTIMIZATION: Batch fetch all chunks instead of individual Get calls
                var matchIds = vectorResult.Matches.Select(m => m.Id).ToList();
                _logger.LogInformation("Batch fetching {Count} chunks from database", matchIds.Count);

                var fetchedChunks = await Chunk.Get(matchIds, cancellationToken);
                var chunkLookup = fetchedChunks
                    .Where(c => c != null)
                    .ToDictionary(c => c!.Id, c => c!);

                _logger.LogInformation("Fetched {FetchedCount}/{RequestedCount} chunks from database ({MissingCount} missing)",
                    chunkLookup.Count, matchIds.Count, matchIds.Count - chunkLookup.Count);

                // Apply filters at database level would be ideal, but for now we filter in-memory
                // with batch fetching to minimize round trips
                if (normalizedOptions.Categories != null && normalizedOptions.Categories.Count > 0)
                {
                    var categoriesLower = normalizedOptions.Categories
                        .Select(c => c.ToLowerInvariant())
                        .ToHashSet();

                    var preFilterCount = chunkLookup.Count;
                    chunkLookup = chunkLookup
                        .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value.Category) &&
                                     categoriesLower.Contains(kvp.Value.Category.ToLowerInvariant()))
                        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                    _logger.LogInformation("Category filter applied: {AfterCount}/{BeforeCount} chunks match categories [{Categories}]",
                        chunkLookup.Count, preFilterCount, string.Join(", ", normalizedOptions.Categories));
                }

                if (normalizedOptions.Languages != null && normalizedOptions.Languages.Count > 0)
                {
                    var languagesLower = normalizedOptions.Languages
                        .Select(l => l.ToLowerInvariant())
                        .ToHashSet();

                    var preFilterCount = chunkLookup.Count;
                    chunkLookup = chunkLookup
                        .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value.Language) &&
                                     languagesLower.Contains(kvp.Value.Language.ToLowerInvariant()))
                        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                    _logger.LogInformation("Language filter applied: {AfterCount}/{BeforeCount} chunks match languages [{Languages}]",
                        chunkLookup.Count, preFilterCount, string.Join(", ", normalizedOptions.Languages));
                }

                var chunks = new List<SearchResultChunk>();
                var sources = new List<SourceFile>();
                var sourceIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var tokensReturned = 0;
                var skippedCount = 0;
                var shouldSkip = continuationData != null;
                string? lastChunkId = null;

                // Now iterate through matches in vector search order
                foreach (var match in vectorResult.Matches)
                {
                    // Look up chunk from batch-fetched results
                    if (!chunkLookup.TryGetValue(match.Id, out var documentChunk))
                    {
                        // Chunk was filtered out or missing from database
                        continue;
                    }

                    // Skip chunks until we reach the last returned chunk from previous page
                    if (shouldSkip)
                    {
                        if (documentChunk.Id == continuationData!.LastChunkId)
                        {
                            shouldSkip = false; // Found the last chunk, start collecting from next one
                            _logger.LogInformation("Found continuation point at chunk {ChunkId}, skipped {SkippedCount} chunks", documentChunk.Id, skippedCount);
                        }
                        skippedCount++;
                        continue;
                    }

                    var chunkTokens = documentChunk.TokenCount > 0
                        ? documentChunk.TokenCount
                        : _tokenCounter.EstimateTokens(documentChunk.SearchText);

                    // Check if adding this chunk would exceed token budget
                    if (tokensReturned + chunkTokens > normalizedOptions.MaxTokens && chunks.Count > 0)
                    {
                        _logger.LogInformation("Token budget reached ({TokensReturned}/{MaxTokens}), stopping at {ChunkCount} chunks",
                            tokensReturned, normalizedOptions.MaxTokens, chunks.Count);
                        break; // Stop processing, we have enough results
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

                    var reasoning = normalizedOptions.IncludeReasoning
                        ? BuildReasoning((float)match.Score, normalizedOptions.Alpha)
                        : null;

                    var startLine = documentChunk.StartLine <= 0 ? 1 : documentChunk.StartLine;
                    var endLine = documentChunk.EndLine <= 0 ? startLine : documentChunk.EndLine;

                    chunks.Add(new SearchResultChunk(
                        Id: documentChunk.Id,
                        Text: documentChunk.SearchText,
                        Score: (float)match.Score,
                        Provenance: new ChunkProvenance(
                            SourceIndex: index,
                            StartByteOffset: documentChunk.StartByteOffset,
                            EndByteOffset: documentChunk.EndByteOffset,
                            StartLine: startLine,
                            EndLine: endLine,
                            Language: documentChunk.Language),
                        Reasoning: reasoning));

                    lastChunkId = documentChunk.Id; // Track last chunk added
                }

                // Determine if there are more results available
                var hasMoreResults = chunks.Count > 0 && (skippedCount + chunks.Count) < vectorResult.Matches.Count;

                stopwatch.Stop();

                var metadata = new SearchMetadata(
                    TokensRequested: normalizedOptions.MaxTokens,
                    TokensReturned: tokensReturned,
                    Page: 1,
                    Model: _embedding.GetType().Name,
                    VectorProvider: "default",
                    Timestamp: DateTime.UtcNow,
                    Duration: stopwatch.Elapsed);

                var searchSources = new SearchSources(
                    TotalFiles: sources.Count,
                    Files: sources);

                var insights = normalizedOptions.IncludeInsights
                    ? BuildInsights(chunks, searchSources)
                    : null;

                // Generate continuation token if there are more results
                string? continuationToken = null;
                if (hasMoreResults && !string.IsNullOrEmpty(lastChunkId))
                {
                    var tokenData = new ContinuationTokenData(
                        ProjectId: projectId,
                        Query: query,
                        Alpha: normalizedOptions.Alpha,
                        TokensRemaining: normalizedOptions.MaxTokens,
                        LastChunkId: lastChunkId,
                        CreatedAt: DateTime.UtcNow,
                        Page: currentPage + 1,
                        ProviderHint: vectorResult.ContinuationToken);  // Store provider hint opaquely

                    continuationToken = _Pagination.CreateToken(tokenData);
                    _logger.LogInformation("Generated continuation token for page {Page}, last chunk: {LastChunkId}, providerHint: {HasHint}",
                        currentPage + 1, lastChunkId, vectorResult.ContinuationToken != null ? "present" : "null");
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
            return CreateEmptyResult(normalizedOptions, stopwatch.Elapsed, warnings);
        }
    }

    private static SearchOptions NormalizeOptions(SearchOptions options)
    {
        var maxTokens = Math.Clamp(options.MaxTokens, MinTokenBudget, MaxTokenBudget);
        var alpha = Math.Clamp(options.Alpha, 0f, 1f);
        return options with { MaxTokens = maxTokens, Alpha = alpha };
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

    /// <summary>
    /// Resolves an audience profile from the database and returns its categories, alpha, and maxTokens
    /// </summary>
    /// <remarks>
    /// Uses IMemoryCache with 30-minute TTL to avoid repeated database queries.
    /// Returns defaults if audience not found or inactive.
    /// </remarks>
    private async Task<(List<string> Categories, float Alpha, int MaxTokens)> ResolveAudienceAsync(
        string? audienceName,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(audienceName))
            return (new List<string>(), 0.5f, 5000);

        // Check cache first (30-min TTL)
        var cacheKey = $"audience:{audienceName}";
        var audience = await _embeddingCache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);

            try
            {
                var result = await SearchAudience.Query(
                    a => a.Name == audienceName && a.IsActive,
                    ct);

                return result.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load audience '{Audience}' from database", audienceName);
                return null;
            }
        });

        if (audience == null)
        {
            _logger.LogWarning("Audience '{Audience}' not found or inactive, using defaults", audienceName);
            return (new List<string>(), 0.5f, 5000);
        }

        return (audience.CategoryNames, audience.DefaultAlpha, audience.MaxTokens);
    }

    /// <summary>
    /// Infers search intent from query text and returns suggested categories and alpha
    /// </summary>
    /// <remarks>
    /// Heuristic-based intent detection for common query patterns.
    /// Returns empty list if no specific intent detected (defaults will apply).
    /// </remarks>
    private (List<string> Categories, float Alpha) InferSearchIntent(string query)
    {
        var lower = query.ToLowerInvariant();

        // Documentation-seeking queries
        if (lower.Contains("documentation") || lower.Contains("guide") ||
            lower.Contains("learn") || lower.Contains("tutorial"))
        {
            _logger.LogDebug("Inferred documentation intent from query");
            return (new() { "guide", "documentation" }, 0.4f);
        }

        // Decision/rationale queries
        if (lower.Contains("why") || lower.Contains("decision") ||
            lower.Contains("rationale") || lower.Contains("adr"))
        {
            _logger.LogDebug("Inferred decision/rationale intent from query");
            return (new() { "adr" }, 0.3f);
        }

        // Example/sample queries
        if (lower.Contains("example") || lower.Contains("sample") ||
            lower.Contains("demo") || lower.Contains("show me"))
        {
            _logger.LogDebug("Inferred example/sample intent from query");
            return (new() { "sample", "test" }, 0.5f);
        }

        // Implementation queries
        if (lower.Contains("implement") || lower.Contains("code") ||
            lower.Contains("class") || lower.Contains("method"))
        {
            _logger.LogDebug("Inferred implementation intent from query");
            return (new() { "source" }, 0.7f);
        }

        // How-to queries
        if (lower.Contains("how to") || lower.Contains("how do i"))
        {
            _logger.LogDebug("Inferred how-to intent from query");
            return (new() { "guide", "sample" }, 0.4f);
        }

        // Architecture/overview queries
        if (lower.Contains("architecture") || lower.Contains("overview") ||
            lower.Contains("design"))
        {
            _logger.LogDebug("Inferred architecture/overview intent from query");
            return (new() { "adr", "documentation" }, 0.3f);
        }

        // Default: no specific intent detected
        return (new List<string>(), 0.5f);
    }

    private SearchResult CreateEmptyResult(
        SearchOptions options,
        TimeSpan? duration = null,
        IReadOnlyList<string>? warnings = null)
    {
        var metadata = new SearchMetadata(
            TokensRequested: options.MaxTokens,
            TokensReturned: 0,
            Page: 1,
            Model: _embedding.GetType().Name,
            VectorProvider: "default",
            Timestamp: DateTime.UtcNow,
            Duration: duration ?? TimeSpan.Zero);

        var insights = options.IncludeInsights
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
/// Options for semantic search with AI optimization
/// </summary>
public record SearchOptions(
    int MaxTokens = 5000,
    float Alpha = 0.7f,
    string? ContinuationToken = null,
    bool IncludeInsights = true,
    bool IncludeReasoning = true,
    List<string>? Languages = null,
    List<string>? Categories = null,  // Filter by categories (e.g., ["guide", "documentation"])
    string? Audience = null            // Apply audience profile (e.g., "learner", "architect")
);

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
