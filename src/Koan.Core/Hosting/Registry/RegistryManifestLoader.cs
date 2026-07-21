using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Koan.Core.BackgroundServices;
using Koan.Core.Hosting.Registry;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Semantics;
using Koan.Core.Semantics.Contributions;
using Koan.Core.Infrastructure;

namespace Koan.Core.Hosting.Bootstrap;

/// <summary>
/// Runtime fallback that reflects loaded assemblies to populate the registry when source-generated
/// manifests are missing. Ensures modules and generated catalogs still light up when the compile-time
/// generator could not run. Core ships that generator transitively; this reflection path is a degraded
/// runtime fallback. Every assembly is scanned, so consumer-defined <see cref="KoanModule"/> types follow
/// the same reference-driven activation contract.
/// </summary>
/// <remarks>
/// Well-known framework prefixes are skipped via <see cref="ShouldSkipAssembly"/> as a cheap
/// fast-path; those assemblies (BCL, ASP.NET Core, System.*, etc.) are known not to carry Koan
/// interface implementations and excluding them keeps the per-startup scan tight without changing
/// observable behaviour. Per-type metadata access is guarded so an unloadable reference inside a
/// scanned assembly can't bring down the host bootstrap.
/// </remarks>
internal static class RegistryManifestLoader
{
    private static readonly Type ModuleType = typeof(KoanModule);
    private static readonly Type BackgroundServiceInterface = typeof(IKoanBackgroundService);
    private static readonly Type PokableInterface = typeof(IKoanPokableService);
    private static readonly Type PeriodicInterface = typeof(IKoanPeriodicService);
    private static readonly Type StartupInterface = typeof(IKoanStartupService);
    private static readonly Type HealthContributorInterface = typeof(IHealthContributor);
    private static readonly Type DiscoveryAdapterInterface = typeof(IServiceDiscoveryAdapter);

    /// <summary>
    /// Assembly-name prefixes that obviously cannot contain Koan interface implementations.
    /// Excluding them keeps the runtime scan tight without restricting where consumers can define
    /// their own modules. Comparison is case-sensitive — the BCL and Microsoft frameworks use
    /// stable PascalCase prefixes.
    /// </summary>
    private static readonly string[] SkipPrefixes =
    {
        "System.",
        "Microsoft.",
        "mscorlib",
        "netstandard",
        "WindowsBase",
        "PresentationFramework",
        "PresentationCore",
    };

    public static void PopulateFromAssembly(Assembly assembly)
    {
        if (assembly is null) return;
        if (ShouldSkipAssembly(assembly)) return;

        Type[] types;
        try
        {
#pragma warning disable IL2026
            types = assembly.GetTypes();
#pragma warning restore IL2026
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t is not null)!.Cast<Type>().ToArray();
        }
        catch
        {
            // Any other reflection failure on the assembly is fatal only to its scan — skip it.
            return;
        }

        if (types.Length == 0) return;

        var backgroundServices = new List<KoanRegistry.BackgroundServiceDescriptor>();
        var discoveryAdapters = new List<KoanRegistry.ServiceDiscoveryAdapterDescriptor>();
        // [KoanDiscoverable] implementers, accumulated per marked-interface contract (ARCH-0086).
        var discoverables = new Dictionary<Type, List<Type>>();

