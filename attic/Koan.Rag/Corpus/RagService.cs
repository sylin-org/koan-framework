using System.Collections.Concurrent;
using Koan.Data.Abstractions;
using Koan.Rag.Abstractions;
using Koan.Rag.Evaluation;
using Koan.Rag.Ingestion;
using Koan.Rag.Retrieval;
using Microsoft.Extensions.Logging;

namespace Koan.Rag;

/// <summary>
/// Singleton service managing corpus instances. Each (entity type, name) pair
/// resolves to the same corpus instance. Registered via <c>KoanRagAutoRegistrar</c>.
/// </summary>
internal sealed class RagService : IRagService
{
    private readonly ConcurrentDictionary<(Type, string?), object> _corpora = new();
    private readonly IRagIngestionPipeline _ingestionPipeline;
    private readonly IRagRetrievalPipeline _retrievalPipeline;
    private readonly RagEvaluator _evaluator;
    private readonly ILoggerFactory _loggerFactory;

    public RagService(
        IRagIngestionPipeline ingestionPipeline,
        IRagRetrievalPipeline retrievalPipeline,
        RagEvaluator evaluator,
        ILoggerFactory loggerFactory)
    {
        _ingestionPipeline = ingestionPipeline;
        _retrievalPipeline = retrievalPipeline;
        _evaluator = evaluator;
        _loggerFactory = loggerFactory;
    }

    public IRagCorpus<TEntity> GetCorpus<TEntity>(string? name = null) where TEntity : class, IEntity<string>
    {
        var key = (typeof(TEntity), name);
        return (IRagCorpus<TEntity>)_corpora.GetOrAdd(key, _ =>
        {
            var metadata = name is null
                ? RagCorpusMetadata.ResolveDefault<TEntity>()
                : RagCorpusMetadata.Resolve<TEntity>(name)
                    ?? throw new RagCorpusNotFoundException(name, typeof(TEntity).Name);

            return CreateCorpus<TEntity>(metadata);
        });
    }

    public IRagCorpus<TEntity> GetCorpus<TEntity>(string name, string directive) where TEntity : class, IEntity<string>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var key = (typeof(TEntity), (string?)name);
        return (IRagCorpus<TEntity>)_corpora.GetOrAdd(key, _ =>
        {
            // Try attribute-declared first, then create with provided directive
            var metadata = RagCorpusMetadata.Resolve<TEntity>(name);
            if (metadata is not null && directive != metadata.Directive)
            {
                var warnLogger = _loggerFactory.CreateLogger("Koan.Rag");
                warnLogger.LogWarning(
                    "Directive ignored for corpus '{Name}' [{Entity}]: attribute-declared directive takes precedence. " +
                    "Use Rebuild(new RagRebuildOptions {{ Directive = \"...\" }}) to change.",
                    name, typeof(TEntity).Name);
            }
            metadata ??= RagCorpusMetadata.CreateDynamic(name, directive);

            return CreateCorpus<TEntity>(metadata);
        });
    }

    private RagCorpus<TEntity> CreateCorpus<TEntity>(RagCorpusMetadata metadata) where TEntity : class, IEntity<string>
    {
        var logger = _loggerFactory.CreateLogger($"Koan.Rag.Corpus<{typeof(TEntity).Name}>" +
            (metadata.Name is not null ? $"[{metadata.Name}]" : ""));

        return new RagCorpus<TEntity>(
            metadata, _ingestionPipeline, _retrievalPipeline, _evaluator, logger);
    }
}
