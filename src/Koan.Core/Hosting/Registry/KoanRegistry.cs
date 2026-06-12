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
    // Generic [KoanDiscoverable] discovery: implementer types keyed by the marked interface Type (ARCH-0086).
    private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<Type, byte>> _discoveredImplementors = new(TypeEqualityComparer.Instance);

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
    /// Registers concrete implementers of a <c>[KoanDiscoverable]</c>-marked interface, keyed by that
    /// interface <see cref="Type"/>. Populated by the source generator (build-time) and
    /// <c>RegistryManifestLoader</c> (runtime fallback). Idempotent. See ARCH-0086.
    /// </summary>
    public static void RegisterDiscoveredImplementors(Type contract, IEnumerable<Type> implementers)
    {
        if (contract is null) return;
        var set = _discoveredImplementors.GetOrAdd(contract, static _ => new ConcurrentDictionary<Type, byte>(TypeEqualityComparer.Instance));
        foreach (var type in implementers)
        {
            if (type is null) continue;
            set.TryAdd(type, 0);
        }
    }

    /// <summary>
    /// Returns the discovered concrete implementers of a <c>[KoanDiscoverable]</c>-marked interface —
    /// the registry-backed replacement for bespoke <c>AppDomain</c> reflection scans (ARCH-0086).
    /// </summary>
    public static Type[] GetDiscoveredImplementors(Type contract)
        => contract is not null && _discoveredImplementors.TryGetValue(contract, out var set)
            ? set.Keys.ToArray()
            : Array.Empty<Type>();

    /// <summary>
    /// Clears all registered items; intended for testing only.
    /// </summary>
    internal static void ResetForTesting()
    {
        _initializerTypes.Clear();
        _autoRegistrarTypes.Clear();
        _backgroundServices.Clear();
        _serviceDiscoveryAdapters.Clear();
        _discoveredImplementors.Clear();
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
