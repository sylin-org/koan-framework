using System.Collections.Concurrent;
using System.Reflection;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Lexical;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Core.Lexical;

/// <summary>
/// Reaches the typed Data surface for an entity known only as a <see cref="Type"/>: the facts projector and the health
/// contributor enumerate discovered entity types, and the generic statics they need are closed once per type here.
/// Nothing on a hot path uses this.
/// </summary>
internal static class LexicalRoutes
{
    private static readonly ConcurrentDictionary<Type, Func<IServiceProvider, DataSourceRegistry, string>> Providers = new();
    private static readonly ConcurrentDictionary<Type, Func<IServiceProvider, CancellationToken, Task<LexicalIndexStatus?>>> Statuses = new();

    /// <summary>The provider the entity's route elects under the ambient context.</summary>
    public static string ResolveProvider(Type entityType, IServiceProvider services, DataSourceRegistry registry) =>
        Providers.GetOrAdd(entityType, static type =>
        {
            var method = typeof(LexicalRoutes)
                .GetMethod(nameof(ResolveProviderOf), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(type);
            return (Func<IServiceProvider, DataSourceRegistry, string>)Delegate.CreateDelegate(
                typeof(Func<IServiceProvider, DataSourceRegistry, string>), method);
        })(services, registry);

    /// <summary>
    /// The lexical status the entity's routed repository reports, or <see langword="null"/> when the route does not
    /// keep a lexical index.
    /// </summary>
    public static Task<LexicalIndexStatus?> Status(Type entityType, IServiceProvider services, CancellationToken ct) =>
        Statuses.GetOrAdd(entityType, static type =>
        {
            var key = KeyTypeOf(type);
            var method = typeof(LexicalRoutes)
                .GetMethod(nameof(StatusOf), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(type, key);
            return (Func<IServiceProvider, CancellationToken, Task<LexicalIndexStatus?>>)Delegate.CreateDelegate(
                typeof(Func<IServiceProvider, CancellationToken, Task<LexicalIndexStatus?>>), method);
        })(services, ct);

    private static string ResolveProviderOf<TEntity>(IServiceProvider services, DataSourceRegistry registry)
        where TEntity : class =>
        AdapterResolver.ResolveForEntity<TEntity>(services, registry).Adapter;

    private static async Task<LexicalIndexStatus?> StatusOf<TEntity, TKey>(IServiceProvider services, CancellationToken ct)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var repository = services.GetRequiredService<IDataService>().GetRepository<TEntity, TKey>();
        if (repository is not ILexicalSearchRepository<TEntity, TKey> lexical) return null;
        try
        {
            return await lexical.Status(ct).ConfigureAwait(false);
        }
        catch (NotSupportedException)
        {
            // The route cannot keep the index; the entity's first write already says so.
            return null;
        }
    }

    private static Type KeyTypeOf(Type entityType) =>
        entityType.GetInterfaces()
            .Single(static contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IEntity<>))
            .GetGenericArguments()[0];
}
