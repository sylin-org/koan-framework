using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions;
using Koan.Data.Core.Routing;
using Koan.Data.Vector.Abstractions;

namespace Koan.Data.Vector;

internal sealed class VectorService(IServiceProvider services) : IVectorService, IDisposable, IAsyncDisposable
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(Type, Type, string), object> _cache = new();
    private int _disposed;

    public IVectorSearchRepository<TEntity, TKey>? TryGetRepository<TEntity, TKey>()
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var source = RoutedSource.Resolve<TEntity>().Source ?? "Default";
        var key = (typeof(TEntity), typeof(TKey), source);
        if (_cache.TryGetValue(key, out var existing))
            return (IVectorSearchRepository<TEntity, TKey>)existing;

        var providers = services.GetRequiredService<VectorProviderCatalog>();
        if (providers.Candidates.Count == 0) return null;

        var explicitAttribute = (VectorAdapterAttribute?)Attribute.GetCustomAttribute(
            typeof(TEntity),
            typeof(VectorAdapterAttribute));
        var explicitDefault = services.GetService<IOptions<VectorDefaultsOptions>>()?.Value?.DefaultProvider;
        var required = !string.IsNullOrWhiteSpace(explicitAttribute?.Provider)
            ? explicitAttribute.Provider
            : !string.IsNullOrWhiteSpace(explicitDefault)
                ? explicitDefault
                : null;

        IVectorAdapterFactory? factory;
        if (required is not null)
        {
            factory = providers.Find(required)
                ?? throw RequiredProviderUnavailable<TEntity>(required, providers);
        }
        else
        {
            var preferred = PreferredRecordProvider<TEntity>();
            if (string.IsNullOrWhiteSpace(preferred))
            {
                preferred = services.GetService<DataDefaultProviderPlan>()?.ProviderId;
            }

            factory = !string.IsNullOrWhiteSpace(preferred) ? providers.Find(preferred) : null;
            factory ??= providers.SelectAutomatic();
        }

        if (factory is null) return null;
        var inner = factory.Create<TEntity, TKey>(services, source);
        var repository = new ScopedVectorRepository<TEntity, TKey>(inner, services);
        var winner = (IVectorSearchRepository<TEntity, TKey>)_cache.GetOrAdd(key, repository);
        if (!ReferenceEquals(winner, repository))
            repository.Dispose();
        return winner;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        foreach (var repository in _cache.Values.Distinct(ReferenceEqualityComparer.Instance))
        {
            if (repository is IDisposable disposable)
                disposable.Dispose();
            else if (repository is IAsyncDisposable asyncDisposable)
                asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        _cache.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        foreach (var repository in _cache.Values.Distinct(ReferenceEqualityComparer.Instance))
        {
            if (repository is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            else if (repository is IDisposable disposable)
                disposable.Dispose();
        }

        _cache.Clear();
    }

    private static string? PreferredRecordProvider<TEntity>() where TEntity : class
    {
        var source = (SourceAdapterAttribute?)Attribute.GetCustomAttribute(
            typeof(TEntity),
            typeof(SourceAdapterAttribute));
        if (!string.IsNullOrWhiteSpace(source?.Provider)) return source.Provider;

        var data = (DataAdapterAttribute?)Attribute.GetCustomAttribute(
            typeof(TEntity),
            typeof(DataAdapterAttribute));
        return !string.IsNullOrWhiteSpace(data?.Provider) ? data.Provider : null;
    }

    private static InvalidOperationException RequiredProviderUnavailable<TEntity>(
        string requested,
        VectorProviderCatalog providers)
    {
        var choices = providers.Candidates.Count == 0
            ? "none"
            : string.Join(", ", providers.Candidates.Select(static candidate => candidate.Id));
        return new InvalidOperationException(
            $"Entity '{typeof(TEntity).Name}' requires vector provider '{requested}', but it is unavailable. " +
            $"Referenced vector providers: {choices}. Correct the VectorAdapter/default provider or reference the intended connector; Koan will not substitute an unrelated provider.");
    }
}
