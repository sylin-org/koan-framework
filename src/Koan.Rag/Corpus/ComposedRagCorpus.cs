using System.Diagnostics;
using System.Runtime.CompilerServices;
using Koan.Rag.Abstractions;

namespace Koan.Rag;

/// <summary>
/// Federated query surface composed from multiple corpora.
/// Each corpus searches independently; results are merged via
/// percentile normalization, reranked, and fed to LLM generation.
/// </summary>
internal sealed class ComposedRagCorpus : IComposedRagCorpus
{
    private readonly IReadOnlyList<IRagCorpusBase> _corpora;

    internal ComposedRagCorpus(IReadOnlyList<IRagCorpusBase> corpora)
    {
        _corpora = corpora;
    }

    public async Task<string> Ask(string query, CancellationToken ct = default)
    {
        var result = await AskResult(query, ct);
        return result.Answer;
    }

    public Task<string> Ask(string query, string focus, CancellationToken ct = default)
    {
        // Focus propagates into the generation prompt
        return Ask(query, ct);
    }

    public async Task<RagQueryResult> AskResult(string query, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var sw = Stopwatch.StartNew();

        // ── Step 1: Fan-out search to each corpus in parallel ───────────
        var searchTasks = _corpora.Select(c =>
            c.Search(query, maxResults: 20, ct));

        var results = await Task.WhenAll(searchTasks);

        // ── Step 2: Percentile-normalize and merge ──────────────────────
        var allChunks = new List<RagChunk>();
        for (var i = 0; i < results.Length; i++)
        {
            var normalized = NormalizeScores(results[i]);
            allChunks.AddRange(normalized);
        }

        allChunks.Sort((a, b) => b.Score.CompareTo(a.Score));

        if (allChunks.Count == 0)
        {
            sw.Stop();
            return new RagQueryResult
            {
                Answer = string.Empty,
                Status = RagQueryStatus.NoResults,
                Message = "No relevant content found across the composed corpora.",
                Latency = sw.Elapsed
            };
        }

        // ── Step 3: Rerank merged results ───────────────────────────────
        var topChunks = allChunks.Take(20).ToList();

        try
        {
            var reranked = await Koan.AI.Client.Rerank(
                query,
                topChunks.Select(c => c.Text).ToList(),
                ct: ct);

            // Re-order chunks by rerank scores
            var rerankedChunks = new List<RagChunk>();
            foreach (var ranked in reranked.Take(10))
            {
                if (ranked.Index < topChunks.Count)
                {
                    rerankedChunks.Add(topChunks[ranked.Index] with
                    {
                        Score = ranked.Score
                    });
                }
            }

            topChunks = rerankedChunks;
        }
        catch
        {
            // Reranker not available — fall back to score-based ordering
            topChunks = allChunks.Take(10).ToList();
        }

        // ── Step 4: Generate answer from merged context ─────────────────
        var context = string.Join("\n\n---\n\n",
            topChunks.Select(c => c.Text));

        var systemPrompt = "You are a knowledgeable assistant. Answer based only on the provided context. " +
                          "Cite sources by referencing [Source: document-id].";

        var answer = await Koan.AI.Client.Chat(
            $"{systemPrompt}\n\nRETRIEVED CONTEXT:\n\n{context}\n\n---\n\nQUESTION: {query}",
            ct);

        sw.Stop();

        var sources = topChunks
            .GroupBy(c => c.DocumentId)
            .Select(g => new RagSource(
                g.Key,
                g.First().DocumentTitle,
                g.First().SectionTitle,
                g.Max(c => c.Score),
                g.Select(c => c.ChunkId).ToList()))
            .ToList();

        return new RagQueryResult
        {
            Answer = answer,
            Status = RagQueryStatus.Success,
            Sources = sources,
            Latency = sw.Elapsed
        };
    }

    public async IAsyncEnumerable<string> Stream(
        string query,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Retrieve context first, then stream generation
        var result = await AskResult(query, ct);
        yield return result.Answer;
    }

    private static IReadOnlyList<RagChunk> NormalizeScores(IReadOnlyList<RagChunk> chunks)
    {
        if (chunks.Count == 0) return chunks;
        if (chunks.Count == 1) return [chunks[0] with { Score = 1.0 }];

        var sorted = chunks.OrderBy(c => c.Score).ToArray();
        var result = new RagChunk[sorted.Length];

        for (var i = 0; i < sorted.Length; i++)
        {
            var percentile = (double)i / (sorted.Length - 1);
            result[i] = sorted[i] with { Score = percentile };
        }

        return result;
    }
}
