using System.Reflection;
using Koan.Core.Hosting.Registry;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Transactions;
using Koan.Data.Core.Routing;
using Koan.Data.Core.Polymorphism;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Koan.Data.Core.Composition;

/// <summary>
/// Makes every discovered entity's table exist before the host reports started, when the default route offers holds (ARCH-0142).
/// On such a store every writer waits for one lock, so a table made at first use can stall a hold or be made inside one, and
/// the schema changes it makes under a running application are what a new connection's first use of an FTS5 table cannot
/// survive. An entity that names its own source or adapter stays at its first use, so a store nothing elected is not touched,
/// and so does one routed by a Database axis, which has no finite list of tables, and a partition's table. A store that refuses
/// here answers at first use as it would have, so a boot does not depend on it.
/// </summary>
internal static class TablesAtBoot
{
    private static readonly MethodInfo One =
        typeof(TablesAtBoot).GetMethod(nameof(Ready), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static async Task Make(IServiceProvider services, ILogger? logger, CancellationToken ct)
    {
        if (!DefaultRouteHolds(services)) return;
        var refused = new List<string>();
        foreach (var (root, key) in Roots().Distinct())
        {
            if (DataApplicationManifest.ScopeOf(root) != DataEntityRouteScope.Default) continue;
            var ready = (Task<Exception?>)One.MakeGenericMethod(root, key).Invoke(null, [services, ct])!;
            if (await ready.ConfigureAwait(false) is not { } failure) continue;
            refused.Add(root.Name);
            logger?.LogDebug(failure, "The table of {Entity} could not be made at start.", root.Name);
        }

        if (refused.Count > 0)
            logger?.LogWarning("These entities have no table yet and answer at their first use: {Entities}.", string.Join(", ", refused));
    }

    /// <summary>Every concrete entity's root. One declared wrongly says so at its first use, as it does without this.</summary>
    private static IEnumerable<(Type Root, Type Key)> Roots()
    {
        foreach (var type in KoanRegistry.GetDiscoveredImplementors(typeof(IEntity)))
        {
            if (!type.IsClass || type.IsAbstract || type.ContainsGenericParameters) continue;
            EntityRootDescriptor descriptor;
            try
            {
                descriptor = EntityRootDescriptor.For(type);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            yield return (descriptor.RootType, descriptor.KeyType);
        }
    }

    /// <summary>Asked of the provider, so a store that holds nothing has no repository made for it. One that cannot be resolved says so at first use.</summary>
    private static bool DefaultRouteHolds(IServiceProvider services)
    {
        try
        {
            return AdapterResolver.ResolveDefault(services).Binding is { } binding &&
                   services.GetRequiredService<DataProviderCatalog>().Find(binding.Plan.Adapter) is IHeldTransactionProvider;
        }
        catch (Exception failure) when (failure is AdapterResolutionException or InvalidOperationException)
        {
            return false;
        }
    }

    private static async Task<Exception?> Ready<TEntity, TKey>(IServiceProvider services, CancellationToken ct)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        try
        {
            await services.GetRequiredService<IDataService>().GetRepository<TEntity, TKey>().EnsureReady(ct).ConfigureAwait(false);
            return null;
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            return failure;
        }
    }
}
