using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Data.AI.Attributes;
using Koan.Data.Core;  // For .Save() extension method

namespace Koan.Data.AI.Initialization;

/// <summary>
/// Auto-registers [Embedding] entities discovered via source-generated registries and wires up entity lifecycle hooks.
/// Automatically configures embedding generation on Save().
/// </summary>
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Data.AI";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
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
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);

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
    }

    /// <summary>
    /// Registers AfterUpsert hook for the specified entity type.
    /// Uses reflection to call Entity&lt;T&gt;.Events.AfterUpsert().
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

        // Get the static Events property
        var eventsProperty = entityBaseType.GetProperty("Events", BindingFlags.Static | BindingFlags.Public);
        if (eventsProperty == null)
        {
            throw new InvalidOperationException(
                $"Type {entityType.Name} does not have a static Events property. " +
                $"This should never happen for Entity<T> types.");
        }

        var eventsBuilder = eventsProperty.GetValue(null);
        if (eventsBuilder == null)
        {
            throw new InvalidOperationException(
                $"Events property for {entityType.Name} returned null.");
        }

        // Get the AfterUpsert method
        var afterUpsertMethod = eventsBuilder.GetType().GetMethod("AfterUpsert", new[] { typeof(Func<,>).MakeGenericType(
            typeof(Koan.Data.Core.Events.EntityEventContext<>).MakeGenericType(entityType),
            typeof(ValueTask)) });

        if (afterUpsertMethod == null)
        {
            throw new InvalidOperationException(
                $"AfterUpsert method not found on Events for {entityType.Name}.");
        }

        // Create the embedding hook handler
        var handlerType = typeof(Func<,>).MakeGenericType(
            typeof(Koan.Data.Core.Events.EntityEventContext<>).MakeGenericType(entityType),
            typeof(ValueTask));

        var hookMethod = typeof(KoanAutoRegistrar).GetMethod(nameof(EmbeddingHookAsync), BindingFlags.Static | BindingFlags.NonPublic);
        if (hookMethod == null)
        {
            throw new InvalidOperationException("EmbeddingHookAsync method not found.");
        }

        var genericHookMethod = hookMethod.MakeGenericMethod(entityType);
        var handler = Delegate.CreateDelegate(handlerType, genericHookMethod);

        // Register the hook: Entity<T>.Events.AfterUpsert(EmbeddingHookAsync)
        afterUpsertMethod.Invoke(eventsBuilder, new[] { handler });
    }

    /// <summary>
    /// Entity lifecycle hook that generates and stores embeddings after upsert.
    /// </summary>
    private static async ValueTask EmbeddingHookAsync<TEntity>(Koan.Data.Core.Events.EntityEventContext<TEntity> ctx)
        where TEntity : class, Koan.Data.Abstractions.IEntity<string>
    {
        var entity = ctx.Current;
        var metadata = EmbeddingMetadata.Resolve<TEntity>();

        // Compute current content signature
        var currentSignature = metadata.ComputeSignature(entity);

        // Load existing embedding state (if any)
        var stateId = EmbeddingState<TEntity>.MakeId(entity.Id);
        var state = await EmbeddingState<TEntity>.Get(stateId, ctx.CancellationToken);

        // Skip if signature unchanged (content hasn't changed)
        if (state != null && state.ContentSignature == currentSignature)
        {
            return;
        }

        if (metadata.Async)
        {
            // Queue for background processing (Phase 3)
            await QueueEmbeddingJobAsync(entity, metadata, currentSignature, ctx.CancellationToken);
            return;
        }

        // Synchronous embedding generation
        await GenerateAndStoreEmbedding(entity, metadata, currentSignature, ctx.CancellationToken);

        // Update or create embedding state
        if (state == null)
        {
            state = new EmbeddingState<TEntity>
            {
                Id = stateId,
                EntityId = entity.Id,
                ContentSignature = currentSignature,
                LastEmbeddedAt = DateTimeOffset.UtcNow,
                Model = metadata.Model
            };
        }
        else
        {
            state.ContentSignature = currentSignature;
            state.LastEmbeddedAt = DateTimeOffset.UtcNow;
            state.Model = metadata.Model;
        }

        await state.Save(ctx.CancellationToken);
    }

    private static async ValueTask QueueEmbeddingJobAsync<TEntity>(
        TEntity entity,
        EmbeddingMetadata metadata,
        string signature,
        CancellationToken ct)
        where TEntity : class, Koan.Data.Abstractions.IEntity<string>
    {
        // Build embedding text now (to capture current state)
        var text = metadata.BuildEmbeddingText(entity);

        // Check if job already exists for this entity
        var jobId = EmbedJob<TEntity>.MakeId(entity.Id);
        var existingJob = await EmbedJob<TEntity>.Get(jobId, ct);

        if (existingJob != null)
        {
            // Update existing job with new content if signature changed
            if (existingJob.ContentSignature != signature)
            {
                existingJob.ContentSignature = signature;
                existingJob.EmbeddingText = text;
                existingJob.Model = metadata.Model;

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
            EntityType = typeof(TEntity).Name,
            ContentSignature = signature,
            EmbeddingText = text,
            Status = EmbedJobStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            Model = metadata.Model,
            MaxRetries = 3, // TODO: Make configurable via EmbeddingWorkerOptions
            Priority = 0
        };

        await job.Save(ct);
    }

    /// <summary>
    /// Generates embedding and stores it in vector database.
    /// </summary>
    private static async ValueTask GenerateAndStoreEmbedding<TEntity>(
        TEntity entity,
        EmbeddingMetadata metadata,
        string signature,
        CancellationToken ct)
        where TEntity : class, Koan.Data.Abstractions.IEntity<string>
    {
        // Build embedding text
        var text = metadata.BuildEmbeddingText(entity);

        // Generate embedding with source routing
        float[] embedding;
        using (metadata.Source != null || metadata.Model != null
            ? Koan.AI.Client.Scope(all: metadata.Source)
            : null)
        {
            embedding = await Koan.AI.Client.Embed(text, ct);
        }

        // Store in vector database
        await Koan.Data.Vector.VectorData<TEntity>.SaveWithVector(entity, embedding, null, ct);
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
