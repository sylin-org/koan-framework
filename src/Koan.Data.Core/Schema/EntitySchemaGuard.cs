using Koan.Core.Infrastructure;
using Koan.Data.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Data.Core.Schema;

/// <summary>
/// Coordinates schema provisioning for an entity across adapters and logical sets.
/// </summary>
internal sealed class EntitySchemaGuard<TEntity, TKey>
    where TEntity : class, Koan.Data.Abstractions.IEntity<TKey>
    where TKey : notnull
{
    private static readonly ConcurrentDictionary<string, bool> _healthy = new(StringComparer.Ordinal);

    private readonly IServiceProvider _services;
    private readonly ILogger _logger;
    private ISchemaHealthContributor<TEntity, TKey>? _contributor;
    private bool _attempted;

    public EntitySchemaGuard(IServiceProvider services, ILogger<EntitySchemaGuard<TEntity, TKey>> logger)
    {
        _services = services;
        _logger = logger;
    }

    /// <summary>
    /// Ensures the entity's backing store is healthy for the current dataset.
    /// </summary>
    public async Task EnsureHealthyAsync(CancellationToken ct)
    {
        var contributor = GetContributor();
        if (contributor is null)
        {
            return;
        }

        var storageKey = BuildStorageKey();
        if (_healthy.TryGetValue(storageKey, out var healthy) && healthy)
        {
            return;
        }

        await Singleflight.RunAsync(storageKey, async token =>
        {
            if (_healthy.TryGetValue(storageKey, out var hot) && hot)
            {
                return;
            }

            try
            {
                await contributor.EnsureHealthyAsync(token).ConfigureAwait(false);
                _healthy[storageKey] = true;
                _logger.LogDebug("Schema guard ensured {Entity} storage {StorageKey}", typeof(TEntity).FullName, storageKey);
            }
            catch
            {
                _healthy.TryRemove(storageKey, out _);
                throw;
            }
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Invalidates the cached health state, forcing the next ensure attempt to run again.
    /// </summary>
    public void Invalidate()
    {
        var contributor = GetContributor();
        if (contributor is null)
        {
            return;
        }

        var storageKey = BuildStorageKey();
        _healthy.TryRemove(storageKey, out _);
        contributor.InvalidateHealth();
        Singleflight.Invalidate(storageKey);
        _logger.LogDebug("Schema guard invalidated {Entity} storage {StorageKey}", typeof(TEntity).FullName, storageKey);
    }

    private ISchemaHealthContributor<TEntity, TKey>? GetContributor()
    {
        if (_attempted)
        {
            return _contributor;
        }

        _attempted = true;
        _contributor = _services.GetService<ISchemaHealthContributor<TEntity, TKey>>();
        return _contributor;
    }

    private string BuildStorageKey()
    {
        var cfg = AggregateConfigs.Get<TEntity, TKey>(_services);
        var storage = StorageNameRegistry.GetOrCompute<TEntity, TKey>(_services);
        return $"{cfg.Provider}:{storage}";
    }
}
