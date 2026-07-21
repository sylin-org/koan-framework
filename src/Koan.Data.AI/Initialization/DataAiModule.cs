using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Context;
using Koan.Core.Hosting.App;
using Koan.Core.Hosting.Bootstrap;
using Koan.Data.AI.Attributes;
using Koan.Data.Core;  // For .Save() extension method

namespace Koan.Data.AI.Initialization;

/// <summary>
/// Auto-registers [Embedding] and [MediaAnalysis] entities discovered via source-generated registries
/// and assembly scanning, wiring up entity lifecycle hooks.
/// Automatically configures embedding generation and media analysis on Save().
/// </summary>
public sealed class DataAiModule : KoanModule
{
    private const string ContextCaptureOperation = "embedding job context capture";

    public override void Register(IServiceCollection services)
    {
        // Use source-generated registry populated at module initialization
        var embeddingTypes = EmbeddingRegistry.GetRegisteredTypes();

        // Register event hooks only for entity types with lifecycle enabled (attribute present)
        foreach (var entityType in embeddingTypes)
        {
            var metadata = EmbeddingMetadata.Resolve(entityType);
            if (metadata.LifecycleEnabled)
            {
                RegisterEmbeddingHooks(entityType);
            }
        }

        // Register EmbeddingWorker as a hosted service (background worker)
        services.AddHostedService<Workers.EmbeddingWorker>();

        // Register EmbeddingWorkerOptions configuration
        services.AddOptions<EmbeddingWorkerOptions>()
            .BindConfiguration(Infrastructure.ConfigurationConstants.Keys.EmbeddingWorker)
            .ValidateDataAnnotations();

        // Register telemetry (singleton for metric collection)
        services.AddSingleton<Telemetry.EmbeddingTelemetry>();

        // Register health check
        services.AddHealthChecks()
            .AddCheck<Health.EmbeddingHealthCheck>("embeddings", tags: new[] { "ai", "embeddings", "ready" });

        // Registry already populated by generators; nothing else to track here

        // ============================================================
        // [MediaAnalysis] support
        // ============================================================

        // Scan loaded assemblies for [MediaAnalysis] entities (no source generator yet)
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                foreach (var type in assembly.GetTypes())
                {
                    var attr = type.GetCustomAttribute<MediaAnalysisAttribute>();
                    if (attr != null)
                        MediaAnalysisRegistry.Register(type, attr.Async);
                }
            }
            catch { /* Ignore assemblies that can't be reflected */ }
        }

        // Discover and register [MediaAnalysis] entities
        var mediaTypes = MediaAnalysisRegistry.GetRegisteredTypes();
        foreach (var entityType in mediaTypes)
        {
            var metadata = MediaAnalysisMetadata.Resolve(entityType);
            if (metadata != null)
            {
                RegisterMediaAnalysisHooks(entityType);
            }
        }

        // Register MediaAnalysisWorker as a hosted service
        services.AddHostedService<Workers.MediaAnalysisWorker>();

        // Register MediaAnalysisOptions configuration
        services.AddOptions<Options.MediaAnalysisOptions>()
            .BindConfiguration(Infrastructure.ConfigurationConstants.Keys.MediaAnalysisWorker)
            .ValidateDataAnnotations();

        // Register media analysis health check
        services.AddHealthChecks()
            .AddCheck<Health.MediaAnalysisHealthCheck>("media-analysis", tags: new[] { "ai", "media", "ready" });
    }

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);

        var registeredTypes = EmbeddingRegistry.GetRegisteredTypes();
        var syncCount = registeredTypes.Count(t => !EmbeddingMetadata.Resolve(t).Async);
        var asyncCount = registeredTypes.Count(t => EmbeddingMetadata.Resolve(t).Async);

        module.AddNote($"Registered {registeredTypes.Count} auto-embedding entities ({syncCount} sync, {asyncCount} async)");

        foreach (var type in registeredTypes.OrderBy(t => t.Name))
        {
            var metadata = EmbeddingMetadata.Resolve(type);
            var mode = metadata.Template != null ? "Template" :
                       metadata.Properties.Length > 0 && metadata.Policy == EmbeddingPolicy.Explicit ? "Properties" :
                       metadata.Policy.ToString();

            module.AddNote($"  • {type.Name}: {mode}, {metadata.Properties.Length} properties" +
                          (metadata.Async ? " [async queue]" : ""));
        }

        // Media analysis entities
        var mediaTypes = MediaAnalysisRegistry.GetRegisteredTypes();
        if (mediaTypes.Any())
        {
            module.AddNote($"Registered {mediaTypes.Count} media-analysis entities");
            foreach (var type in mediaTypes.OrderBy(t => t.Name))
            {
                var meta = MediaAnalysisMetadata.Resolve(type);
                if (meta != null)
                    module.AddNote($"  • {type.Name}: modes={meta.Analysis}, version={meta.Version}" +
                                  (meta.Async ? " [async]" : " [sync]"));
            }
        }
    }

    /// <summary>
    /// Registers the typed host-owned lifecycle contribution for a discovered entity type.
    /// </summary>
    [RequiresUnreferencedCode("Embedding hooks use reflection against entity lifecyle APIs.")]
    [RequiresDynamicCode("Embedding hooks create closed generic delegates at runtime.")]
    private static void RegisterEmbeddingHooks([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicMethods)] Type entityType)
    {
        // Find the Entity<T> or Entity<T, TKey> base class
        var entityBaseType = FindEntityBaseType(entityType);
        if (entityBaseType == null)
        {
            throw new InvalidOperationException(
                $"Type {entityType.Name} has [Embedding] attribute but does not inherit from Entity<T> or Entity<T, TKey>. " +
                $"Only entity types can use automatic embedding generation.");
        }

        // Check if entity uses string keys (VectorData<T> requires IEntity<string>)
        var genericArgs = entityBaseType.GetGenericArguments();
        var keyType = genericArgs.Length == 2 ? genericArgs[1] : typeof(string);
        if (keyType != typeof(string))
        {
            throw new InvalidOperationException(
                $"Type {entityType.Name} has [Embedding] attribute but uses {keyType.Name} keys. " +
                $"Automatic embedding generation currently only supports string keys (Entity<T> or Entity<T, string>). " +
                $"Remove [Embedding] attribute or change to string-based keys.");
        }

        CloseRegistration(nameof(RegisterEmbeddingHook), entityType);
    }

    /// <summary>
    /// Registers AfterUpsert hook for media analysis on the specified entity type.
    /// Same reflection pattern as <see cref="RegisterEmbeddingHooks"/>.
    /// </summary>
    [RequiresUnreferencedCode("Media analysis hooks use reflection against entity lifecycle APIs.")]
    [RequiresDynamicCode("Media analysis hooks create closed generic delegates at runtime.")]
    private static void RegisterMediaAnalysisHooks([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicMethods)] Type entityType)
    {
        var entityBaseType = FindEntityBaseType(entityType);
        if (entityBaseType == null)
        {
            throw new InvalidOperationException(
                $"Type {entityType.Name} has [MediaAnalysis] attribute but does not inherit from Entity<T> or Entity<T, TKey>.");
        }

        var genericArgs = entityBaseType.GetGenericArguments();
        var keyType = genericArgs.Length == 2 ? genericArgs[1] : typeof(string);
        if (keyType != typeof(string))
        {
            throw new InvalidOperationException(
                $"Type {entityType.Name} has [MediaAnalysis] attribute but uses {keyType.Name} keys. " +
                $"Media analysis currently only supports string keys.");
        }

        CloseRegistration(nameof(RegisterMediaAnalysisHook), entityType);
    }

    private static void CloseRegistration(string methodName, Type entityType)
    {
        var method = typeof(DataAiModule).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Lifecycle registration method '{methodName}' was not found.");
        method.MakeGenericMethod(entityType).Invoke(null, null);
    }

    private static void RegisterEmbeddingHook<TEntity>()
        where TEntity : class, Koan.Data.Abstractions.IEntity<string>
        => Koan.Data.Core.Model.Entity<TEntity, string>.Lifecycle.AfterUpsert(EmbeddingHookAsync<TEntity>);

    private static void RegisterMediaAnalysisHook<TEntity>()
        where TEntity : class, Koan.Data.Abstractions.IEntity<string>
        => Koan.Data.Core.Model.Entity<TEntity, string>.Lifecycle.AfterUpsert(MediaAnalysisHookAsync<TEntity>);

    /// <summary>
    /// Entity lifecycle hook that triggers media analysis after upsert.
    /// Detects byte[] content changes, checks version, and queues or executes analysis.
    /// </summary>
    private static async ValueTask MediaAnalysisHookAsync<TEntity>(Koan.Data.Core.Lifecycle.EntityLifecycleContext<TEntity> ctx)
        where TEntity : class, Koan.Data.Abstractions.IEntity<string>
    {
        var entity = ctx.Current;
        var metadata = MediaAnalysisMetadata.Resolve<TEntity>();
        if (metadata == null) return;

        // Load or create state
        var stateId = MediaAnalysisState<TEntity>.MakeId(entity.Id);
        var state = await MediaAnalysisState<TEntity>.Get(stateId, ctx.CancellationToken);

        // Skip if version hasn't bumped and already analyzed
        if (state != null && state.AnalyzedVersion >= metadata.Version && state.Status == MediaAnalysisStatus.Completed)
            return;

        if (metadata.Async)
        {
            // Queue for background processing
            var newState = new MediaAnalysisState<TEntity>
            {
                Id = stateId,
                EntityId = entity.Id,
                Status = MediaAnalysisStatus.Queued,
                AnalyzedVersion = 0, // Will be set on completion
                AttemptCount = state?.AttemptCount ?? 0,
            };
            await newState.Save(ctx.CancellationToken);
            return;
        }

        // Synchronous processing — extract bytes, run analysis, save
        var bytes = EntityAi.ExtractBytes<TEntity>(entity);
        if (bytes == null || bytes.Length == 0) return;

        var results = await Workers.MediaAnalysisExecutor.Execute(entity, metadata, bytes, ctx.CancellationToken);

        // Save entity with analysis results written by executor
        await entity.Save(ctx.CancellationToken);

        // Update state with completion info
        var allCompleted = results.Values.All(m => m.Completed);
        var anyCompleted = results.Values.Any(m => m.Completed);
        var overallStatus = allCompleted
            ? MediaAnalysisStatus.Completed
            : anyCompleted
                ? MediaAnalysisStatus.PartiallyCompleted
                : MediaAnalysisStatus.Failed;

        var completedState = new MediaAnalysisState<TEntity>
        {
            Id = stateId,
            EntityId = entity.Id,
            Status = overallStatus,
            AnalyzedVersion = metadata.Version,
            AttemptCount = (state?.AttemptCount ?? 0) + 1,
            LastAttemptAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            ModeStatuses = results,
        };
        await completedState.Save(ctx.CancellationToken);
    }

    /// <summary>
    /// Entity lifecycle hook that generates and stores embeddings after upsert.
    /// </summary>
    private static async ValueTask EmbeddingHookAsync<TEntity>(Koan.Data.Core.Lifecycle.EntityLifecycleContext<TEntity> ctx)
        where TEntity : class, Koan.Data.Abstractions.IEntity<string>
    {
        var entity = ctx.Current;
        var metadata = EmbeddingMetadata.Resolve<TEntity>();

        var content = EmbeddingWriter.Describe(entity, metadata);

        // Load existing embedding state (if any)
        var stateId = EmbeddingState<TEntity>.MakeId(entity.Id);
        var state = await EmbeddingState<TEntity>.Get(stateId, ctx.CancellationToken);

        // Skip if signature unchanged (content hasn't changed)
        if (state != null && state.ContentSignature == content.Signature)
        {
            return;
        }

        if (metadata.Async)
        {
            // Queue for background processing (Phase 3)
            await QueueEmbeddingJobAsync(entity, content.Signature, ctx.CancellationToken);
            return;
        }

        await EmbeddingWriter.Write(
            entity,
            metadata,
            content,
            ct: ctx.CancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask QueueEmbeddingJobAsync<TEntity>(
        TEntity entity,
        string signature,
        CancellationToken ct)
        where TEntity : class, Koan.Data.Abstractions.IEntity<string>
    {
        // Snapshot the Koan context (tenant + access subject) at enqueue. This hook runs inside the caller's Save,
        // so the captured context is exactly the scope the entity lives in. The global worker has no context of its
        // own; resolving the required registry through AppHost makes an incomplete composition fail loudly here.
        var registry = AppHost.GetRequiredService<KoanContextCarrierRegistry>(ContextCaptureOperation);
        var ambient = registry.Capture();
        var carrierBag = ambient is null ? null : new Dictionary<string, string>(ambient);

        // Check if job already exists for this entity
        var jobId = EmbedJob<TEntity>.MakeId(entity.Id, carrierBag);
        var existingJob = await EmbedJob<TEntity>.Get(jobId, ct);

        if (existingJob != null)
        {
            // Update existing job with new content if signature changed
            if (existingJob.ContentSignature != signature)
            {
                existingJob.ContentSignature = signature;
                existingJob.AmbientCarrier = carrierBag;   // re-capture: the enqueuing ambient is the authoritative one

                // Reset retry count if content changed
                existingJob.RetryCount = 0;
                existingJob.Error = null;

                // If job was completed/failed, reset to pending
                if (existingJob.Status != EmbedJobStatus.Pending &&
                    existingJob.Status != EmbedJobStatus.Processing)
                {
                    existingJob.Status = EmbedJobStatus.Pending;
                    existingJob.StartedAt = null;
                    existingJob.CompletedAt = null;
                }

                await existingJob.Save(ct);
            }
            // If signature unchanged and job is pending/processing, do nothing
            return;
        }

        // Create new job
        var job = new EmbedJob<TEntity>
        {
            Id = jobId,
            EntityId = entity.Id,
            ContentSignature = signature,
            Status = EmbedJobStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            AmbientCarrier = carrierBag
        };

        await job.Save(ct);
    }

    /// <summary>
    /// Finds the Entity&lt;T&gt; or Entity&lt;T, TKey&gt; base type for the given type.
    /// </summary>
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicMethods)]
    private static Type? FindEntityBaseType([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicMethods)] Type type)
    {
        var currentType = type;
        while (currentType != null)
        {
            if (currentType.IsGenericType)
            {
                var genericDef = currentType.GetGenericTypeDefinition();
                var name = genericDef.Name;

                // Check for Entity<T> or Entity<T, TKey>
                if (name == "Entity`1" || name == "Entity`2")
                {
                    return currentType;
                }
            }

            currentType = currentType.BaseType;
        }

        return null;
    }

}
