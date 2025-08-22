using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Annotations;
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
        // 1) Entity-level vector role
        string? desired = (Attribute.GetCustomAttribute(typeof(TEntity), typeof(VectorAdapterAttribute))
            as VectorAdapterAttribute)?.Provider;
        // 2) App defaults
        desired ??= sp.GetService<IOptions<VectorDefaultsOptions>>()?.Value?.DefaultProvider;
        // 3) Entity source provider (role-based)
        if (string.IsNullOrWhiteSpace(desired))
        {
            var src = (SourceAdapterAttribute?)Attribute.GetCustomAttribute(typeof(TEntity), typeof(SourceAdapterAttribute));
            if (src is not null && !string.IsNullOrWhiteSpace(src.Provider)) desired = src.Provider;
            else
            {
                var data = (DataAdapterAttribute?)Attribute.GetCustomAttribute(typeof(TEntity), typeof(DataAdapterAttribute));
                if (data is not null && !string.IsNullOrWhiteSpace(data.Provider)) desired = data.Provider;
            }
        }
        // 4) Default to highest-priority data provider name when none specified
        if (string.IsNullOrWhiteSpace(desired))
        {
            var dataFactories = sp.GetServices<IDataAdapterFactory>().ToList();
            if (dataFactories.Count > 0)
            {
                var ranked = dataFactories
                    .Select(f => new
                    {
                        Factory = f,
                        Priority = (f.GetType().GetCustomAttributes(typeof(ProviderPriorityAttribute), inherit: false).FirstOrDefault() as ProviderPriorityAttribute)?.Priority ?? 0,
                        Name = f.GetType().Name
                    })
                    .OrderByDescending(x => x.Priority)
                    .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var chosen = ranked.First().Factory.GetType().Name;
                const string suffix = "AdapterFactory";
                if (chosen.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) chosen = chosen[..^suffix.Length];
                desired = chosen.ToLowerInvariant();
            }
        }
        // Resolve vector factory
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

    public static Task<int> UpsertManyAsync(IEnumerable<(string Id, float[] Embedding, object? Metadata)> items, CancellationToken ct = default)
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
