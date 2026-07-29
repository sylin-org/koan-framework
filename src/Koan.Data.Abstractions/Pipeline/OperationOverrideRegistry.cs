using Koan.Core.Composition;
using Koan.Core.Hosting.App;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Abstractions.Pipeline;

/// <summary>Host-owned operation overrides behind Koan's terse composition facade.</summary>
public static class OperationOverrideRegistry
{
    public static bool IsEmpty => Current()?.IsEmpty ?? true;

    public static void Register(OperationOverrideDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        Composition().Register(descriptor);
    }

    public static OperationOverrideDescriptor? ForDelete(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        return Current()?.ForDelete(entityType);
    }

    public static void Reset() => Current()?.Reset();

    private static OperationOverrideCatalog Composition()
    {
        var services = KoanCompositionScope.RequireServices("Data operation-override registration");
        var catalog = services.Where(static descriptor => descriptor.ServiceType == typeof(OperationOverrideCatalog))
            .Select(static descriptor => descriptor.ImplementationInstance).OfType<OperationOverrideCatalog>().LastOrDefault();
        if (catalog is not null) return catalog;
        catalog = new OperationOverrideCatalog();
        services.AddSingleton(catalog);
        return catalog;
    }

    private static OperationOverrideCatalog? Current()
    {
        if (KoanCompositionScope.TryGetServices(out var services))
            return services.Where(static descriptor => descriptor.ServiceType == typeof(OperationOverrideCatalog))
                .Select(static descriptor => descriptor.ImplementationInstance).OfType<OperationOverrideCatalog>().LastOrDefault();
        return AppHost.Current?.GetService(typeof(OperationOverrideCatalog)) as OperationOverrideCatalog;
    }

    internal sealed class OperationOverrideCatalog
    {
        private readonly object _gate = new();
        private volatile OperationOverrideDescriptor[] _snapshot = [];
        public bool IsEmpty => _snapshot.Length == 0;

        public void Register(OperationOverrideDescriptor descriptor)
        {
            lock (_gate)
            {
                if (_snapshot.Any(item => string.Equals(item.Field, descriptor.Field, StringComparison.Ordinal))) return;
                _snapshot = [.. _snapshot, descriptor];
            }
        }

        public OperationOverrideDescriptor? ForDelete(Type type)
        {
            foreach (var descriptor in _snapshot)
                if (descriptor.AppliesTo(type)) return descriptor;
            return null;
        }

        public void Reset()
        {
            lock (_gate) _snapshot = [];
        }
    }
}
