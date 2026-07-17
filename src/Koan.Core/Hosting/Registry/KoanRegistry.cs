using System.Collections.Concurrent;
using System.ComponentModel;
using Koan.Core.Semantics;
using Koan.Core.Semantics.Contributions;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Core.Hosting.Registry;

/// <summary>
/// Central registry populated by source generators with compile-time manifests.
/// </summary>
public static partial class KoanRegistry
{
    private static readonly ConcurrentDictionary<Type, BackgroundServiceDescriptor> _backgroundServices = new(TypeEqualityComparer.Instance);
    private static readonly ConcurrentDictionary<Type, ServiceDiscoveryAdapterDescriptor> _serviceDiscoveryAdapters = new(TypeEqualityComparer.Instance);
    private static readonly ConcurrentDictionary<Type, SemanticComponentDescriptor> _semanticModules = new(TypeEqualityComparer.Instance);
    // Generic [KoanDiscoverable] discovery: implementer types keyed by the marked interface Type (ARCH-0086).
    private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<Type, byte>> _discoveredImplementors = new(TypeEqualityComparer.Instance);

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

    /// <summary>Generated-code ABI for construction-free semantic module descriptors.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void RegisterSemanticModule(
        string id,
        Type implementationType,
        Func<KoanModule> factory,
        SemanticContributionBinding[]? contributionBindings = null)
    {
        var descriptor = new SemanticComponentDescriptor(
            id,
            implementationType,
            factory,
            contributionBindings);

        if (_semanticModules.TryGetValue(implementationType, out var existing))
        {
            if (existing.Id != descriptor.Id)
            {
                throw new InvalidOperationException(
                    $"Koan semantic module '{implementationType.FullName}' was registered with both '{existing.Id}' and '{descriptor.Id}'. " +
                    "Keep one concrete KoanModule as the assembly's activation owner.");
            }

            return;
        }

        _semanticModules.TryAdd(implementationType, descriptor);
    }

    /// <summary>
    /// Returns background service descriptors registered for the current AppDomain.
    /// </summary>
    public static BackgroundServiceDescriptor[] GetBackgroundServices() => _backgroundServices.Values.ToArray();

    /// <summary>
    /// Returns service discovery adapter descriptors registered for the current AppDomain.
    /// </summary>
    public static ServiceDiscoveryAdapterDescriptor[] GetServiceDiscoveryAdapters() => _serviceDiscoveryAdapters.Values.ToArray();

    internal static SemanticComponentDescriptor[] GetSemanticModuleDescriptors() =>
        _semanticModules.Values
            .OrderBy(static descriptor => descriptor.Id)
            .ToArray();

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
        _backgroundServices.Clear();
        _serviceDiscoveryAdapters.Clear();
        _semanticModules.Clear();
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
