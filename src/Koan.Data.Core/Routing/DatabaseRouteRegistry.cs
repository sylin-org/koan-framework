using Koan.Core.Composition;
using Koan.Core.Hosting.App;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Core.Routing;

/// <summary>Host-owned Database-axis routes behind Koan's terse composition facade.</summary>
public static class DatabaseRouteRegistry
{
    public static bool IsEmpty => Current()?.IsEmpty ?? true;

    public static void Register(DatabaseRouteDescriptor route)
    {
        ArgumentNullException.ThrowIfNull(route);
        Composition().Register(route);
    }

    public static string? ResolveSourceKey(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        return Current()?.ResolveSourceKey(entityType);
    }

    public static void Reset() => Current()?.Reset();

    private static DatabaseRouteCatalog Composition()
    {
        var services = KoanCompositionScope.RequireServices("Database-axis route registration");
        var catalog = services.Where(static descriptor => descriptor.ServiceType == typeof(DatabaseRouteCatalog))
            .Select(static descriptor => descriptor.ImplementationInstance).OfType<DatabaseRouteCatalog>().LastOrDefault();
        if (catalog is not null) return catalog;
        catalog = new DatabaseRouteCatalog();
        services.AddSingleton(catalog);
        return catalog;
    }

    private static DatabaseRouteCatalog? Current()
    {
        if (KoanCompositionScope.TryGetServices(out var services))
            return services.Where(static descriptor => descriptor.ServiceType == typeof(DatabaseRouteCatalog))
                .Select(static descriptor => descriptor.ImplementationInstance).OfType<DatabaseRouteCatalog>().LastOrDefault();
        return AppHost.Current?.GetService(typeof(DatabaseRouteCatalog)) as DatabaseRouteCatalog;
    }

    internal sealed class DatabaseRouteCatalog
    {
        private readonly object _gate = new();
        private volatile DatabaseRouteDescriptor[] _snapshot = [];
        public bool IsEmpty => _snapshot.Length == 0;

        public void Register(DatabaseRouteDescriptor route)
        {
            lock (_gate)
            {
                if (_snapshot.Any(item => string.Equals(item.AxisId, route.AxisId, StringComparison.Ordinal))) return;
                _snapshot = [.. _snapshot, route];
            }
        }

        public string? ResolveSourceKey(Type type)
        {
            foreach (var route in _snapshot)
            {
                if (!route.AppliesTo(type)) continue;
                var raw = route.SourceKeyProvider();
                var key = raw as string ?? raw?.ToString();
                if (!string.IsNullOrWhiteSpace(key)) return key;
            }
            return null;
        }

        public void Reset()
        {
            lock (_gate) _snapshot = [];
        }
    }
}
