using Koan.Core.Composition;
using Koan.Core.Hosting.App;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Core.Pipeline;

/// <summary>Host-owned write-stamp contributors behind Koan's terse composition facade.</summary>
public static class StorageWriteContributorRegistry
{
    public static bool IsEmpty => Current()?.IsEmpty ?? true;
    public static IReadOnlyList<WriteStampContributor> All => Current()?.All ?? [];

    public static void Register(WriteStampContributor contributor)
    {
        ArgumentNullException.ThrowIfNull(contributor);
        if (string.IsNullOrWhiteSpace(contributor.Id))
            throw new ArgumentException("A write-stamp contributor must have a non-empty Id.", nameof(contributor));
        Composition().Register(contributor);
    }

    public static void Reset()
    {
        Current()?.Reset();
        (AppHost.Current?.GetService(typeof(StorageWritePlanCache)) as StorageWritePlanCache)?.Invalidate();
    }

    private static StorageWriteContributorCatalog Composition()
    {
        var services = KoanCompositionScope.RequireServices("Storage write-contributor registration");
        var catalog = services.Where(static descriptor => descriptor.ServiceType == typeof(StorageWriteContributorCatalog))
            .Select(static descriptor => descriptor.ImplementationInstance).OfType<StorageWriteContributorCatalog>().LastOrDefault();
        if (catalog is not null) return catalog;
        catalog = new StorageWriteContributorCatalog();
        services.AddSingleton(catalog);
        return catalog;
    }

    private static StorageWriteContributorCatalog? Current()
    {
        if (KoanCompositionScope.TryGetServices(out var services))
            return services.Where(static descriptor => descriptor.ServiceType == typeof(StorageWriteContributorCatalog))
                .Select(static descriptor => descriptor.ImplementationInstance).OfType<StorageWriteContributorCatalog>().LastOrDefault();
        return AppHost.Current?.GetService(typeof(StorageWriteContributorCatalog)) as StorageWriteContributorCatalog;
    }

    internal sealed class StorageWriteContributorCatalog
    {
        private readonly object _gate = new();
        private volatile WriteStampContributor[] _snapshot = [];
        public bool IsEmpty => _snapshot.Length == 0;
        public IReadOnlyList<WriteStampContributor> All => _snapshot;

        public void Register(WriteStampContributor contributor)
        {
            lock (_gate)
            {
                if (_snapshot.Any(item => string.Equals(item.Id, contributor.Id, StringComparison.Ordinal))) return;
                _snapshot = [.. _snapshot, contributor];
            }
        }

        public void Reset()
        {
            lock (_gate) _snapshot = [];
        }
    }
}
