namespace Koan.Rag.Abstractions;

/// <summary>
/// Non-generic base interface for corpus instances, enabling composition
/// across different entity types via <c>Rag.Compose()</c>.
/// </summary>
public interface IRagCorpusBase
{
    /// <summary>Corpus name. Null for the default (unnamed) corpus.</summary>
    string? Name { get; }

    /// <summary>The entity type this corpus is scoped to.</summary>
    Type EntityType { get; }

    /// <summary>Search for raw chunks (non-generic, returns untyped chunks).</summary>
    Task<IReadOnlyList<RagChunk>> Search(
        string query,
        int maxResults = 10,
        CancellationToken ct = default);
}

/// <summary>
/// A federated query surface composed from multiple corpora.
/// Each corpus searches independently; results are merged via
/// percentile normalization and reranking.
/// </summary>
public interface IComposedRagCorpus
{
    /// <summary>Ask a question across all composed corpora.</summary>
    Task<string> Ask(string query, CancellationToken ct = default);

    /// <summary>Ask with a focus modifier.</summary>
    Task<string> Ask(string query, string focus, CancellationToken ct = default);

    /// <summary>Ask and return a rich result with citations.</summary>
    Task<RagQueryResult> AskResult(string query, CancellationToken ct = default);

    /// <summary>Stream the answer.</summary>
    IAsyncEnumerable<string> Stream(string query, CancellationToken ct = default);
}
