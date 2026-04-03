using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Koan.Data.Core;
using Koan.Rag.Abstractions;
using Koan.Rag.Infrastructure;
using Koan.Rag.Ingestion;
using Koan.Rag.Retrieval;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.Rag.Initialization;

/// <summary>
/// Auto-registers RAG infrastructure when <c>Koan.Rag</c> is referenced.
/// Discovers <c>[RagCorpus]</c>-decorated entities, wires entity lifecycle hooks,
/// and registers the ingestion worker, health check, and service layer.
/// <para>
/// <b>Reference = Intent:</b> Adding a package reference to <c>Koan.Rag</c> and calling
/// <c>services.AddKoan()</c> enables RAG for all <c>[RagCorpus]</c>-decorated entities
/// with zero additional configuration.
/// </para>
/// </summary>
public sealed class KoanRagAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Rag";
    public string? ModuleVersion => typeof(KoanRagAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // ── Core Services ───────────────────────────────────────────────
        services.TryAddSingleton<IRagIngestionPipeline, RagIngestionPipeline>();
        services.TryAddSingleton<IRagRetrievalPipeline, RagRetrievalPipeline>();
        services.TryAddSingleton<IRagService, RagService>();
        services.TryAddSingleton<IConceptGraphStore, Graph.InMemoryConceptGraphStore>();

        // ── Pipeline Components ─────────────────────────────────────────
        services.TryAddSingleton<Chunking.ContextualChunker>();
        services.TryAddSingleton<Graph.EntityExtractor>();
        services.TryAddSingleton<Graph.EntityResolver>();
        services.TryAddSingleton<Evaluation.RagEvaluator>();

        // ── Content Adapters ────────────────────────────────────────────
        services.TryAddSingleton<Content.Strategies.StrategyGenerator>();
        services.TryAddSingleton<Content.ContentAdapterRegistry>();
        services.TryAddSingleton<IContentAdapter, Content.Adapters.TextDocumentAdapter>();
        services.TryAddSingleton<IContentAdapter, Content.Adapters.ImageAdapter>();
        services.TryAddSingleton<IContentAdapter, Content.Adapters.AudioAdapter>();
        services.TryAddSingleton<IContentAdapter, Content.Adapters.PdfAdapter>();

        // ── Distillation Tree ───────────────────────────────────────────
        services.TryAddSingleton<IClusteringStrategy, Distillation.DiagonalGmmClustering>();
        services.TryAddSingleton<IDistillationTreeStore, Distillation.InMemoryDistillationTreeStore>();
        services.TryAddSingleton<Distillation.DistillationTreeBuilder>();

        // ── Document Segmentation ───────────────────────────────────────
        services.TryAddSingleton<IDocumentSegmenter, Segmentation.TextDocumentSegmenter>();

        // ── Configuration ───────────────────────────────────────────────
        services.AddOptions<RagOptions>()
            .BindConfiguration(Infrastructure.ConfigurationKeys.Rag)
            .ValidateDataAnnotations();

        // ── Background Worker ───────────────────────────────────────────
        services.AddHostedService<Workers.RagIngestionWorker>();

        // ── Health Check ────────────────────────────────────────────────
        services.AddHealthChecks()
            .AddCheck<Health.RagCorpusHealthCheck>(
                "rag-corpus",
                tags: ["ai", "rag", "ready"]);

        // ── Entity Lifecycle Hooks + Worker Processors ────────────────────
        // Scan loaded assemblies for [RagCorpus]-decorated entities
        var ragTypes = DiscoverRagCorpusTypes();
        var processorRegistry = new Workers.RagJobProcessorRegistry();
        services.AddSingleton(processorRegistry);

        foreach (var entityType in ragTypes)
        {
            var declarations = RagCorpusMetadata.ResolveAll(entityType);
            foreach (var metadata in declarations)
            {
                if (metadata.LifecycleEnabled)
                {
                    RegisterIngestionHooks(entityType);
                    RegisterJobProcessor(processorRegistry, entityType);
                    break; // One registration per entity type (serves all corpora)
                }
            }
        }
    }

    public void Describe(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);

        var ragTypes = DiscoverRagCorpusTypes();

        if (ragTypes.Count == 0)
        {
            module.SetNote("status", n => n
                .Message("No [RagCorpus] entities registered"));
            return;
        }

        var totalCorpora = 0;
        var corpusDescriptions = new List<string>();

        foreach (var entityType in ragTypes.OrderBy(t => t.Name))
        {
            var declarations = RagCorpusMetadata.ResolveAll(entityType);
            foreach (var meta in declarations)
            {
                var label = meta.Name is not null ? $"[{meta.Name}]" : "[default]";
                var strategy = meta.GraphStrategy.ToString().ToLowerInvariant();
                var mode = meta.Async ? "async" : "sync";

                var desc = $"{entityType.Name} {label}: graph={strategy}, {mode}" +
                    (meta.Directive is not null ? $", directive=\"{Truncate(meta.Directive, 40)}\"" : "");
                corpusDescriptions.Add(desc);
                totalCorpora++;
            }
        }

        module.SetNote("corpora", n => n
            .Message($"{totalCorpora} corpora across {ragTypes.Count} entity types: " +
                   string.Join("; ", corpusDescriptions)));

        // Configuration settings
        var defaultStrategy = cfg["Koan:Rag:GraphStrategy"] ?? "Lightweight";
        var rerankEnabled = cfg["Koan:Rag:RerankEnabled"] ?? "true";

        module.AddSetting("Rag:GraphStrategy", defaultStrategy,
            source: BootSettingSource.AppSettings);
        module.AddSetting("Rag:RerankEnabled", rerankEnabled,
            source: BootSettingSource.AppSettings);

        // Report distillation tree stats if available
        var treeStore = Koan.Core.Hosting.App.AppHost.Current
            ?.GetService(typeof(IDistillationTreeStore)) as IDistillationTreeStore;
        if (treeStore is not null)
        {
            var treeStats = treeStore.GetStats();
            module.SetNote("distillation-tree", n => n
                .Message($"Distillation tree: {treeStats.TotalNodes} nodes, " +
                         $"depth {treeStats.TreeDepth}, version {treeStats.CurrentVersion}" +
                         (treeStats.LastBuildTime.HasValue
                             ? $", last built {treeStats.LastBuildTime.Value:u}"
                             : ", not yet built")));
        }
    }

    // ── Job Processor Registration (typed, no runtime reflection) ──────

    [RequiresUnreferencedCode("RAG processor registration uses reflection to close generics at init time.")]
    [RequiresDynamicCode("RAG processor registration creates closed generic method at init time.")]
    private static void RegisterJobProcessor(
        Workers.RagJobProcessorRegistry registry,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
        Type entityType)
    {
        var method = typeof(Workers.RagJobProcessorRegistry)
            .GetMethod(nameof(Workers.RagJobProcessorRegistry.Register))
            ?.MakeGenericMethod(entityType)
            ?? throw new InvalidOperationException(
                $"RagJobProcessorRegistry.Register<> method not found.");

        method.Invoke(registry, []);
    }

    // ── Hook Registration (follows Koan.Data.AI pattern exactly) ────────

    [RequiresUnreferencedCode("RAG hooks use reflection against entity lifecycle APIs.")]
    [RequiresDynamicCode("RAG hooks create closed generic delegates at runtime.")]
    private static void RegisterIngestionHooks(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicMethods)]
        Type entityType)
    {
        var entityBaseType = FindEntityBaseType(entityType);
        if (entityBaseType is null)
            throw new InvalidOperationException(
                $"Type {entityType.Name} has [RagCorpus] but does not inherit from Entity<T>.");

        var genericArgs = entityBaseType.GetGenericArguments();
        var keyType = genericArgs.Length == 2 ? genericArgs[1] : typeof(string);
        if (keyType != typeof(string))
            throw new InvalidOperationException(
                $"Type {entityType.Name} has [RagCorpus] but uses {keyType.Name} keys. " +
                "RAG requires string keys (Entity<T> or Entity<T, string>).");

        // Get Entity<T>.Events static property
        var eventsProperty = entityBaseType.GetProperty("Events", BindingFlags.Static | BindingFlags.Public)
            ?? throw new InvalidOperationException(
                $"Entity base type for {entityType.Name} has no static Events property.");

        var eventsBuilder = eventsProperty.GetValue(null)
            ?? throw new InvalidOperationException(
                $"Events property for {entityType.Name} returned null.");

        // Build the handler delegate type: Func<EntityEventContext<TEntity>, ValueTask>
        var contextType = typeof(Koan.Data.Core.Events.EntityEventContext<>).MakeGenericType(entityType);
        var handlerType = typeof(Func<,>).MakeGenericType(contextType, typeof(ValueTask));

        // Wire AfterUpsert
        var afterUpsertMethod = eventsBuilder.GetType()
            .GetMethod("AfterUpsert", [handlerType])
            ?? throw new InvalidOperationException(
                $"AfterUpsert method not found on Events for {entityType.Name}.");

        var upsertHook = typeof(KoanRagAutoRegistrar)
            .GetMethod(nameof(IngestionHookAsync), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(entityType);

        afterUpsertMethod.Invoke(eventsBuilder,
            [Delegate.CreateDelegate(handlerType, upsertHook)]);

        // Wire AfterRemove
        var afterRemoveMethod = eventsBuilder.GetType()
            .GetMethod("AfterRemove", [handlerType]);

        if (afterRemoveMethod is not null)
        {
            var removeHook = typeof(KoanRagAutoRegistrar)
                .GetMethod(nameof(RemovalHookAsync), BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod(entityType);

            afterRemoveMethod.Invoke(eventsBuilder,
                [Delegate.CreateDelegate(handlerType, removeHook)]);
        }
    }

    /// <summary>
    /// AfterUpsert hook: queues the entity for RAG ingestion.
    /// Wired dynamically via reflection for each [RagCorpus] entity type.
    /// </summary>
    private static async ValueTask IngestionHookAsync<TEntity>(
        Koan.Data.Core.Events.EntityEventContext<TEntity> ctx)
        where TEntity : class, Koan.Data.Abstractions.IEntity<string>
    {
        var ragService = Koan.Core.Hosting.App.AppHost.Current
            ?.GetService(typeof(IRagService)) as IRagService;

        if (ragService is null) return;

        var metadata = RagCorpusMetadata.ResolveDefault<TEntity>();

        if (metadata.Async)
        {
            // Queue for background processing — don't block the entity save
            var signature = Koan.Data.AI.EmbeddingMetadata.Resolve<TEntity>()
                .ComputeSignature(ctx.Current);

            var jobId = Workers.RagIngestionJob.MakeId(
                typeof(TEntity).Name, metadata.Name, ctx.Current.Id);

            var job = new Workers.RagIngestionJob
            {
                Id = jobId,
                EntityId = ctx.Current.Id,
                EntityType = typeof(TEntity).Name,
                CorpusName = metadata.Name,
                ContentSignature = signature,
                Status = Abstractions.RagIngestionStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow
            };

            try { await job.Save(ctx.CancellationToken); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // Job store may not be configured yet — log and continue
                var log = Koan.Core.Hosting.App.AppHost.Current
                    ?.GetService(typeof(Microsoft.Extensions.Logging.ILoggerFactory))
                    as Microsoft.Extensions.Logging.ILoggerFactory;
                log?.CreateLogger("Koan.Rag")
                    .LogDebug(ex, "Failed to queue RAG ingestion job for {EntityId}", ctx.Current.Id);
            }
        }
        else
        {
            // Synchronous ingestion in the save path
            var corpus = ragService.GetCorpus<TEntity>();
            await corpus.Ingest(ctx.Current, ctx.CancellationToken);
        }
    }

    /// <summary>
    /// AfterRemove hook: removes the entity from the RAG corpus.
    /// </summary>
    private static async ValueTask RemovalHookAsync<TEntity>(
        Koan.Data.Core.Events.EntityEventContext<TEntity> ctx)
        where TEntity : class, Koan.Data.Abstractions.IEntity<string>
    {
        var ragService = Koan.Core.Hosting.App.AppHost.Current
            ?.GetService(typeof(IRagService)) as IRagService;

        if (ragService is null) return;

        var corpus = ragService.GetCorpus<TEntity>();
        await corpus.Remove(ctx.Current, ctx.CancellationToken);
    }

    // ── Assembly Discovery ──────────────────────────────────────────────

    private static IReadOnlyList<Type> DiscoverRagCorpusTypes()
    {
        var result = new List<Type>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic) continue;

            try
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (type.IsAbstract || type.IsInterface) continue;

                    if (type.GetCustomAttributes<RagCorpusAttribute>(inherit: false).Any())
                        result.Add(type);
                }
            }
            catch (ReflectionTypeLoadException)
            {
                // Skip assemblies that can't be fully loaded
            }
        }

        return result;
    }

    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicMethods)]
    private static Type? FindEntityBaseType(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicMethods)]
        Type type)
    {
        var currentType = type;
        while (currentType is not null)
        {
            if (currentType.IsGenericType)
            {
                var name = currentType.GetGenericTypeDefinition().Name;
                if (name is "Entity`1" or "Entity`2")
                    return currentType;
            }

            currentType = currentType.BaseType;
        }

        return null;
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "...";
}
