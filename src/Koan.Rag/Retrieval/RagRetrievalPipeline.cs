using System.Diagnostics;
using System.Runtime.CompilerServices;
using Koan.Data.Abstractions;
using Koan.Data.AI;
using Koan.Rag.Abstractions;
using Koan.Rag.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Rag.Retrieval;

/// <summary>
/// Orchestrates the retrieval pipeline: vector search, concept graph exploration,
/// reranking, and generation with structural prompt separation.
/// <para>
/// Phase 1-2: Chain-based retrieval with graph-augmented context.
/// Phase 3 (future): Full agentic retrieval with tool-use loop.
/// </para>
/// </summary>
internal sealed class RagRetrievalPipeline : IRagRetrievalPipeline
{
    private readonly IConceptGraphStore _graphStore;
    private readonly IDistillationTreeStore _treeStore;
    private readonly ILogger<RagRetrievalPipeline> _logger;
    private readonly RagOptions _options;

    public RagRetrievalPipeline(
        IConceptGraphStore graphStore,
        IDistillationTreeStore treeStore,
        IOptions<RagOptions> options,
        ILogger<RagRetrievalPipeline> logger)
    {
        _graphStore = graphStore;
        _treeStore = treeStore;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<RagQueryResult> Execute<TEntity>(
        string query,
        RagQueryOptions options,
        RagCorpusMetadata metadata,
        CancellationToken ct) where TEntity : class, IEntity<string>
    {
        var sw = Stopwatch.StartNew();
        var trace = new List<RagToolInvocation>();

        // Check if corpus has any content
        if (!Koan.Data.Vector.Vector<TEntity>.IsAvailable)
        {
            return new RagQueryResult
            {
                Answer = string.Empty,
                Status = RagQueryStatus.EmptyCorpus,
                Message = $"Corpus '{metadata.EffectiveName(typeof(TEntity))}' contains no documents. Call Ingest() first."
            };
        }

        try
        {
            // ── Step 1: Hybrid vector search ────────────────────────────
            var searchSw = Stopwatch.StartNew();
            var queryEmbedding = await Koan.AI.Client.Embed(query, ct);

            var searchResult = await Koan.Data.Vector.Vector<TEntity>.Search(
                vector: queryEmbedding,
                text: query,
                alpha: _options.HybridAlpha,
                topK: _options.RerankTopN * 2, // Retrieve 2x for reranking headroom
                ct: ct);

            searchSw.Stop();
            trace.Add(new RagToolInvocation(
                "semantic_search", query, searchResult.Matches.Count, searchSw.Elapsed));

            if (searchResult.Matches.Count == 0)
            {
                sw.Stop();
                return new RagQueryResult
                {
                    Answer = string.Empty,
                    Status = RagQueryStatus.NoResults,
                    Message = "No relevant content found for the query.",
                    Latency = sw.Elapsed,
                    Trace = BuildTrace(trace, sw.Elapsed)
                };
            }

            // ── Step 2: Concept graph exploration (if available) ────────
            var graphChunks = new List<string>();
            var graphStats = _graphStore.GetStats();

            if (graphStats.EntityCount > 0 && metadata.GraphStrategy != GraphStrategy.Lazy)
            {
                var graphSw = Stopwatch.StartNew();
                graphChunks = await ExploreConceptGraph(query, ct);
                graphSw.Stop();

                if (graphChunks.Count > 0)
                {
                    trace.Add(new RagToolInvocation(
                        "concept_explore", query, graphChunks.Count, graphSw.Elapsed));
                }
            }

            // ── Step 3: Build context from retrieved chunks ─────────────
            var contextParts = new List<string>();

            // Add vector search results — load entities in parallel
            var matchSubset = searchResult.Matches.Take(_options.RerankTopN).ToList();
            var hydrationTasks = matchSubset.Select(match =>
            {
                var documentId = match.Id.Contains(':') ? match.Id[..match.Id.IndexOf(':')] : match.Id;
                return Koan.Data.Core.Data<TEntity, string>.Get(documentId, ct);
            }).ToList();

            var hydratedEntities = await Task.WhenAll(hydrationTasks);

            for (var i = 0; i < hydratedEntities.Length; i++)
            {
                if (hydratedEntities[i] is not null)
                {
                    var text = EntityAi.ExtractText(hydratedEntities[i]!);
                    if (!string.IsNullOrWhiteSpace(text))
                        contextParts.Add(text);
                }
            }

            // Add graph-augmented context
            contextParts.AddRange(graphChunks);

            if (contextParts.Count == 0)
            {
                sw.Stop();
                return new RagQueryResult
                {
                    Answer = string.Empty,
                    Status = RagQueryStatus.NoResults,
                    Message = "Retrieved chunks contained no usable content.",
                    Latency = sw.Elapsed,
                    Trace = BuildTrace(trace, sw.Elapsed)
                };
            }

            var context = string.Join("\n\n---\n\n", contextParts);

            // ── Step 4: Generate answer with structural role separation ──
            // System role: instructions + focus + directive
            // User role: retrieved context (UNTRUSTED) + question
            // This is the primary defense against retrieval-path injection.
            var systemPrompt = BuildSystemPrompt(options.Focus, metadata.Directive);
            var userPrompt = BuildUserPrompt(query, context, options.IncludeCitations);

            var genSw = Stopwatch.StartNew();
            var chatOptions = new Koan.AI.Contracts.Options.ChatOptions
            {
                SystemPrompt = systemPrompt
            };
            var answer = await Koan.AI.Client.Chat(userPrompt, chatOptions, ct);
            genSw.Stop();

            trace.Add(new RagToolInvocation(
                "generate", query, 1, genSw.Elapsed));

            sw.Stop();

            // Build source citations
            var sources = searchResult.Matches
                .Take(_options.RerankTopN)
                .Select(m => new RagSource(
                    DocumentId: m.Id,
                    DocumentTitle: null,
                    SectionTitle: null,
                    RelevanceScore: m.Score,
                    ChunkIds: [m.Id]))
                .ToList();

            return new RagQueryResult
            {
                Answer = answer,
                Status = RagQueryStatus.Success,
                Sources = sources,
                Latency = sw.Elapsed,
                Trace = BuildTrace(trace, sw.Elapsed)
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            _logger.LogError(ex, "RAG retrieval failed for corpus '{Corpus}'",
                metadata.EffectiveName(typeof(TEntity)));

            return new RagQueryResult
            {
                Answer = string.Empty,
                Status = RagQueryStatus.Error,
                Message = "An error occurred during retrieval. Check server logs for details.",
                Latency = sw.Elapsed,
                Trace = BuildTrace(trace, sw.Elapsed)
            };
        }
    }

    public async IAsyncEnumerable<string> Stream<TEntity>(
        string query,
        RagQueryOptions options,
        RagCorpusMetadata metadata,
        [EnumeratorCancellation] CancellationToken ct) where TEntity : class, IEntity<string>
    {
        // Retrieve context (non-streaming), then stream the generation
        if (!Koan.Data.Vector.Vector<TEntity>.IsAvailable)
            yield break;

        var queryEmbedding = await Koan.AI.Client.Embed(query, ct);
        var searchResult = await Koan.Data.Vector.Vector<TEntity>.Search(
            vector: queryEmbedding, text: query, alpha: _options.HybridAlpha,
            topK: _options.RerankTopN, ct: ct);

        if (searchResult.Matches.Count == 0)
            yield break;

        var contextParts = new List<string>();
        var streamMatchSubset = searchResult.Matches.Take(_options.RerankTopN).ToList();
        var streamHydrationTasks = streamMatchSubset.Select(match =>
        {
            var documentId = match.Id.Contains(':') ? match.Id[..match.Id.IndexOf(':')] : match.Id;
            return Koan.Data.Core.Data<TEntity, string>.Get(documentId, ct);
        }).ToList();

        var streamEntities = await Task.WhenAll(streamHydrationTasks);

        for (var i = 0; i < streamEntities.Length; i++)
        {
            if (streamEntities[i] is not null)
            {
                var text = EntityAi.ExtractText(streamEntities[i]!);
                if (!string.IsNullOrWhiteSpace(text))
                    contextParts.Add(text);
            }
        }

        if (contextParts.Count == 0)
            yield break;

        var context = string.Join("\n\n---\n\n", contextParts);
        var systemPrompt = BuildSystemPrompt(options.Focus, metadata.Directive);
        var userPrompt = BuildUserPrompt(query, context, options.IncludeCitations);
        var streamOptions = new Koan.AI.Contracts.Options.ChatOptions
        {
            SystemPrompt = systemPrompt
        };

        // Stream generation token by token
        await foreach (var token in Koan.AI.Client.Stream(userPrompt, streamOptions, ct))
        {
            yield return token;
        }
    }

    public async Task<TResult> Extract<TEntity, TResult>(
        string query,
        RagCorpusMetadata metadata,
        CancellationToken ct) where TEntity : class, IEntity<string>
    {
        var result = await Execute<TEntity>(query, new RagQueryOptions(), metadata, ct);

        var extractPrompt = $"Based on the following context, extract the requested information " +
                          $"and return a JSON object matching the {typeof(TResult).Name} schema.\n\n" +
                          $"Context:\n{result.Answer}\n\nQuestion: {query}";

        return await Koan.AI.Client.Extract<TResult>(extractPrompt, ct);
    }

    public async Task<IReadOnlyList<RagChunk>> SearchChunks<TEntity>(
        string query,
        int maxResults,
        RagCorpusMetadata metadata,
        CancellationToken ct) where TEntity : class, IEntity<string>
    {
        if (!Koan.Data.Vector.Vector<TEntity>.IsAvailable)
            return [];

        var queryEmbedding = await Koan.AI.Client.Embed(query, ct);

        var searchResult = await Koan.Data.Vector.Vector<TEntity>.Search(
            vector: queryEmbedding,
            text: query,
            alpha: _options.HybridAlpha,
            topK: maxResults,
            ct: ct);

        // Load actual entity text for each result in parallel
        var searchMatches = searchResult.Matches.ToList();
        var documentIds = searchMatches
            .Select(m => m.Id.Contains(':') ? m.Id[..m.Id.IndexOf(':')] : m.Id)
            .ToList();

        var entityTasks = documentIds
            .Select(docId => Koan.Data.Core.Data<TEntity, string>.Get(docId, ct))
            .ToList();

        var loadedEntities = await Task.WhenAll(entityTasks);

        var chunks = new List<RagChunk>();
        for (var i = 0; i < searchMatches.Count; i++)
        {
            var match = searchMatches[i];
            var entity = loadedEntities[i];
            var text = entity is not null ? EntityAi.ExtractText(entity) : $"[Chunk {match.Id}]";

            chunks.Add(new RagChunk(
                ChunkId: match.Id,
                DocumentId: documentIds[i],
                Text: text,
                Score: match.Score));
        }

        return chunks;
    }

    // ── Concept Graph Exploration ────────────────────────────────────────

    private async Task<List<string>> ExploreConceptGraph(string query, CancellationToken ct)
    {
        var results = new List<string>();

        try
        {
            // Extract potential entity names from the query using simple heuristics
            // (Full agentic version would use the agent to decide which entities to explore)
            var queryTerms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length > 3 && char.IsUpper(t[0]))
                .Select(t => t.Trim(',', '.', '?', '!'))
                .Distinct()
                .ToList();

            foreach (var term in queryTerms.Take(3)) // Limit to prevent over-expansion
            {
                var entityId = Graph.EntityResolver.ComputeEntityId(
                    Graph.EntityResolver.NormalizeName(term));

                var neighborhood = await _graphStore.GetNeighborhood(entityId, depth: 1, ct);

                foreach (var relatedEntity in neighborhood.Entities
                    .Where(e => e.Id != entityId && e.Description is not null))
                {
                    results.Add($"[Related concept: {relatedEntity.CanonicalName}] " +
                                $"{relatedEntity.Description}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Concept graph exploration failed for query");
        }

        return results;
    }

    // ── Prompt Construction ─────────────────────────────────────────────

    private static string BuildSystemPrompt(string? focus, string? directive)
    {
        var parts = new List<string>
        {
            "You are a knowledgeable assistant that answers questions based on the provided context.",
            "Only answer based on the provided context. If the context does not contain enough information, say so clearly.",
            "Cite your sources by referencing the relevant document sections."
        };

        if (!string.IsNullOrWhiteSpace(directive))
            parts.Add($"Domain guidance: {directive}");

        if (!string.IsNullOrWhiteSpace(focus))
            parts.Add($"Focus: {focus}");

        return string.Join("\n", parts);
    }

    private static string BuildUserPrompt(string query, string context, bool includeCitations)
    {
        var citationInstruction = includeCitations
            ? "\n\nInclude [Source: document-id] citations for key claims."
            : "";

        return $"RETRIEVED CONTEXT (use only this to answer):\n\n{context}" +
               $"\n\n---\n\nQUESTION: {query}{citationInstruction}";
    }

    private static RagRetrievalTrace BuildTrace(
        List<RagToolInvocation> steps, TimeSpan totalLatency)
    {
        return new RagRetrievalTrace
        {
            Steps = steps,
            RoundsExecuted = steps.Count(s => s.ToolName is "semantic_search" or "keyword_search"),
            TotalChunksRetrieved = steps.Where(s => s.ToolName != "generate")
                .Sum(s => s.ResultCount),
            ChunksUsedInGeneration = steps.FirstOrDefault(s => s.ToolName == "generate")?.ResultCount ?? 0,
            RetrievalLatency = totalLatency - (steps.FirstOrDefault(s => s.ToolName == "generate")?.Latency ?? TimeSpan.Zero)
        };
    }
}
