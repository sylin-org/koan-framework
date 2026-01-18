using Microsoft.Extensions.DependencyInjection;
using Koan.Core;
using Koan.Core.Hosting.Registry;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace Koan.Core.Hosting.Bootstrap;

// Greenfield bootstrapper: wires up IKoanInitializer instances already registered or discoverable via DI.
public static class AppBootstrapper
{
    private static RegistrySummarySnapshot? _registrySummary;

    internal static RegistrySummarySnapshot? RegistrySummary => _registrySummary;

    public static void InitializeModules(IServiceCollection services)
    {
        KoanStartupTimeline.Mark(KoanStartupStage.BootstrapStart);

        // Build a closure of loaded + referenced assemblies and populate AssemblyCache
        var set = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        var cache = AssemblyCache.Instance;
        var verboseAssemblies = string.Equals(Environment.GetEnvironmentVariable("KOAN_VERBOSE_ASSEMBLIES"), "1", StringComparison.OrdinalIgnoreCase);
        var assemblyLog = new List<(Assembly Assembly, string LoadContext)>();
        var discoveredAssemblies = new List<Assembly>();

        bool AddAsm(Assembly a, bool isDiscovery = false)
        {
            var name = a.GetName().Name ?? string.Empty;
            if (!set.ContainsKey(name))
            {
                set[name] = a;
                cache.AddAssembly(a); // Cache for reuse by other components
                var alc = AssemblyLoadContext.GetLoadContext(a)?.Name ?? "<default>";
                assemblyLog.Add((a, alc));
                if (isDiscovery)
                {
                    discoveredAssemblies.Add(a);
                }
                InvokeManifestLoader(a);
                return true;
            }
            return false;
        }

        foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) AddAsm(a);
        if (Assembly.GetEntryAssembly() is { } entry) AddAsm(entry);
        if (Assembly.GetExecutingAssembly() is { } exec) AddAsm(exec);

        var changed = true; var guard = 0;
        while (changed && guard++ < 5)
        {
            changed = false;
            var current = set.Values.ToList();
            foreach (var asm in current)
            {
                AssemblyName[] refs;
                try { refs = asm.GetReferencedAssemblies(); } catch { continue; }
                foreach (var rn in refs)
                {
                    if (set.ContainsKey(rn.Name!)) continue;
                    try
                    {
                        var loaded = Assembly.Load(rn);
                        if (AddAsm(loaded))
                        {
                            changed = true;
                        }
                    }
                    catch { /* skip */ }
                }
            }
        }

        // Also load any Koan.*.dll assemblies from base directory
        try
        {
            var baseDir = AppContext.BaseDirectory;
            foreach (var file in Directory.GetFiles(baseDir, "Koan.*.dll"))
            {
                try
                {
                    var asmName = AssemblyName.GetAssemblyName(file);
                    if (!set.ContainsKey(asmName.Name!))
                    {
                        var asm = Assembly.Load(asmName);
                        AddAsm(asm, isDiscovery: true);
                    }
                }
                catch { /* ignore bad files */ }
            }
        }
        catch { /* ignore */ }

        EmitAssemblySummary(assemblyLog, discoveredAssemblies, verboseAssemblies);

        var initializerTypes = KoanRegistry.GetInitializerTypes();
        var autoRegistrarTypes = KoanRegistry.GetAutoRegistrarTypes();
        var backgroundServices = KoanRegistry.GetBackgroundServices();
        var serviceDiscoveryAdapters = KoanRegistry.GetServiceDiscoveryAdapters();

        var registrySummary = BuildRegistrySummary(initializerTypes, autoRegistrarTypes, backgroundServices, serviceDiscoveryAdapters);
        _registrySummary = registrySummary;

        // CORE-0003: Always run initializers for every ServiceCollection.
        // Initializers are responsible for their own idempotency (AppDomain-scoped guards for static state).
        // Source-generated registries track eligible types deterministically.
        foreach (var initializerType in initializerTypes)
        {
            try
            {
                if (initializerType.IsAbstract) continue;
                if (Activator.CreateInstance(initializerType) is IKoanInitializer init)
                {
                    init.Initialize(services);
                }
            }
            catch
            {
                // best-effort to avoid failing the host during bootstrap
            }
        }

