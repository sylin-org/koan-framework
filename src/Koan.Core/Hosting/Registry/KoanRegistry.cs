using System.Collections.Concurrent;
using Koan.Core.Ordering;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Core.Hosting.Registry;

/// <summary>
/// Central registry populated by source generators with compile-time manifests.
/// </summary>
public static partial class KoanRegistry
{
    private static readonly ConcurrentDictionary<Type, byte> _initializerTypes = new(TypeEqualityComparer.Instance);
    private static readonly ConcurrentDictionary<Type, byte> _autoRegistrarTypes = new(TypeEqualityComparer.Instance);
    private static readonly ConcurrentDictionary<Type, BackgroundServiceDescriptor> _backgroundServices = new(TypeEqualityComparer.Instance);
    private static readonly ConcurrentDictionary<Type, ServiceDiscoveryAdapterDescriptor> _serviceDiscoveryAdapters = new(TypeEqualityComparer.Instance);

    public static void RegisterInitializers(IEnumerable<Type> types)
    {
        foreach (var type in types)
        {
            if (type is null) continue;
            _initializerTypes.TryAdd(type, 0);
        }
    }

    public static void RegisterAutoRegistrars(IEnumerable<Type> types)
    {
        foreach (var type in types)
        {
            if (type is null) continue;
            _autoRegistrarTypes.TryAdd(type, 0);
        }
    }

    public static void RegisterBackgroundServices(IEnumerable<BackgroundServiceDescriptor> descriptors)
    {
        foreach (var descriptor in descriptors)
        {
            if (descriptor.ServiceType is null) continue;
            _backgroundServices.TryAdd(descriptor.ServiceType, descriptor);
        }
    }

    public static void RegisterServiceDiscoveryAdapters(IEnumerable<ServiceDiscoveryAdapterDescriptor> descriptors)
    {
        foreach (var descriptor in descriptors)
        {
            if (descriptor.ServiceType is null) continue;
            _serviceDiscoveryAdapters.TryAdd(descriptor.ServiceType, descriptor);
        }
    }

    /// <summary>
    /// Returns all initializer types registered for the current AppDomain,
    /// topologically sorted to satisfy any <see cref="BeforeAttribute"/> /
    /// <see cref="AfterAttribute"/> declarations and otherwise broken by a
    /// stable <c>AssemblyQualifiedName</c> tie-break. See CORE-0091.
    /// </summary>
    public static Type[] GetInitializerTypes() => RegistrarOrdering.Sort(_initializerTypes.Keys);

    /// <summary>
    /// Returns all auto-registrar types registered for the current AppDomain,
    /// sorted by the same rules as <see cref="GetInitializerTypes"/> so that
    /// provenance / boot-output ordering matches initialization ordering.
    /// </summary>
    public static Type[] GetAutoRegistrarTypes() => RegistrarOrdering.Sort(_autoRegistrarTypes.Keys);

    /// <summary>
    /// Returns background service descriptors registered for the current AppDomain.
    /// </summary>
    public static BackgroundServiceDescriptor[] GetBackgroundServices() => _backgroundServices.Values.ToArray();

    /// <summary>
    /// Returns service discovery adapter descriptors registered for the current AppDomain.
    /// </summary>
    public static ServiceDiscoveryAdapterDescriptor[] GetServiceDiscoveryAdapters() => _serviceDiscoveryAdapters.Values.ToArray();

    /// <summary>
    /// Clears all registered items; intended for testing only.
    /// </summary>
    internal static void ResetForTesting()
    {
        _initializerTypes.Clear();
        _autoRegistrarTypes.Clear();
        _backgroundServices.Clear();
        _serviceDiscoveryAdapters.Clear();
    }

    public readonly record struct BackgroundServiceDescriptor(
        Type ServiceType,
        bool Enabled,
        string? ConfigurationSection,
        ServiceLifetime Lifetime,
        int Priority,
        bool RunInDevelopment,
        bool RunInProduction,
        bool RunInTesting,
        bool IsPeriodic,
        bool IsStartup,
        bool IsPokable,
        bool ImplementsHealthContributor);

    public readonly record struct ServiceDiscoveryAdapterDescriptor(Type ServiceType);

    private sealed class TypeEqualityComparer : IEqualityComparer<Type>
    {
        public static TypeEqualityComparer Instance { get; } = new();
        public bool Equals(Type? x, Type? y) => x == y;
        public int GetHashCode(Type obj) => obj.GetHashCode();
    }
}
