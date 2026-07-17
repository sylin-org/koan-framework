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
        var decision = AdapterResolver.ResolveDecisionForEntity<TEntity>(sp, sourceRegistry);
        var adapter = decision.Adapter;
        var source = decision.Source;

        var key = new CacheKey(typeof(TEntity), typeof(TKey), adapter, source);

        if (_cache.TryGetValue(key, out var existing))
            return (IDataRepository<TEntity, TKey>)existing;

        var factory = decision.Factory;

        // Selection is the activation boundary, including failed first use. From this point the
        // application depends on this route, so readiness must report it even when repository
        // construction or the first provider operation cannot connect.
        var diagnostics = sp.GetService<DataDiagnostics>();
        diagnostics?.ObserveParticipation(factory.Provider, source);

        // Create repository with source context
        var repo = factory.Create<TEntity, TKey>(sp, source);

        // Provider/module decorators sit inside the Data-owned facade. They may cache or specialize
        // physical access, but cannot bypass guards, isolation, transforms, or Lifecycle by returning
        // early. The facade is therefore the one unavoidable application-facing repository boundary.
        var decorated = ApplyDecorators(typeof(TEntity), typeof(TKey), repo, sp);

        // Wrap once with the Data-owned semantic boundary: guards, isolation, transforms, write stamps and Lifecycle.
        // Schema readiness is the adapter's responsibility now (IDataRepository.EnsureReady);
        // the facade calls it before every operation — no separate EntitySchemaGuard layer.
        var guards = sp.GetServices<Pipeline.IStorageGuard>().ToArray();
        var readContributors = sp.GetServices<Pipeline.IReadFilterContributor>().ToArray();
        var lifecycle = sp.GetService<Lifecycle.EntityLifecyclePlan<TEntity, TKey>>();
        var segmentation = sp.GetRequiredService<Semantics.DataSegmentationPlan>().For(typeof(TEntity));
        var facade = new RepositoryFacade<TEntity, TKey>(decorated, guards, readContributors, lifecycle, segmentation);

        // Repository construction is the activation boundary: inspection and route description remain pure, while
        // any runtime path that actually asks for a repository makes that provider/source visible to readiness.
        diagnostics?.Observe(new EntityConfigInfo(
            typeof(TEntity).FullName ?? typeof(TEntity).Name,
            typeof(TKey).FullName ?? typeof(TKey).Name,
            factory.Provider,
            AggregateMetadata.GetIdSpec(typeof(TEntity))?.Prop.Name));

        _cache[key] = facade;
        return facade;
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
        var decision = AdapterResolver.ResolveDecisionForEntity<TEntity>(sp, sourceRegistry);
        var source = decision.Source;
        var factory = decision.Factory;
        var repo = factory.Create<TEntity, TKey>(sp, source);
        var guards = sp.GetServices<Pipeline.IStorageGuard>().ToArray();
        var readContributors = sp.GetServices<Pipeline.IReadFilterContributor>().ToArray();
        var lifecycle = sp.GetService<Lifecycle.EntityLifecyclePlan<TEntity, TKey>>();
        var segmentation = sp.GetRequiredService<Semantics.DataSegmentationPlan>().For(typeof(TEntity));
        return new RepositoryFacade<TEntity, TKey>(repo, guards, readContributors, lifecycle, segmentation);
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
