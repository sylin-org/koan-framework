using Koan.Core.Composition;
using Koan.Core.Hosting.App;
using Koan.Core.Naming;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Abstractions.Naming;

/// <summary>Host-owned storage-name particles behind Koan's terse composition facade.</summary>
public static class StorageNameParticleRegistry
{
    public static bool IsEmpty => Current()?.IsEmpty ?? true;

    public static void Register(IStorageNameParticleContributor contributor)
    {
        ArgumentNullException.ThrowIfNull(contributor);
        Composition().Register(contributor);
    }

    public static Particle[] Gather(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        return Current()?.Gather(entityType) ?? [];
    }

    public static void Reset() => Current()?.Reset();

    private static StorageNameParticleCatalog Composition()
    {
        var services = KoanCompositionScope.RequireServices("Storage-name particle registration");
        var catalog = services.Where(static descriptor => descriptor.ServiceType == typeof(StorageNameParticleCatalog))
            .Select(static descriptor => descriptor.ImplementationInstance).OfType<StorageNameParticleCatalog>().LastOrDefault();
        if (catalog is not null) return catalog;
        catalog = new StorageNameParticleCatalog();
        services.AddSingleton(catalog);
        return catalog;
    }

    private static StorageNameParticleCatalog? Current()
    {
        if (KoanCompositionScope.TryGetServices(out var services))
            return services.Where(static descriptor => descriptor.ServiceType == typeof(StorageNameParticleCatalog))
                .Select(static descriptor => descriptor.ImplementationInstance).OfType<StorageNameParticleCatalog>().LastOrDefault();
        return AppHost.Current?.GetService(typeof(StorageNameParticleCatalog)) as StorageNameParticleCatalog;
    }

    internal sealed class StorageNameParticleCatalog
    {
        private readonly object _gate = new();
        private volatile IStorageNameParticleContributor[] _snapshot = [];
        public bool IsEmpty => _snapshot.Length == 0;

        public void Register(IStorageNameParticleContributor contributor)
        {
            lock (_gate)
            {
                if (_snapshot.Any(item => string.Equals(item.Axis, contributor.Axis, StringComparison.Ordinal))) return;
                _snapshot = [.. _snapshot, contributor];
            }
        }

        public Particle[] Gather(Type type)
        {
            var snapshot = _snapshot;
            List<Particle>? particles = null;
            foreach (var contributor in snapshot)
                if (contributor.GetParticle(type) is { } particle) (particles ??= []).Add(particle);
            if (particles is null) return [];
            if (particles.Count > 1)
                particles.Sort(static (left, right) =>
                {
                    var order = left.Order.CompareTo(right.Order);
                    return order != 0 ? order : string.CompareOrdinal(left.Axis, right.Axis);
                });
            return particles.ToArray();
        }

        public void Reset()
        {
            lock (_gate) _snapshot = [];
        }
    }
}
