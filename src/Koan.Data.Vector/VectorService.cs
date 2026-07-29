using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core;
using Koan.Data.Core.Routing;
using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Data.Vector;

internal sealed class VectorService : IVectorService, IVectorRuntime, IDisposable, IAsyncDisposable
{
    private readonly IServiceProvider _services;
    private readonly IVectorAdapterParticipation _participation;
    private readonly VectorSpaceDeclarationCatalog _spaces;
    private readonly DataSourceRegistry _sources;
    private readonly VectorMetadataMaterializer _metadata;
    private readonly int _capacity;
    private readonly object _gate = new();
    private readonly Dictionary<CacheKey, Lazy<object>> _repositories = new();
    private int _disposed;

    public VectorService(
        IServiceProvider services,
        IVectorAdapterParticipation participation,
        VectorSpaceDeclarationCatalog spaces,
        DataSourceRegistry sources,
        VectorMetadataMaterializer metadata,
        IOptions<VectorDefaultsOptions> options)
    {
        _services = services;
        _participation = participation;
        _spaces = spaces;
        _sources = sources;
        _metadata = metadata;
        ArgumentNullException.ThrowIfNull(options);
        _capacity = options.Value.RepositoryEntries > 0
            ? options.Value.RepositoryEntries
            : throw new InvalidOperationException("VectorDefaults:RepositoryEntries must be greater than zero.");
    }

    public IVectorSearchRepository<TEntity, TKey>? TryGetRepository<TEntity, TKey>()
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        try
        {
            return Resolve<TEntity, TKey>(DataOperationEffect.Read, "vector repository resolution").Repository;
        }
        catch (InvalidOperationException error) when (
            error.Message.StartsWith("No vector adapter", StringComparison.Ordinal))
        {
            return null;
        }
    }

    public VectorExecution<TEntity, TKey> Resolve<TEntity, TKey>(DataOperationEffect effect, string operation)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        var routed = RoutedSource.Resolve<TEntity>().Source;
        var space = _spaces.Resolve(typeof(TEntity), routed);
        var factory = SelectFactory<TEntity>(space.Source);
        var source = _sources.GetPlan(space.Source, factory.Provider);
        source.Demand(effect, operation);

        var key = new CacheKey(
            typeof(TEntity),
            typeof(TKey),
            factory.Provider,
            space.Source,
            space.Name,
            space.Dimensions,
            space.Metric,
            space.Visibility,
            space.Model);
        Lazy<object> lazy;
        lock (_gate)
        {
            if (!_repositories.TryGetValue(key, out lazy!))
            {
                if (_repositories.Count >= _capacity)
                    throw new InvalidOperationException(
                        $"The host Vector repository cache reached its configured limit of {_capacity}. " +
                        "Reduce declared source/entity spaces or increase VectorDefaults:RepositoryEntries.");
                lazy = new Lazy<object>(
                    () => Create<TEntity, TKey>(factory, space, source),
                    LazyThreadSafetyMode.ExecutionAndPublication);
                _repositories.Add(key, lazy);
            }
        }

        try
        {
            return new VectorExecution<TEntity, TKey>(
                space,
                source,
                (IVectorSearchRepository<TEntity, TKey>)lazy.Value,
                _metadata);
        }
        catch
        {
            lock (_gate)
                if (_repositories.TryGetValue(key, out var current) && ReferenceEquals(current, lazy))
                    _repositories.Remove(key);
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (var repository in CreatedRepositories())
            if (repository is IDisposable disposable) disposable.Dispose();
            else if (repository is IAsyncDisposable asyncDisposable)
                asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
        lock (_gate) _repositories.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (var repository in CreatedRepositories())
            if (repository is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            else if (repository is IDisposable disposable) disposable.Dispose();
        lock (_gate) _repositories.Clear();
    }

    private object Create<TEntity, TKey>(
        IVectorAdapterFactory factory,
        VectorSpacePlan space,
        DataSourcePlan source)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        _participation.Observe(factory.Provider, space.Source);
        var inner = factory.Create<TEntity, TKey>(_services, space);
        return new ScopedVectorRepository<TEntity, TKey>(inner, _services);
    }

    private IVectorAdapterFactory SelectFactory<TEntity>(string source)
        where TEntity : class
    {
        var providers = _services.GetRequiredService<VectorProviderCatalog>();
        if (providers.Candidates.Count == 0)
            throw new InvalidOperationException(
                $"No vector adapter is available for '{typeof(TEntity).Name}'. Reference one Vector connector.");

        var explicitAttribute = (VectorAdapterAttribute?)Attribute.GetCustomAttribute(
            typeof(TEntity), typeof(VectorAdapterAttribute));
        var explicitDefault = _services.GetService<IOptions<VectorDefaultsOptions>>()?.Value.DefaultProvider;
        var sourceAdapter = _sources.GetSource(source)?.Adapter;
        var required = !string.IsNullOrWhiteSpace(explicitAttribute?.Provider)
            ? explicitAttribute.Provider
            : !string.IsNullOrWhiteSpace(explicitDefault)
                ? explicitDefault
                : !string.IsNullOrWhiteSpace(sourceAdapter) && providers.Find(sourceAdapter) is not null
                    ? sourceAdapter
                    : null;

        if (required is not null)
            return providers.Find(required) ?? throw RequiredProviderUnavailable<TEntity>(required, providers);

        var preferred = PreferredRecordProvider<TEntity>();
        var selected = !string.IsNullOrWhiteSpace(preferred) ? providers.Find(preferred) : null;
        selected ??= providers.SelectAutomatic();
        return selected ?? throw new InvalidOperationException(
            $"No vector adapter could be elected for '{typeof(TEntity).Name}'. " +
            "Reference one connector or configure VectorDefaults:DefaultProvider.");
    }

    private object[] CreatedRepositories()
    {
        lock (_gate)
            return _repositories.Values
                .Where(static value => value.IsValueCreated)
                .Select(static value => value.Value)
                .Distinct(ReferenceEqualityComparer.Instance)
                .ToArray();
    }

    private static string? PreferredRecordProvider<TEntity>() where TEntity : class
    {
        var source = (SourceAdapterAttribute?)Attribute.GetCustomAttribute(
            typeof(TEntity), typeof(SourceAdapterAttribute));
        if (!string.IsNullOrWhiteSpace(source?.Provider)) return source.Provider;
        var data = (DataAdapterAttribute?)Attribute.GetCustomAttribute(
            typeof(TEntity), typeof(DataAdapterAttribute));
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

    private readonly record struct CacheKey(
        Type Entity,
        Type Key,
        string Provider,
        string Source,
        string Space,
        int Dimensions,
        VectorMetric Metric,
        VectorVisibility Visibility,
        string? Model);
}
