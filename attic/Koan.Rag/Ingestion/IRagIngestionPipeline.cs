using Koan.Data.Abstractions;
using Koan.Rag.Abstractions;

namespace Koan.Rag.Ingestion;

/// <summary>
/// Internal pipeline for document ingestion: extraction, chunking, embedding,
/// concept graph construction. Implementations orchestrate the multi-stage
/// pipeline with parallel extraction via Zen Garden compute.
/// </summary>
internal interface IRagIngestionPipeline
{
    Task<RagIngestResult> IngestFiles<TEntity>(
        IReadOnlyList<string> filePaths,
        RagCorpusMetadata metadata,
        IProgress<RagIngestProgress>? progress,
        CancellationToken ct) where TEntity : class, IEntity<string>;

    Task<RagIngestResult> IngestEntity<TEntity>(
        TEntity entity,
        RagCorpusMetadata metadata,
        CancellationToken ct) where TEntity : class, IEntity<string>;

    Task RemoveEntity<TEntity>(
        TEntity entity,
        RagCorpusMetadata metadata,
        CancellationToken ct) where TEntity : class, IEntity<string>;

    Task Rebuild<TEntity>(
        RagCorpusMetadata metadata,
        CancellationToken ct) where TEntity : class, IEntity<string>;

    Task Rebuild<TEntity>(
        RagCorpusMetadata metadata,
        string? newDirective,
        CancellationToken ct) where TEntity : class, IEntity<string>;

    Task<RagCorpusStats> GetStats<TEntity>(
        RagCorpusMetadata metadata,
        CancellationToken ct) where TEntity : class, IEntity<string>;

    Task<bool> IsReady<TEntity>(
        RagCorpusMetadata metadata,
        CancellationToken ct) where TEntity : class, IEntity<string>;

    Task Clear<TEntity>(
        RagCorpusMetadata metadata,
        CancellationToken ct) where TEntity : class, IEntity<string>;
}
