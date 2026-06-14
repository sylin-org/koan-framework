using Koan.Data.Abstractions;
using Koan.Rag.Abstractions;

namespace Koan.Rag.Workers;

/// <summary>
/// Pre-registered typed job processors, populated by <c>KoanRagAutoRegistrar</c>
/// during initialization. Eliminates runtime reflection in the worker loop.
/// <para>
/// Each entity type with <c>[RagCorpus]</c> gets a delegate registered here
/// at startup. The worker calls the delegate by entity type name — no
/// <c>MakeGenericMethod</c>, no assembly scanning at runtime.
/// </para>
/// </summary>
internal sealed class RagJobProcessorRegistry
{
    private readonly Dictionary<string, Func<RagIngestionJob, CancellationToken, Task>> _processors = new(
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Register a typed processor for an entity type. Called by the auto-registrar
    /// at initialization time. The processor captures a typed delegate that resolves
    /// the entity and corpus at execution time — no runtime reflection needed.
    /// </summary>
    public void Register<TEntity>()
        where TEntity : class, IEntity<string>
    {
        var typeName = typeof(TEntity).Name;

        _processors[typeName] = async (job, ct) =>
        {
            // Resolve RAG service at execution time (not at registration time)
            var ragService = Koan.Core.Hosting.App.AppHost.Current
                ?.GetService(typeof(IRagService)) as IRagService
                ?? throw new InvalidOperationException("IRagService not registered.");

            // Load entity by ID using the typed Data<T,K> facade
            var entity = await Koan.Data.Core.Data<TEntity, string>.Get(job.EntityId, ct)
                ?? throw new InvalidOperationException(
                    $"Entity {job.EntityType}:{job.EntityId} not found.");

            // Re-verify content signature
            var embeddingMeta = Koan.Data.AI.EmbeddingMetadata.Resolve<TEntity>();
            var currentSignature = embeddingMeta.ComputeSignature(entity);

            if (currentSignature != job.ContentSignature)
            {
                job.ContentSignature = currentSignature;
            }

            // Ingest via the corpus
            var corpus = ragService.GetCorpus<TEntity>(job.CorpusName);
            var result = await corpus.Ingest(entity, ct);

            job.ChunksCreated = result.ChunksCreated;
            job.EntitiesExtracted = result.EntitiesExtracted;
        };
    }

    /// <summary>
    /// Process a job using the pre-registered typed processor.
    /// </summary>
    public async Task Process(RagIngestionJob job, CancellationToken ct)
    {
        if (!_processors.TryGetValue(job.EntityType, out var processor))
            throw new InvalidOperationException(
                $"No RAG job processor registered for entity type '{job.EntityType}'. " +
                "Ensure the entity has [RagCorpus] and Koan.Rag is initialized.");

        await processor(job, ct);
    }

    /// <summary>True if at least one processor is registered.</summary>
    public bool HasProcessors => _processors.Count > 0;
}