        var moduleIdentity = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(
                attribute.Key,
                Constants.Composition.ModuleIdentityMetadataName,
                StringComparison.Ordinal))
            ?.Value;
        if (string.IsNullOrWhiteSpace(moduleIdentity))
        {
            moduleIdentity = assembly.GetName().Name;
        }

        foreach (var type in types)
        {
            try
            {
                if (type is null || type.IsAbstract || type.IsGenericTypeDefinition) continue;

                var isSemanticModule = ModuleType.IsAssignableFrom(type);
                if (isSemanticModule && !string.IsNullOrWhiteSpace(moduleIdentity))
                {
                    KoanRegistry.RegisterSemanticModule(
                        moduleIdentity,
                        type,
                        () => (KoanModule)(Activator.CreateInstance(type)
                            ?? throw new InvalidOperationException($"Koan could not construct semantic module '{type.FullName}'.")),
                        BuildSemanticContributionBindings(type));
                }

                if (DiscoveryAdapterInterface.IsAssignableFrom(type))
                {
                    discoveryAdapters.Add(new KoanRegistry.ServiceDiscoveryAdapterDescriptor(type));
                }

                if (BackgroundServiceInterface.IsAssignableFrom(type))
                {
                    backgroundServices.Add(BuildBackgroundDescriptor(type));
                }

                foreach (var contract in type.GetInterfaces())
                {
                    if (contract.GetCustomAttribute<KoanDiscoverableAttribute>(inherit: false) is null) continue;
                    if (!discoverables.TryGetValue(contract, out var implementers))
                    {
                        implementers = new List<Type>();
                        discoverables[contract] = implementers;
                    }
                    implementers.Add(type);
                }
            }
            catch
            {
                // Defensive: a single type with unloadable references shouldn't kill the whole
                // assembly's scan. The interface checks above can trip TypeLoadException /
                // FileNotFoundException when a transitive dependency is missing — we silently
                // skip the offending type and keep going.
            }
        }

        if (backgroundServices.Count > 0)
        {
            KoanRegistry.RegisterBackgroundServices(backgroundServices);
        }

        if (discoveryAdapters.Count > 0)
        {
            KoanRegistry.RegisterServiceDiscoveryAdapters(discoveryAdapters);
        }

        foreach (var pair in discoverables)
        {
            KoanRegistry.RegisterDiscoveredImplementors(pair.Key, pair.Value);
        }
    }

    internal static SemanticContributionBinding[] BuildSemanticContributionBindings(Type moduleType)
    {
        ArgumentNullException.ThrowIfNull(moduleType);

        return moduleType.GetInterfaces()
            .Where(static contract =>
                contract.IsGenericType
                && contract.GetGenericTypeDefinition() == typeof(IContributeTo<>))
            .Select(static contract => contract.GetGenericArguments()[0])
            .Distinct()
            .OrderBy(static targetType => targetType.AssemblyQualifiedName, StringComparer.Ordinal)
            .Select(CreateSemanticContributionBinding)
            .ToArray();
    }

    private static SemanticContributionBinding CreateSemanticContributionBinding(Type targetType)
    {
        var factory = typeof(RegistryManifestLoader)
            .GetMethod(
                nameof(CreateSemanticContributionBindingCore),
                BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(targetType);
        return (SemanticContributionBinding)factory.Invoke(null, null)!;
    }

    private static SemanticContributionBinding CreateSemanticContributionBindingCore<TTarget>() =>
        new(
            typeof(TTarget),
            static (module, target) =>
                ((IContributeTo<TTarget>)module).Contribute((TTarget)target));

    private static KoanRegistry.BackgroundServiceDescriptor BuildBackgroundDescriptor(Type type)
    {
        var attr = type.GetCustomAttribute<KoanBackgroundServiceAttribute>();

        var enabled = attr?.Enabled ?? true;
        var configurationSection = attr?.ConfigurationSection;
        var lifetime = (int)(attr?.Lifetime ?? Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton);
        var priority = attr?.Priority ?? 100;
        var runDev = attr?.RunInDevelopment ?? true;
        var runProd = attr?.RunInProduction ?? true;
        var runTest = attr?.RunInTesting ?? false;

        var isPeriodic = PeriodicInterface.IsAssignableFrom(type);
        var isStartup = StartupInterface.IsAssignableFrom(type);
        var isPokable = PokableInterface.IsAssignableFrom(type);
        var isHealthContributor = HealthContributorInterface.IsAssignableFrom(type);

        return new KoanRegistry.BackgroundServiceDescriptor(
            type,
            enabled,
            configurationSection,
            (Microsoft.Extensions.DependencyInjection.ServiceLifetime)lifetime,
            priority,
            runDev,
            runProd,
            runTest,
            isPeriodic,
            isStartup,
            isPokable,
            isHealthContributor);
    }

    private static bool ShouldSkipAssembly(Assembly assembly)
    {
        var name = assembly.GetName().Name;
        if (string.IsNullOrEmpty(name)) return true;
        foreach (var prefix in SkipPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
