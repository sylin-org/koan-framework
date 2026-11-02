using System.Reflection;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
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
        var refused = new List<string>();
        foreach (var root in services.GetRequiredService<DataApplicationManifest>().Roots)
        {
            if (root.RouteScope != DataEntityRouteScope.Default) continue;
            var ready = (Task<Exception?>)One.MakeGenericMethod(root.RootType, root.KeyType).Invoke(null, [services, ct])!;
            if (await ready.ConfigureAwait(false) is not { } failure) continue;
            refused.Add(root.RootType.Name);
            logger?.LogDebug(failure, "The table of {Entity} could not be made at start.", root.RootType.Name);
        }

        if (refused.Count > 0)
            logger?.LogWarning("These entities have no table yet and answer at their first use: {Entities}.", string.Join(", ", refused));
    }

    private static async Task<Exception?> Ready<TEntity, TKey>(IServiceProvider services, CancellationToken ct)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        try
        {
            // A route whose adapter cannot be resolved has nothing to make, and says so at first use.
            if (!Data<TEntity, TKey>.Capabilities.Has(DataCaps.Write.HeldTransaction)) return null;
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            return null;
        }

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