        KoanStartupTimeline.Mark(KoanStartupStage.DataReady);
    }

    private static void EmitAssemblySummary(
        List<(Assembly Assembly, string LoadContext)> assemblyLog,
        List<Assembly> discoveredAssemblies,
        bool verboseAssemblies)
    {
        static string Classify(Assembly asm)
        {
            var name = asm.GetName().Name ?? string.Empty;
            if (name.StartsWith("Koan", StringComparison.OrdinalIgnoreCase)) return "koan";
            if (name.StartsWith("OpenTelemetry", StringComparison.OrdinalIgnoreCase)) return "telemetry";
            if (name.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Microsoft.Extensions", StringComparison.OrdinalIgnoreCase)) return "aspnet";
            if (name.StartsWith("System", StringComparison.OrdinalIgnoreCase) || name.Equals("netstandard", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)) return "coreclr";
            return "thirdParty";
        }

        var assemblies = assemblyLog.Select(entry => entry.Assembly).Distinct().ToList();
        var breakdown = assemblies
            .GroupBy(Classify)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.Count());

        var breakdownText = string.Join(" ", breakdown.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        Console.WriteLine($"ASSEMBLIES|loaded={assemblies.Count} {breakdownText}");

        if (discoveredAssemblies.Count > 0)
        {
            var delta = string.Join(", ", discoveredAssemblies.Select(a =>
            {
                var name = a.GetName();
                return $"{name.Name}({name.Version})";
            }));
            Console.WriteLine($"ASSEMBLIES|new={delta}");
        }

        var payload = new
        {
            @event = "assembly-scan",
            loaded = assemblies.Count,
            categories = breakdown,
            discovered = discoveredAssemblies.Select(a => a.GetName().Name ?? string.Empty).ToArray()
        };
        Console.WriteLine(JsonSerializer.Serialize(payload));

        if (verboseAssemblies)
        {
            foreach (var entry in assemblyLog.OrderBy(a => a.Assembly.GetName().Name, StringComparer.OrdinalIgnoreCase))
            {
                var name = entry.Assembly.GetName();
                Console.WriteLine($"ASSEMBLY|{name.Name} {name.Version} :: {entry.Assembly.Location} :: ALC={entry.LoadContext}");
            }
        }
    }

    private static RegistrySummarySnapshot BuildRegistrySummary(
        IReadOnlyCollection<Type> initializerTypes,
        IReadOnlyCollection<Type> autoRegistrarTypes,
        IReadOnlyCollection<KoanRegistry.BackgroundServiceDescriptor> backgroundServices,
        IReadOnlyCollection<KoanRegistry.ServiceDiscoveryAdapterDescriptor> serviceDiscoveryAdapters)
    {
        static string ClassifyNamespace(Type type)
        {
            var ns = type.Namespace;
            if (string.IsNullOrWhiteSpace(ns)) return "global";
            var parts = ns.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return string.Join('.', parts.Take(2));
            }
            return parts[0];
        }

        var initializerBreakdown = initializerTypes
            .GroupBy(ClassifyNamespace)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Namespace: g.Key, Count: g.Count()))
            .ToArray();

        var startupServices = backgroundServices.Count(b => b.IsStartup);
        var periodicServices = backgroundServices.Count(b => b.IsPeriodic);

        return new RegistrySummarySnapshot(
            initializerTypes.Count,
            initializerBreakdown,
            autoRegistrarTypes.Count,
            backgroundServices.Count,
            startupServices,
            periodicServices,
            serviceDiscoveryAdapters.Count);
    }

    private static Action<Assembly>? _manifestLoader;

    private static void InvokeManifestLoader(Assembly assembly)
    {
        _manifestLoader ??= CreateManifestInvoker();
        _manifestLoader?.Invoke(assembly);
    }

    private static Action<Assembly>? CreateManifestInvoker()
    {
        try
        {
            var loaderType = typeof(AppBootstrapper).Assembly.GetType("Koan.Core.Hosting.Bootstrap.RegistryManifestLoader");
            if (loaderType is null) return null;
            var method = loaderType.GetMethod("PopulateFromAssembly", BindingFlags.Public | BindingFlags.Static);
            if (method is null) return null;
            return asm =>
            {
                try { method.Invoke(null, new object?[] { asm }); }
                catch { /* swallow manifest reflection errors */ }
            };
        }
        catch
        {
            return null;
        }
    }
}

internal readonly record struct RegistrySummarySnapshot(
    int Initializers,
    IReadOnlyList<(string Namespace, int Count)> InitializerBreakdown,
    int AutoRegistrars,
    int BackgroundServices,
    int StartupBackgroundServices,
    int PeriodicBackgroundServices,
    int ServiceDiscoveryAdapters);
