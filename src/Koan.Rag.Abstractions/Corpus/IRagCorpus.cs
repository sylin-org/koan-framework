using Koan.Data.Abstractions;

namespace Koan.Rag.Abstractions;

/// <summary>
/// A typed, persistent knowledge corpus scoped to an entity type.
/// Provides ingestion, querying, and operational methods.
/// <para>
/// Obtained via <c>Rag.Corpus&lt;T&gt;()</c> (default) or
/// <c>Rag.Corpus&lt;T&gt;("name", "directive")</c> (named).
/// </para>
/// </summary>
/// <typeparam name="TEntity">The entity type this corpus is scoped to.</typeparam>
public interface IRagCorpus<TEntity> : IRagCorpusBase where TEntity : class, IEntity<string>
{
    /// <summary>Corpus name. Null for the default (unnamed) corpus.</summary>
    new string? Name { get; }

    /// <summary>The natural-language directive shaping extraction behavior.</summary>
    string? Directive { get; }

    // ── Ingestion ───────────────────────────────────────────────────────

    /// <summary>Ingest files into the corpus.</summary>
    Task<RagIngestResult> Ingest(
        IEnumerable<string> filePaths,
        IProgress<RagIngestProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>Ingest a single entity into the corpus.</summary>
    Task<RagIngestResult> Ingest(TEntity entity, CancellationToken ct = default);

    /// <summary>Remove an entity from the corpus.</summary>
    Task Remove(TEntity entity, CancellationToken ct = default);

    // ── Query ───────────────────────────────────────────────────────────

    /// <summary>Ask a question — returns the answer as a string.</summary>
    Task<string> Ask(string query, CancellationToken ct = default);

    /// <summary>Ask with a focus modifier — shapes retrieval and generation.</summary>
    Task<string> Ask(string query, string focus, CancellationToken ct = default);

    /// <summary>Ask with full options — escape hatch for advanced control.</summary>
    Task<string> Ask(string query, RagQueryOptions options, CancellationToken ct = default);

    /// <summary>Ask and return a rich result with citations, confidence, and trace.</summary>
    Task<RagQueryResult> AskResult(string query, CancellationToken ct = default);

    /// <summary>Ask with focus and return a rich result.</summary>
    Task<RagQueryResult> AskResult(string query, string focus, CancellationToken ct = default);

    /// <summary>Ask with full options and return a rich result.</summary>
    Task<RagQueryResult> AskResult(string query, RagQueryOptions options, CancellationToken ct = default);

    /// <summary>Stream the answer token by token.</summary>
    IAsyncEnumerable<string> Stream(string query, CancellationToken ct = default);

    /// <summary>Stream with a focus modifier.</summary>
    IAsyncEnumerable<string> Stream(string query, string focus, CancellationToken ct = default);

    /// <summary>Ask and extract a typed result.</summary>
    Task<TResult> Ask<TResult>(string query, CancellationToken ct = default);

    /// <summary>
    /// Retrieve raw chunks without synthesis. For debugging and evaluation.
    /// </summary>
    new Task<IReadOnlyList<RagChunk>> Search(
        string query,
        int maxResults = 10,
        CancellationToken ct = default);

    // ── Session ─────────────────────────────────────────────────────────

    /// <summary>Create a stateful conversation session.</summary>
    IRagSession<TEntity> Session(RagSessionOptions? options = null);

    // ── Operations ──────────────────────────────────────────────────────

    /// <summary>Full rebuild with current directive.</summary>
    Task Rebuild(CancellationToken ct = default);

    /// <summary>Rebuild with options (e.g., new directive).</summary>
    Task Rebuild(RagRebuildOptions options, CancellationToken ct = default);

    /// <summary>Corpus health, metrics, and freshness indicators.</summary>
    Task<RagCorpusStats> Stats(CancellationToken ct = default);

    /// <summary>True when the corpus is indexed and ready for queries.</summary>
    Task<bool> IsReady(CancellationToken ct = default);

    /// <summary>Remove all documents, chunks, and graph data. Dev/test only.</summary>
    Task Clear(CancellationToken ct = default);
}
