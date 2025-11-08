using System.Diagnostics;
using System.IO;
using Koan.Context.Models;
using Koan.Data.Core;
using Koan.Data.Vector;
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

    private readonly Embedding _embedding;
    private readonly TokenCounter _tokenCounter;
    private readonly Pagination _Pagination;
    private readonly ILogger<Search> _logger;

    public Search(
        Embedding embedding,
        TokenCounter tokenCounter,
        Pagination Pagination,
        ILogger<Search> logger)
    {
        _embedding = embedding ?? throw new ArgumentNullException(nameof(embedding));
        _tokenCounter = tokenCounter ?? throw new ArgumentNullException(nameof(tokenCounter));
        _Pagination = Pagination ?? throw new ArgumentNullException(nameof(Pagination));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SearchResult> SearchAsync(
        string projectId,
        string query,
        SearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedOptions = NormalizeOptions(options ?? new SearchOptions());

        if (string.IsNullOrWhiteSpace(query))
        {
            return CreateEmptyResult(normalizedOptions, warnings: Array.Empty<string>());
        }

        var stopwatch = Stopwatch.StartNew();
        var warnings = new List<string>();

        if (!string.IsNullOrWhiteSpace(normalizedOptions.ContinuationToken))
        {
            warnings.Add("Continuation tokens are not yet supported; returning the first page only.");
        }

        _logger.LogInformation(
            "Searching project {ProjectId} for query: {Query} (alpha={Alpha}, maxTokens={MaxTokens})",
            projectId,
            query,
            normalizedOptions.Alpha,
            normalizedOptions.MaxTokens);

        try
        {
            var partitionId = $"proj-{Guid.Parse(projectId):N}";
            _logger.LogInformation("Setting partition context: {PartitionId}", partitionId);

            using (EntityContext.Partition(partitionId))
            {
                _logger.LogInformation("Generating embedding for query: {Query}", query);
                var queryEmbedding = await _embedding.EmbedAsync(query, cancellationToken);
                _logger.LogInformation("Generated embedding with dimension: {Dimension}", queryEmbedding.Length);

                if (queryEmbedding.Length == 0)
                {
                    stopwatch.Stop();
                    warnings.Add("Embedding provider returned an empty vector for the supplied query.");
                    return CreateEmptyResult(normalizedOptions, stopwatch.Elapsed, warnings);
                }

                var topK = CalculateTopK(normalizedOptions.MaxTokens);
                _logger.LogInformation("Calling Vector<Chunk>.Search with topK={TopK}, alpha={Alpha}", topK, normalizedOptions.Alpha);

                var vectorResult = await Vector<Chunk>.Search(
                    vector: queryEmbedding,
                    text: query,
                    alpha: normalizedOptions.Alpha,
                    topK: topK,
                    ct: cancellationToken);

                _logger.LogInformation("Vector search returned {MatchCount} matches", vectorResult.Matches.Count);

                var chunks = new List<SearchResultChunk>();
                var sources = new List<SourceFile>();
                var sourceIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var tokensReturned = 0;

                foreach (var match in vectorResult.Matches)
                {
                    _logger.LogInformation("Processing match: ID={MatchId}, Score={Score}", match.Id, match.Score);
                    var documentChunk = await Chunk.Get(match.Id, cancellationToken);
                    if (documentChunk is null)
                    {
                        _logger.LogWarning("Chunk.Get returned null for ID={MatchId} (partition={PartitionId})", match.Id, partitionId);
                        continue;
                    }
                    _logger.LogInformation("Found chunk: ID={ChunkId}, FilePath={FilePath}", documentChunk.Id, documentChunk.FilePath);

                    // Filter by language if specified
                    if (normalizedOptions.Languages != null &&
                        normalizedOptions.Languages.Count > 0 &&
                        !string.IsNullOrWhiteSpace(documentChunk.Language))
                    {
                        var languageMatches = normalizedOptions.Languages
                            .Any(lang => string.Equals(lang, documentChunk.Language, StringComparison.OrdinalIgnoreCase));

                        if (!languageMatches)
                        {
                            _logger.LogDebug("Skipping chunk {ChunkId} - language {Language} not in filter",
                                documentChunk.Id, documentChunk.Language);
                            continue;
                        }
                    }

                    var chunkTokens = documentChunk.TokenCount > 0
                        ? documentChunk.TokenCount
                        : _tokenCounter.EstimateTokens(documentChunk.SearchText);
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
                }

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

                return new SearchResult(
                    Chunks: chunks,
                    Metadata: metadata,
                    Sources: searchSources,
                    Insights: insights,
                    ContinuationToken: null,
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

    private static int CalculateTopK(int maxTokens)
    {
        var estimate = Math.Max(1, maxTokens / EstimatedTokensPerChunk);
        return Math.Clamp(estimate, 4, 20);
    }

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
    List<string>? Languages = null
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
