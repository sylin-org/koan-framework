using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;
using Koan.Data.Core.Configuration;
using System.Collections.Concurrent;

namespace Koan.Data.Core;

/// <summary>
/// Default <see cref="IDataService"/> implementation.
/// Uses multi-dimensional caching per (entity, key, adapter, source) combination.
/// </summary>
public sealed class DataService(IServiceProvider sp) : IDataService
{
    private readonly ConcurrentDictionary<CacheKey, object> _cache = new();

    private record CacheKey(
        Type EntityType,
        Type KeyType,
        string Adapter,
        string Source);

    /// <inheritdoc />
    public IDataRepository<TEntity, TKey> GetRepository<TEntity, TKey>()
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        EntityShapeGuard.EnsureOwnRoot(typeof(TEntity));

        var sourceRegistry = sp.GetRequiredService<DataSourceRegistry>();
        var (adapter, source) = AdapterResolver.ResolveForEntity<TEntity>(sp, sourceRegistry);

        var key = new CacheKey(typeof(TEntity), typeof(TKey), adapter, source);

        if (_cache.TryGetValue(key, out var existing))
            return (IDataRepository<TEntity, TKey>)existing;

        // Find factory for adapter
        var factories = sp.GetServices<IDataAdapterFactory>();
        var factory = factories.FirstOrDefault(f => f.CanHandle(adapter))
            ?? throw new InvalidOperationException($"No data adapter factory for provider '{adapter}'");

        // Create repository with source context
        var repo = factory.Create<TEntity, TKey>(sp, source);

        // Wrap with facade for the write-stamp pipeline (identity + [Timestamp]) and the generic pre-op guards.
        // Schema readiness is the adapter's responsibility now (IDataRepository.EnsureReady);
        // the facade calls it before every operation — no separate EntitySchemaGuard layer.
        var guards = sp.GetServices<Pipeline.IStorageGuard>().ToArray();
        var readContributors = sp.GetServices<Pipeline.IReadFilterContributor>().ToArray();
        var facade = new RepositoryFacade<TEntity, TKey>(repo, guards, readContributors);

        var decorated = ApplyDecorators(typeof(TEntity), typeof(TKey), facade, sp);

        _cache[key] = decorated;
        return decorated;
    }

    /// <inheritdoc />
    public Axes.IAxisScopeDiagnostics GetScopeDiagnostics<TEntity, TKey>()
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        // Mirror GetRepository's raw-adapter resolution but return the UNDECORATED facade (the diagnostic authority that
        // holds the raw adapter for the IQueryRepository check). Cheap + connection-free: capability description is
        // static. Not cached — Explain / the boot pre-flight call it rarely, never on a hot path.
        var sourceRegistry = sp.GetRequiredService<DataSourceRegistry>();
        var (adapter, source) = AdapterResolver.ResolveForEntity<TEntity>(sp, sourceRegistry);
        var factories = sp.GetServices<IDataAdapterFactory>();
        var factory = factories.FirstOrDefault(f => f.CanHandle(adapter))
            ?? throw new InvalidOperationException($"No data adapter factory for provider '{adapter}'");
        var repo = factory.Create<TEntity, TKey>(sp, source);
        var guards = sp.GetServices<Pipeline.IStorageGuard>().ToArray();
        var readContributors = sp.GetServices<Pipeline.IReadFilterContributor>().ToArray();
        return new RepositoryFacade<TEntity, TKey>(repo, guards, readContributors);
    }

    /// <inheritdoc />
    public Direct.IDirectSession Direct(string? source = null, string? adapter = null)
    {
        var svc = sp.GetService<Direct.IDirectDataService>()
            ?? throw new InvalidOperationException("IDirectDataService not registered. It is registered by default via AddKoanDataCore() (ARCH-0090 §1) — ensure Koan data core is initialized.");
        return svc.Direct(source, adapter);
    }

    private static IDataRepository<TEntity, TKey> ApplyDecorators<TEntity, TKey>(
        Type entityType,
        Type keyType,
        IDataRepository<TEntity, TKey> repository,
        IServiceProvider services)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var decorators = services.GetService<IEnumerable<Decorators.IDataRepositoryDecorator>>();
        if (decorators is null)
        {
            return repository;
        }

        object current = repository;

        foreach (var decorator in decorators)
        {
            var result = decorator.TryDecorate(entityType, keyType, current, services);
            if (result is not null)
            {
                current = result;
            }
        }

        return (IDataRepository<TEntity, TKey>)current;
    }

}
