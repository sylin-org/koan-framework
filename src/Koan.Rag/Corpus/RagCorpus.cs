using System.Runtime.CompilerServices;
using Koan.Data.Abstractions;
using Koan.Rag.Abstractions;
using Koan.Rag.Ingestion;
using Koan.Rag.Retrieval;
using Microsoft.Extensions.Logging;

namespace Koan.Rag;

/// <summary>
/// Core implementation of <see cref="IRagCorpus{TEntity}"/>. Each instance
/// represents a named or unnamed knowledge corpus scoped to a single entity type.
/// Thread-safe: concurrent Ingest and Ask operations are supported.
/// </summary>
internal sealed class RagCorpus<TEntity> : IRagCorpus<TEntity> where TEntity : class, IEntity<string>
{
    private readonly RagCorpusMetadata _metadata;
    private readonly IRagIngestionPipeline _ingestionPipeline;
    private readonly IRagRetrievalPipeline _retrievalPipeline;
    private readonly ILogger _logger;

    public RagCorpus(
        RagCorpusMetadata metadata,
        IRagIngestionPipeline ingestionPipeline,
        IRagRetrievalPipeline retrievalPipeline,
        ILogger logger)
    {
        _metadata = metadata;
        _ingestionPipeline = ingestionPipeline;
        _retrievalPipeline = retrievalPipeline;
        _logger = logger;
    }

    public string? Name => _metadata.Name;
    public string? Directive => _metadata.Directive;
    public Type EntityType => typeof(TEntity);

    // ── Ingestion ───────────────────────────────────────────────────────

    public async Task<RagIngestResult> Ingest(
        IEnumerable<string> filePaths,
        IProgress<RagIngestProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        var paths = filePaths as IReadOnlyList<string> ?? filePaths.ToList();

        _logger.LogInformation(
            "Ingesting {FileCount} files into corpus '{Corpus}' [{EntityType}]",
            paths.Count, _metadata.EffectiveName(typeof(TEntity)), typeof(TEntity).Name);

        return await _ingestionPipeline.IngestFiles<TEntity>(
            paths, _metadata, progress, ct);
    }

    public async Task<RagIngestResult> Ingest(TEntity entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        return await _ingestionPipeline.IngestEntity(
            entity, _metadata, ct);
    }

    public async Task Remove(TEntity entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        await _ingestionPipeline.RemoveEntity(entity, _metadata, ct);
    }

    // ── Query ───────────────────────────────────────────────────────────

    public async Task<string> Ask(string query, CancellationToken ct = default)
    {
        var result = await AskResult(query, ct);
        return result.Status == RagQueryStatus.EmptyCorpus
            ? throw new RagCorpusEmptyException(_metadata.EffectiveName(typeof(TEntity)))
            : result.Answer;
    }

    public Task<string> Ask(string query, string focus, CancellationToken ct = default)
        => Ask(query, new RagQueryOptions { Focus = focus }, ct);

    public async Task<string> Ask(string query, RagQueryOptions options, CancellationToken ct = default)
    {
        var result = await AskResult(query, options, ct);
        return result.Status == RagQueryStatus.EmptyCorpus
            ? throw new RagCorpusEmptyException(_metadata.EffectiveName(typeof(TEntity)))
            : result.Answer;
    }

    public Task<RagQueryResult> AskResult(string query, CancellationToken ct = default)
        => AskResult(query, new RagQueryOptions(), ct);

    public Task<RagQueryResult> AskResult(string query, string focus, CancellationToken ct = default)
        => AskResult(query, new RagQueryOptions { Focus = focus }, ct);

    public async Task<RagQueryResult> AskResult(string query, RagQueryOptions options, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(options);

        return await _retrievalPipeline.Execute<TEntity>(
            query, options, _metadata, ct);
    }

    public async IAsyncEnumerable<string> Stream(
        string query,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var token in _retrievalPipeline.Stream<TEntity>(
            query, new RagQueryOptions(), _metadata, ct))
        {
            yield return token;
        }
    }

    public async IAsyncEnumerable<string> Stream(
        string query,
        string focus,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var token in _retrievalPipeline.Stream<TEntity>(
            query, new RagQueryOptions { Focus = focus }, _metadata, ct))
        {
            yield return token;
        }
    }

    public async Task<TResult> Ask<TResult>(string query, CancellationToken ct = default)
    {
        return await _retrievalPipeline.Extract<TEntity, TResult>(
            query, _metadata, ct);
    }

    public async Task<IReadOnlyList<RagChunk>> Search(
        string query, int maxResults = 10, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        return await _retrievalPipeline.SearchChunks<TEntity>(
            query, maxResults, _metadata, ct);
    }

    // ── Session ─────────────────────────────────────────────────────────

    public IRagSession<TEntity> Session(RagSessionOptions? options = null)
    {
        return new RagSession<TEntity>(
            this, _retrievalPipeline, _metadata, options ?? new RagSessionOptions());
    }

    // ── Operations ──────────────────────────────────────────────────────

    public async Task Rebuild(CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Rebuilding corpus '{Corpus}' [{EntityType}]",
            _metadata.EffectiveName(typeof(TEntity)), typeof(TEntity).Name);

        await _ingestionPipeline.Rebuild<TEntity>(_metadata, ct);
    }

    public async Task Rebuild(RagRebuildOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Directive is not null && !options.Confirm)
            throw new InvalidOperationException(
                "Rebuild with a new directive is destructive (re-extracts all documents). " +
                "Set Confirm = true to acknowledge.");

        _logger.LogInformation(
            "Rebuilding corpus '{Corpus}' [{EntityType}] with new directive",
            _metadata.EffectiveName(typeof(TEntity)), typeof(TEntity).Name);

        await _ingestionPipeline.Rebuild<TEntity>(_metadata, options.Directive, ct);
    }

    public Task<RagCorpusStats> Stats(CancellationToken ct = default)
        => _ingestionPipeline.GetStats<TEntity>(_metadata, ct);

    public Task<bool> IsReady(CancellationToken ct = default)
        => _ingestionPipeline.IsReady<TEntity>(_metadata, ct);

    public async Task Clear(CancellationToken ct = default)
    {
        _logger.LogWarning(
            "Clearing corpus '{Corpus}' [{EntityType}] — all documents, chunks, and graph data removed",
            _metadata.EffectiveName(typeof(TEntity)), typeof(TEntity).Name);

        await _ingestionPipeline.Clear<TEntity>(_metadata, ct);
    }
}
