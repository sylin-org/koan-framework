using Koan.Data.Abstractions;
using Koan.Rag.Abstractions;

namespace Koan.Rag.Retrieval;

/// <summary>
/// Internal pipeline for query execution: agentic retrieval, reranking,
/// compression, generation. The agent decides which tools to use per query.
/// </summary>
internal interface IRagRetrievalPipeline
{
    Task<RagQueryResult> Execute<TEntity>(
        string query,
        RagQueryOptions options,
        RagCorpusMetadata metadata,
        CancellationToken ct) where TEntity : class, IEntity<string>;

    IAsyncEnumerable<string> Stream<TEntity>(
        string query,
        RagQueryOptions options,
        RagCorpusMetadata metadata,
        CancellationToken ct) where TEntity : class, IEntity<string>;

    Task<TResult> Extract<TEntity, TResult>(
        string query,
        RagCorpusMetadata metadata,
        CancellationToken ct) where TEntity : class, IEntity<string>;

    Task<IReadOnlyList<RagChunk>> SearchChunks<TEntity>(
        string query,
        int maxResults,
        RagCorpusMetadata metadata,
        CancellationToken ct) where TEntity : class, IEntity<string>;
}
