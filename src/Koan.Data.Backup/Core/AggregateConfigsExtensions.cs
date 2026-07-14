using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Backup.Models;
using Koan.Core.Hosting.App;
using System.Threading.Tasks;

namespace Koan.Data.Backup.Core;

/// <summary>
/// Extensions to AggregateConfigs for entity discovery and backup functionality
/// </summary>
public static class AggregateConfigsExtensions
{
    private const string UnknownProvider = "unknown";

    /// <summary>
    /// Gets all registered entity types using the current ambient host when one is available.
    /// </summary>
    public static IEnumerable<EntityTypeInfo> GetAllRegisteredEntities()
    {
        var services = AppHost.Current;
        return services is not null
            ? GetAllRegisteredEntities(services)
            : AggregateConfigs.GetRegisteredTypes()
                .Select(key => new EntityTypeInfo
                {
                    EntityType = key.EntityType,
                    KeyType = key.KeyType,
                    Provider = UnknownProvider,
                    Assembly = key.EntityType.Assembly.FullName
                });
    }

    /// <summary>
    /// Gets all registered entity types and resolves their providers against the supplied host.
    /// </summary>
    public static IEnumerable<EntityTypeInfo> GetAllRegisteredEntities(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return AggregateConfigs.GetRegisteredTypes()
            .Select(key => new EntityTypeInfo
            {
                EntityType = key.EntityType,
                KeyType = key.KeyType,
                Provider = GetProviderForType(key.EntityType, key.KeyType, services),
                Assembly = key.EntityType.Assembly.FullName
            });
    }

    /// <summary>
    /// Pre-registers entities without accessing their repositories
    /// </summary>
    public static void PreRegisterEntity<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        _ = AggregateConfigs.Get<TEntity, TKey>(sp); // This populates the cache
    }

    /// <summary>
    /// Pre-registers an entity type using reflection
    /// </summary>
    public static void PreRegisterEntityByReflection(Type entityType, Type keyType, IServiceProvider sp)
    {
        var method = typeof(AggregateConfigs)
            .GetMethod(nameof(AggregateConfigs.Get))
            ?.MakeGenericMethod(entityType, keyType);

        method?.Invoke(null, new object[] { sp });
    }

    /// <summary>
    /// Gets a config for unknown entity types via reflection
    /// </summary>
    public static object GetConfigByReflection(Type entityType, Type keyType, IServiceProvider sp)
    {
        var method = typeof(AggregateConfigs)
            .GetMethod(nameof(AggregateConfigs.Get))
            ?.MakeGenericMethod(entityType, keyType);

        return method?.Invoke(null, new object[] { sp })
            ?? throw new InvalidOperationException($"Could not get config for {entityType.Name}");
    }

    /// <summary>
    /// Gets repository for unknown entity types via reflection
    /// </summary>
    public static object GetRepositoryByReflection(Type entityType, Type keyType, IServiceProvider sp)
    {
        var config = GetConfigByReflection(entityType, keyType, sp);
        var repositoryProperty = config.GetType().GetProperty("Repository");

        return repositoryProperty?.GetValue(config)
            ?? throw new InvalidOperationException($"Could not get repository for {entityType.Name}");
    }

    /// <summary>
    /// Gets the generic <see cref="Data{TEntity,TKey}"/> facade type for an entity.
    /// </summary>
    public static Type GetDataType(Type entityType, Type keyType)
    {
        return typeof(Data<,>).MakeGenericType(entityType, keyType);
    }

    /// <summary>
    /// Calls AllStream via reflection for unknown entity types
    /// </summary>
    public static async IAsyncEnumerable<object> GetAllStreamByReflection(Type entityType, Type keyType, int? batchSize = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var dataType = GetDataType(entityType, keyType);
        var allStreamMethod = dataType.GetMethod("AllStream", new[] { typeof(int?), typeof(CancellationToken) });

        if (allStreamMethod == null)
            throw new InvalidOperationException($"AllStream method not found for {entityType.Name}");

        var asyncEnumerable = allStreamMethod.Invoke(null, new object?[] { batchSize, ct });

        if (asyncEnumerable is null)
            throw new InvalidOperationException($"AllStream returned null for {entityType.Name}");

        // Get the async enumerator
        var getEnumeratorMethod = asyncEnumerable.GetType().GetMethod("GetAsyncEnumerator");
        var enumerator = getEnumeratorMethod?.Invoke(asyncEnumerable, new object?[] { ct });

        if (enumerator is null)
            throw new InvalidOperationException($"Could not get enumerator for {entityType.Name}");

        // Iterate through the async enumerable
        var moveNextMethod = enumerator.GetType().GetMethod("MoveNextAsync");
        var currentProperty = enumerator.GetType().GetProperty("Current");

        if (moveNextMethod == null || currentProperty == null)
            throw new InvalidOperationException($"Invalid enumerator for {entityType.Name}");

        try
        {
            while (true)
            {
                var moveNextResult = moveNextMethod.Invoke(enumerator, null);
                if (moveNextResult is not ValueTask<bool> moveNextTask)
                    throw new InvalidOperationException($"MoveNextAsync did not return ValueTask<bool> for {entityType.Name}");

                if (!await moveNextTask)
                    break;

                var current = currentProperty.GetValue(enumerator);
                if (current != null)
                    yield return current;
            }
        }
        finally
        {
            // Dispose the enumerator if it implements IAsyncDisposable
            switch (enumerator)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync();
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
    }

    /// <summary>
    /// Calls UpsertMany via reflection for unknown entity types
    /// </summary>
    public static async Task<int> UpsertManyByReflection(Type entityType, Type keyType, IEnumerable<object> entities, CancellationToken ct = default)
    {
        var dataType = GetDataType(entityType, keyType);
        var upsertManyMethod = dataType.GetMethod("UpsertManyAsync", new[] { typeof(IEnumerable<>).MakeGenericType(entityType), typeof(CancellationToken) });

        if (upsertManyMethod == null)
            throw new InvalidOperationException($"UpsertManyAsync method not found for {entityType.Name}");

        var invocationResult = upsertManyMethod.Invoke(null, new object?[] { entities, ct });

        switch (invocationResult)
        {
            case Task<int> typedTask:
                return await typedTask;
            case Task genericTask:
                await genericTask;
                return 0;
            case null:
                return 0;
            default:
                return 0;
        }
    }

    private static string GetProviderForType(Type entityType, Type keyType, IServiceProvider services)
    {
        var config = GetConfigByReflection(entityType, keyType, services);
        var providerProperty = config.GetType().GetProperty(nameof(EntityTypeInfo.Provider));
        return providerProperty?.GetValue(config) as string ?? UnknownProvider;
    }
}
