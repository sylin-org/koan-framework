using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sora.Data.Abstractions;
using Sora.Data.Core;
using System.Collections.Concurrent;

namespace Sora.Data.Cqrs;

public interface ICqrsRouting
{
    string? GetProfileNameFor(Type entityType);
    IDataRepository<TEntity, TKey> GetReadRepository<TEntity, TKey>() where TEntity : class, IEntity<TKey> where TKey : notnull;
    IDataRepository<TEntity, TKey> GetWriteRepository<TEntity, TKey>() where TEntity : class, IEntity<TKey> where TKey : notnull;
}

internal sealed class CqrsRouting : ICqrsRouting
{
    private readonly IServiceProvider _sp;
    private readonly CqrsOptions _opts;
    private readonly List<IDataAdapterFactory> _factories;
    private readonly IConfiguration _config;
    private readonly ConcurrentDictionary<(Type ent, Type key, string profile, string role), object> _cache = new();

    public CqrsRouting(IServiceProvider sp, IOptions<CqrsOptions> options, IEnumerable<IDataAdapterFactory> factories, IConfiguration config)
    {
        _sp = sp; _opts = options.Value; _factories = factories.ToList(); _config = config;
    }

    public string? GetProfileNameFor(Type entityType)
    {
        var attr = (CqrsAttribute?)Attribute.GetCustomAttribute(entityType, typeof(CqrsAttribute));
        if (!string.IsNullOrWhiteSpace(attr?.Profile)) return attr!.Profile;
        return _opts.DefaultProfile;
    }

    public IDataRepository<TEntity, TKey> GetReadRepository<TEntity, TKey>() where TEntity : class, IEntity<TKey> where TKey : notnull
        => (IDataRepository<TEntity, TKey>)ResolveRepository(typeof(TEntity), typeof(TKey), role: "read");

    public IDataRepository<TEntity, TKey> GetWriteRepository<TEntity, TKey>() where TEntity : class, IEntity<TKey> where TKey : notnull
        => (IDataRepository<TEntity, TKey>)ResolveRepository(typeof(TEntity), typeof(TKey), role: "write");

    private object ResolveRepository(Type entityType, Type keyType, string role)
    {
        var profileName = GetProfileNameFor(entityType) ?? string.Empty;
        var cacheKey = (entityType, keyType, profileName, role);
        if (_cache.TryGetValue(cacheKey, out var cached)) return cached;

        // If profile is not found, fallback to DataService default
        var endpoint = TryGetEndpoint(entityType, role, profileName);
        if (endpoint is null)
        {
            var ds = _sp.GetRequiredService<IDataService>();
            var mi = typeof(IDataService).GetMethod(nameof(IDataService.GetRepository))!.MakeGenericMethod(entityType, keyType);
            var repo = mi.Invoke(ds, null)!;
            _cache[cacheKey] = repo;
            return repo;
        }

        // Build a repository instance using the chosen provider and connection
        var factory = _factories.FirstOrDefault(f => f.CanHandle(endpoint.Provider))
            ?? throw new InvalidOperationException($"No data adapter factory for provider '{endpoint.Provider}'.");

        // NOTE: Adapters currently bind their options from IConfiguration. For now we assume
        // the endpoint's ConnectionString{,Name} is already in configuration. We keep Create(sp).
        var resolvedRepo = CreateRepository(factory, entityType, keyType, _sp);
        _cache[cacheKey] = resolvedRepo;
        return resolvedRepo;
    }

    private static object CreateRepository(IDataAdapterFactory factory, Type entityType, Type keyType, IServiceProvider sp)
    {
        var mi = typeof(IDataAdapterFactory).GetMethod(nameof(IDataAdapterFactory.Create))!;
        var gm = mi.MakeGenericMethod(entityType, keyType);
        return gm.Invoke(factory, new object?[] { sp })!;
    }

    private CqrsEndpoint? TryGetEndpoint(Type entityType, string role, string? profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName)) return null;
        if (!_opts.Profiles.TryGetValue(profileName, out var profile)) return null;
        var name = entityType.Name; // default mapping by simple type name
        if (!profile.Entities.TryGetValue(name, out var route)) return null;
        return string.Equals(role, "read", StringComparison.OrdinalIgnoreCase) ? route.Read : route.Write;
    }
}
