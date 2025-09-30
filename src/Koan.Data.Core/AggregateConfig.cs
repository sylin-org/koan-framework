using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;
using Koan.Data.Core.Schema;
using System.Collections.Concurrent;

namespace Koan.Data.Core;

public sealed class AggregateConfig<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    public string Provider { get; }
    public AggregateMetadata.IdSpec? Id { get; }

    private readonly Lazy<IDataRepository<TEntity, TKey>> _repo;
    public IDataRepository<TEntity, TKey> Repository => _repo.Value;

    // Optional per-entity cache for provider/dialect-specific rendered commands and binders
    private readonly ConcurrentDictionary<string, object> _bags = new();
    public TBag GetOrAddBag<TBag>(string key, Func<TBag> factory) where TBag : class
        => (TBag)_bags.GetOrAdd(key, _ => factory()!);
    internal IEnumerable<(string key, object value)> EnumerateBags()
        => _bags.Select(kvp => (kvp.Key, kvp.Value));

    /// <summary>
    /// Gets initialization state for this entity's provider (for testing/monitoring)
    /// </summary>
    public bool IsProviderInitialized()
    {
        var initializationKey = $"provider_init:{Provider}";
        if (_bags.TryGetValue(initializationKey, out var state) && state is InitializationState initState)
        {
            return initState.IsInitialized;
        }
        return false;
    }

    /// <summary>
    /// Gets when this entity's provider was initialized (for testing/monitoring)
    /// </summary>
    public DateTime? GetProviderInitializedAt()
    {
        var initializationKey = $"provider_init:{Provider}";
        if (_bags.TryGetValue(initializationKey, out var state) && state is InitializationState initState)
        {
            return initState.InitializedAt;
        }
        return null;
    }

    internal AggregateConfig(string provider, AggregateMetadata.IdSpec? id, IServiceProvider sp)
    {
        Provider = provider;
        Id = id;
        _repo = new Lazy<IDataRepository<TEntity, TKey>>(() =>
        {
            // Cache initialization state per entity type
            var initializationKey = $"provider_init:{provider}";
            var isInitialized = GetOrAddBag(initializationKey, () => new InitializationState());

            var factories = sp.GetServices<IDataAdapterFactory>();
            var factory = factories.FirstOrDefault(f => f.CanHandle(provider))
                          ?? throw new InvalidOperationException($"No data adapter factory for provider '{provider}'");

            var repo = factory.Create<TEntity, TKey>(sp);

            // Mark as initialized for this entity type
            isInitialized.MarkInitialized();

            var manager = sp.GetRequiredService<IAggregateIdentityManager>();
            var guard = sp.GetRequiredService<EntitySchemaGuard<TEntity, TKey>>();
            // Decorate with RepositoryFacade for cross-cutting concerns
            return new RepositoryFacade<TEntity, TKey>(repo, manager, guard);
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// Tracks initialization state for a provider/entity combination
    /// </summary>
    private sealed class InitializationState
    {
        private volatile bool _isInitialized;
        public DateTime? InitializedAt { get; private set; }

        public bool IsInitialized => _isInitialized;

        public void MarkInitialized()
        {
            if (!_isInitialized)
            {
                InitializedAt = DateTime.UtcNow;
                _isInitialized = true;
            }
        }
    }
}