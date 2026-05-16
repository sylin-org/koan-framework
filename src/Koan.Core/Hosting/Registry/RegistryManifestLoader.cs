using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Koan.Core.BackgroundServices;
using Koan.Core.Hosting.Registry;
using Koan.Core.Orchestration.Abstractions;

namespace Koan.Core.Hosting.Bootstrap;

/// <summary>
/// Runtime fallback that reflects loaded assemblies to populate the registry when source-generated
/// manifests are missing. Ensures adapters and initializers still light up even when the
/// compile-time generator didn't run (e.g. downstream consumer assemblies that pull Koan in via
/// NuGet, where the analyzer isn't shipped). Every assembly is scanned — Koan-named or not — so a
/// consumer can define its own <see cref="IKoanAutoRegistrar"/> and have it discovered without an
/// explicit <c>Initialize()</c> call.
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
    private static readonly Type InitializerInterface = typeof(IKoanInitializer);
    private static readonly Type AutoRegistrarInterface = typeof(IKoanAutoRegistrar);
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

        var initializers = new List<Type>();
        var autoRegistrars = new List<Type>();
        var backgroundServices = new List<KoanRegistry.BackgroundServiceDescriptor>();
        var discoveryAdapters = new List<KoanRegistry.ServiceDiscoveryAdapterDescriptor>();

        foreach (var type in types)
        {
            try
            {
                if (type is null || type.IsAbstract || type.IsGenericTypeDefinition) continue;

                if (InitializerInterface.IsAssignableFrom(type))
                {
                    initializers.Add(type);
                    if (AutoRegistrarInterface.IsAssignableFrom(type))
                    {
                        autoRegistrars.Add(type);
                    }
                }

                if (DiscoveryAdapterInterface.IsAssignableFrom(type))
                {
                    discoveryAdapters.Add(new KoanRegistry.ServiceDiscoveryAdapterDescriptor(type));
                }

                if (BackgroundServiceInterface.IsAssignableFrom(type))
                {
                    backgroundServices.Add(BuildBackgroundDescriptor(type));
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

        if (initializers.Count > 0)
        {
            KoanRegistry.RegisterInitializers(initializers);
        }

        if (autoRegistrars.Count > 0)
        {
            KoanRegistry.RegisterAutoRegistrars(autoRegistrars);
        }

        if (backgroundServices.Count > 0)
        {
            KoanRegistry.RegisterBackgroundServices(backgroundServices);
        }

        if (discoveryAdapters.Count > 0)
        {
            KoanRegistry.RegisterServiceDiscoveryAdapters(discoveryAdapters);
        }

    }

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