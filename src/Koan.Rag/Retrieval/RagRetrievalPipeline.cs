using System.Diagnostics;
using System.Runtime.CompilerServices;
using Koan.Data.Abstractions;
using Koan.Data.AI;
using Koan.Rag.Abstractions;
using Microsoft.Extensions.Logging;

namespace Koan.Rag.Retrieval;

/// <summary>
/// Orchestrates the retrieval pipeline: agentic tool selection, vector search,
/// concept graph exploration, reranking, compression, and generation.
/// <para>
/// Phase 1: Chain-based retrieval (vector search + Chat generation).
/// Phase 3: Agentic retrieval with tool set and adaptive complexity.
/// </para>
/// </summary>
internal sealed class RagRetrievalPipeline : IRagRetrievalPipeline
{
    private readonly ILogger<RagRetrievalPipeline> _logger;

    public RagRetrievalPipeline(ILogger<RagRetrievalPipeline> logger)
    {
        _logger = logger;
    }

    public async Task<RagQueryResult> Execute<TEntity>(
        string query,
        RagQueryOptions options,
        RagCorpusMetadata metadata,
        CancellationToken ct) where TEntity : class, IEntity<string>
    {
        var sw = Stopwatch.StartNew();

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
            // Phase 1: Simple vector search + Chat generation
            // Phase 3 will replace this with agentic retrieval

            // 1. Embed the query
            var queryEmbedding = await Koan.AI.Client.Embed(query, ct);

            // 2. Hybrid search
            var searchResult = await Koan.Data.Vector.Vector<TEntity>.Search(
                vector: queryEmbedding,
                text: query,
                alpha: 0.6, // Lean semantic
                topK: options.Hint?.MaxRounds is not null ? 20 : 10,
                ct: ct);

            if (searchResult.Matches.Count == 0)
            {
                sw.Stop();
                return new RagQueryResult
                {
                    Answer = string.Empty,
                    Status = RagQueryStatus.NoResults,
                    Message = "No relevant content found for the query.",
                    Latency = sw.Elapsed
                };
            }

            // 3. Build context from retrieved chunks
            // TODO: Load actual chunk text from chunk store
            // For now, use the match metadata or entity content
            var context = await BuildContext<TEntity>(searchResult.Matches, ct);

            // 4. Build generation prompt with structural role separation
            //    (system = instructions, user/tool-result = retrieved content)
            var systemPrompt = BuildSystemPrompt(options.Focus, metadata.Directive);
            var userPrompt = BuildUserPrompt(query, context);

            // 5. Generate answer
            var combinedPrompt = $"{systemPrompt}\n\n{userPrompt}";
            var answer = await Koan.AI.Client.Chat(combinedPrompt, ct);

            sw.Stop();

            // 6. Build sources from search results
            var sources = searchResult.Matches
                .Select(m => new RagSource(
                    DocumentId: m.Id,
                    DocumentTitle: null, // TODO: from chunk metadata
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
                Trace = new RagRetrievalTrace
                {
                    RoundsExecuted = 1,
                    TotalChunksRetrieved = searchResult.Matches.Count,
                    ChunksUsedInGeneration = searchResult.Matches.Count,
                    RetrievalLatency = sw.Elapsed,
                    Steps =
                    [
                        new RagToolInvocation("semantic_search", query,
                            searchResult.Matches.Count, sw.Elapsed)
                    ]
                }
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            _logger.LogError(ex, "RAG retrieval failed for query in corpus '{Corpus}'",
                metadata.EffectiveName(typeof(TEntity)));

            return new RagQueryResult
            {
                Answer = string.Empty,
                Status = RagQueryStatus.Error,
                Message = $"Retrieval error: {ex.Message}",
                Latency = sw.Elapsed
            };
        }
    }

    public async IAsyncEnumerable<string> Stream<TEntity>(
        string query,
        RagQueryOptions options,
        RagCorpusMetadata metadata,
        [EnumeratorCancellation] CancellationToken ct) where TEntity : class, IEntity<string>
    {
        // Phase 1: Non-streaming implementation — return full answer as single yield
        // Phase 3: True streaming via Client.Stream with agentic retrieval
        var result = await Execute<TEntity>(query, options, metadata, ct);
        yield return result.Answer;
    }

    public async Task<TResult> Extract<TEntity, TResult>(
        string query,
        RagCorpusMetadata metadata,
        CancellationToken ct) where TEntity : class, IEntity<string>
    {
        // Retrieve context, then use Client.Extract<T> for typed output
        var result = await Execute<TEntity>(query, new RagQueryOptions(), metadata, ct);

        // Use the retrieved context + query to extract a typed result
        var extractPrompt = $"Based on the following context, answer the question and " +
                          $"return the result as a JSON object matching the {typeof(TResult).Name} schema.\n\n" +
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
            alpha: 0.6,
            topK: maxResults,
            ct: ct);

        return searchResult.Matches
            .Select(m => new RagChunk(
                ChunkId: m.Id,
                DocumentId: m.Id,
                Text: $"[Chunk {m.Id}]", // TODO: Load actual chunk text
                Score: m.Score))
            .ToList();
    }

    private static async Task<string> BuildContext<TEntity>(
        IReadOnlyList<Koan.Data.Vector.Abstractions.VectorMatch<string>> matches,
        CancellationToken ct) where TEntity : class, IEntity<string>
    {
        // TODO: Load actual chunk text from chunk store
        // For Phase 1, load entities and extract their text
        var contextParts = new List<string>();

        foreach (var match in matches.Take(5))
        {
            var entity = await Koan.Data.Core.Data<TEntity, string>.Get(match.Id, ct);
            if (entity is not null)
            {
                var text = EntityAi.ExtractText(entity);
                if (!string.IsNullOrWhiteSpace(text))
                    contextParts.Add(text);
            }
        }

        return string.Join("\n\n---\n\n", contextParts);
    }

    private static string BuildSystemPrompt(string? focus, string? directive)
    {
        var parts = new List<string>
        {
            "You are a knowledgeable assistant that answers questions based on the provided context.",
            "Only answer based on the provided context. If the context does not contain enough information, say so.",
            "Cite your sources by referencing the document sections used."
        };

        if (!string.IsNullOrWhiteSpace(directive))
            parts.Add($"Domain guidance: {directive}");

        if (!string.IsNullOrWhiteSpace(focus))
            parts.Add($"Focus: {focus}");

        return string.Join("\n", parts);
    }

    private static string BuildUserPrompt(string query, string context)
    {
        return $"Context:\n{context}\n\n---\n\nQuestion: {query}";
    }
}
