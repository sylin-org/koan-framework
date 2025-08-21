using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Sora.Data.Abstractions;
using Sora.Data.Vector.Abstractions;

namespace Sora.Data.Vector;

public sealed class VectorDefaultsOptions
{
    public string? DefaultProvider { get; set; }
}

public interface IVectorService
{
    IVectorSearchRepository<TEntity, TKey>? TryGetRepository<TEntity, TKey>() where TEntity : class, IEntity<TKey> where TKey : notnull;
}

internal sealed class VectorService(IServiceProvider sp) : IVectorService
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(Type, Type), object> _cache = new();

    public IVectorSearchRepository<TEntity, TKey>? TryGetRepository<TEntity, TKey>() where TEntity : class, IEntity<TKey> where TKey : notnull
    {
        var key = (typeof(TEntity), typeof(TKey));
        if (_cache.TryGetValue(key, out var existing)) return (IVectorSearchRepository<TEntity, TKey>?)existing;
        var factories = sp.GetServices<IVectorAdapterFactory>().ToList();
        if (factories.Count == 0) return null;
        string? desired = (Attribute.GetCustomAttribute(typeof(TEntity), typeof(Sora.Data.Vector.Abstractions.VectorAdapterAttribute))
            as Sora.Data.Vector.Abstractions.VectorAdapterAttribute)?.Provider;
        desired ??= sp.GetService<IOptions<VectorDefaultsOptions>>()?.Value?.DefaultProvider;
    // Fallback to the source provider name if accessible; otherwise skip to first factory.
        var factory = !string.IsNullOrWhiteSpace(desired) ? factories.FirstOrDefault(f => f.CanHandle(desired)) : null;
        factory ??= factories.FirstOrDefault();
        var repo = factory?.Create<TEntity, TKey>(sp);
        if (repo is not null) _cache[key] = repo;
        return repo;
    }
}

public static class VectorData<TEntity>
    where TEntity : class, IEntity<string>
{
    private static IVectorSearchRepository<TEntity, string> Repo
        => (Sora.Core.SoraApp.Current?.GetService<IVectorService>()?.TryGetRepository<TEntity, string>())
            ?? throw new InvalidOperationException("No vector adapter configured for this entity.");

    public static Task UpsertManyAsync(IEnumerable<(string Id, float[] Embedding, object? Metadata)> items, CancellationToken ct = default)
        => Repo.UpsertManyAsync(items, ct);

    public static Task<VectorQueryResult<string>> SearchAsync(VectorQueryOptions options, CancellationToken ct = default)
        => Repo.SearchAsync(options, ct);
}

public static class ServiceCollectionVectorExtensions
{
    public static IServiceCollection AddSoraDataVector(this IServiceCollection services)
    {
        services.TryAddSingleton<IVectorService, VectorService>();
        services.AddOptions<VectorDefaultsOptions>().BindConfiguration("Sora:Data:VectorDefaults");
        return services;
    }
}
