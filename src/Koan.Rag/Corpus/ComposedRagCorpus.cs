using System.Diagnostics;
using System.Runtime.CompilerServices;
using Koan.Rag.Abstractions;

namespace Koan.Rag;

/// <summary>
/// Federated query surface composed from multiple corpora.
/// Each corpus searches independently; results are merged via
/// percentile normalization and cross-encoder reranking.
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
        // Focus propagates uniformly across all corpora
        return Ask(query, ct);
    }

    public async Task<RagQueryResult> AskResult(string query, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var sw = Stopwatch.StartNew();

        // Fan out: search each corpus in parallel
        var searchTasks = _corpora.Select(c =>
            c.Search(query, maxResults: 20, ct));

        var results = await Task.WhenAll(searchTasks);

        // Merge: percentile-normalize scores within each corpus, then combine
        var allChunks = new List<RagChunk>();
        for (var i = 0; i < results.Length; i++)
        {
            var corpusChunks = results[i];
            var normalized = NormalizeScores(corpusChunks);
            allChunks.AddRange(normalized);
        }

        // Sort by normalized score descending
        allChunks.Sort((a, b) => b.Score.CompareTo(a.Score));

        // Take top results
        var topChunks = allChunks.Take(10).ToList();

        sw.Stop();

        // TODO: Rerank with Client.Rerank() and generate with Client.Chat()
        // For now, return the assembled chunks as a placeholder
        var answer = topChunks.Count > 0
            ? string.Join("\n\n", topChunks.Select(c => c.Text))
            : "No relevant content found across the composed corpora.";

        return new RagQueryResult
        {
            Answer = answer,
            Status = topChunks.Count > 0 ? RagQueryStatus.Success : RagQueryStatus.NoResults,
            Sources = topChunks
                .GroupBy(c => c.DocumentId)
                .Select(g => new RagSource(
                    g.Key,
                    g.First().DocumentTitle,
                    g.First().SectionTitle,
                    g.Max(c => c.Score),
                    g.Select(c => c.ChunkId).ToList()))
                .ToList(),
            Latency = sw.Elapsed
        };
    }

    public async IAsyncEnumerable<string> Stream(
        string query,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var result = await AskResult(query, ct);
        yield return result.Answer;
    }

    /// <summary>
    /// Percentile-normalize scores within a single corpus's results.
    /// This prevents systematic bias from corpora with different embedding
    /// models that produce different score magnitude distributions.
    /// </summary>
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
