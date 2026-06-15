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
    /// <summary>
    /// Degraded-boot opt-out (fail-fast.json). When <c>KOAN_BOOT_LENIENT=1</c>, a broken module no
    /// longer crashes the host — it is still written to <see cref="Console.Error"/> and recorded in
    /// the registry summary's MODULES-FAILED channel, but boot continues. Read here (env var) because
    /// <c>InitializeModules</c> runs on <c>IServiceCollection</c> before any <c>IConfiguration</c>
    /// exists — the same constraint that gates <c>KOAN_VERBOSE_ASSEMBLIES</c>.
    /// </summary>
    private const string LenientBootEnvVar = "KOAN_BOOT_LENIENT";

    private static RegistrySummarySnapshot? _registrySummary;

    // Boot-time module failures (TIER B) accumulated across the manifest-invoker swallow and the
    // initializer loop, surfaced via RegistrySummarySnapshot → boot report MODULES-FAILED block.
    [ThreadStatic]
    private static List<ModuleFailure>? _bootFailures;

    internal static RegistrySummarySnapshot? RegistrySummary => _registrySummary;

    private static bool IsLenientBoot()
        => string.Equals(Environment.GetEnvironmentVariable(LenientBootEnvVar), "1", StringComparison.OrdinalIgnoreCase);

    private static void RecordFailure(Type module, string assembly, string phase, Exception ex)
    {
        // Console.Error is the only diagnostic channel available at InitializeModules time
        // (no ILogger yet) — precedent: EmitAssemblySummary writes to Console. Mirrors
        // KoanBackgroundServiceOrchestrator's unconditional LogError on startup failure.
        Console.Error.WriteLine($"[KOAN] BOOT-FAILED module={module.FullName ?? module.Name} assembly={assembly} phase={phase}");
        Console.Error.WriteLine(ex.ToString());
        (_bootFailures ??= new List<ModuleFailure>())
            .Add(new ModuleFailure(module.FullName ?? module.Name, assembly, phase, ex.Message));
    }

    public static void InitializeModules(IServiceCollection services)
    {
        KoanStartupTimeline.Mark(KoanStartupStage.BootstrapStart);
        _bootFailures = new List<ModuleFailure>();
        var lenientBoot = IsLenientBoot();

        // Build a closure of loaded + referenced assemblies and populate AssemblyCache
        var set = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        var cache = AssemblyCache.Instance;
        var verboseAssemblies = string.Equals(Environment.GetEnvironmentVariable("KOAN_VERBOSE_ASSEMBLIES"), "1", StringComparison.OrdinalIgnoreCase);
        var assemblyLog = new List<(Assembly Assembly, string LoadContext)>();
        var discoveredAssemblies = new List<Assembly>();
        // TIER A (fail-fast.json): assembly-closure load failures stay LENIENT — Assembly.Load of
        // referenced names routinely fails for legitimate reasons (ref-only/trimmed/platform-specific
        // assemblies; Spring @ConditionalOnClass / ReflectionTypeLoadException precedent) — but are now
        // COUNTED and surfaced in the KOAN_VERBOSE_ASSEMBLIES output instead of vanishing entirely.
        var lenientAssemblySkips = 0;

        bool AddAsm(Assembly a, bool isDiscovery = false)
        {
            var name = a.GetName().Name ?? "";
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
                try { refs = asm.GetReferencedAssemblies(); } catch { lenientAssemblySkips++; continue; }
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
                    catch { lenientAssemblySkips++; /* TIER A: skip absent reference, counted */ }
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
                catch { lenientAssemblySkips++; /* TIER A: ignore bad file, counted */ }
            }
        }
        catch { lenientAssemblySkips++; /* TIER A: ignore base-dir scan failure, counted */ }

        EmitAssemblySummary(assemblyLog, discoveredAssemblies, verboseAssemblies, lenientAssemblySkips);

        var initializerTypes = KoanRegistry.GetInitializerTypes();
        var autoRegistrarTypes = KoanRegistry.GetAutoRegistrarTypes();
        var backgroundServices = KoanRegistry.GetBackgroundServices();
        var serviceDiscoveryAdapters = KoanRegistry.GetServiceDiscoveryAdapters();

        // Publish an early snapshot (no failures yet) so a fail-fast crash still leaves the registry
        // summary populated for any best-effort boot-report rendering up the stack.
        _registrySummary = BuildRegistrySummary(initializerTypes, autoRegistrarTypes, backgroundServices, serviceDiscoveryAdapters);

        // CORE-0003: Always run initializers for every ServiceCollection.
        // Initializers are responsible for their own idempotency (AppDomain-scoped guards for static state).
        // Source-generated registries track eligible types deterministically.
        //
        // TIER B (fail-fast.json refinedRecommendation): an exception escaping initializer construction
        // or Initialize() is NO LONGER swallowed. It is written to Console.Error, recorded into the
        // registry summary (MODULES-FAILED channel), and rethrown wrapped in KoanBootException — unless
        // KOAN_BOOT_LENIENT=1, in which case the host boots degraded with the failure left visible in the
        // boot report. Mirrors KoanBackgroundServiceOrchestrator.FailFastOnStartupFailure=true (the policy
        // the framework already ships for startup services) and .NET 6's BackgroundService StopHost move.
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
            catch (Exception ex)
            {
                var asmName = initializerType.Assembly.GetName();
                RecordFailure(initializerType, asmName.Name ?? "<unknown>", "initializer", ex);
                if (!lenientBoot)
                {
                    throw new KoanBootException(
                        initializerType,
                        asmName.Name ?? "<unknown>",
                        asmName.Version?.ToString() ?? "unknown",
                        "initializer",
                        ex);
                }
            }
        }

        // Re-publish the registry summary now that the failures list is fully populated so the boot
        // report (AppRuntime → KoanConsoleBlocks) can render a MODULES-FAILED block in lenient mode.
        var registrySummary = BuildRegistrySummary(initializerTypes, autoRegistrarTypes, backgroundServices, serviceDiscoveryAdapters);
        _registrySummary = registrySummary;

        KoanStartupTimeline.Mark(KoanStartupStage.DataReady);
    }

    // internal (not private) so AssemblySummaryVerboseGateSpec can pin the KOAN_VERBOSE_ASSEMBLIES gate
    // directly (InternalsVisibleTo: Koan.Tests.Integration.Bootstrap) without booting a full host.
    internal static void EmitAssemblySummary(
        List<(Assembly Assembly, string LoadContext)> assemblyLog,
        List<Assembly> discoveredAssemblies,
        bool verboseAssemblies,
        int lenientAssemblySkips)
    {
        // The raw ASSEMBLIES|… lines and the assembly-scan JSON payload are machine-oriented diagnostics.
        // Keep stdout human by default — the module inventory already appears in the KoanConsoleBlocks boot
        // report — and surface this whole block only under KOAN_VERBOSE_ASSEMBLIES (H9).
        if (!verboseAssemblies) return;

        static string Classify(Assembly asm)
        {
            var name = asm.GetName().Name ?? "";
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
            discovered = discoveredAssemblies.Select(a => a.GetName().Name ?? "").ToArray()
        };
        Console.WriteLine(JsonSerializer.Serialize(payload));

        // TIER A counts (fail-fast.json): absent/unloadable references skipped during closure are
        // no longer silent — surface them here alongside the assembly listing.
        Console.WriteLine($"ASSEMBLIES|lenientSkips={lenientAssemblySkips}");
        foreach (var entry in assemblyLog.OrderBy(a => a.Assembly.GetName().Name, StringComparer.OrdinalIgnoreCase))
        {
            var name = entry.Assembly.GetName();
            Console.WriteLine($"ASSEMBLY|{name.Name} {name.Version} :: {entry.Assembly.Location} :: ALC={entry.LoadContext}");
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

        // Snapshot the accumulated boot failures (MODULES-FAILED channel) so the boot report can
        // render them — the partial mechanism fail-fast.json identified as "just isn't wired".
        IReadOnlyList<ModuleFailure> failures = _bootFailures is { Count: > 0 }
            ? _bootFailures.ToArray()
            : Array.Empty<ModuleFailure>();

        return new RegistrySummarySnapshot(
            initializerTypes.Count,
            initializerBreakdown,
            autoRegistrarTypes.Count,
            backgroundServices.Count,
            startupServices,
            periodicServices,
            serviceDiscoveryAdapters.Count,
            failures);
    }

    private static Action<Assembly>? _manifestLoader;

    /// <summary>
    /// Test-only seam (InternalsVisibleTo: Koan.Tests.Integration.Bootstrap). Replaces ONLY the inner
    /// per-assembly loader invocation (the <c>RegistryManifestLoader.PopulateFromAssembly</c> call) while
    /// leaving the production fail-loud wrapper in <see cref="RunManifestLoader"/> — the unwrap,
    /// <see cref="RecordFailure"/>, phase-string, and <see cref="KoanBootException"/> throw — fully in force.
    /// This is the smallest possible seam to reach the TIER B branch: that branch is otherwise unreachable
    /// because <c>PopulateFromAssembly</c> is source-generated per assembly and swallows every reflection
    /// failure internally, so nothing a test can plant makes the real loader throw. Always <c>null</c> in
    /// production — the default path (real reflected loader) is byte-for-byte unchanged.
    /// </summary>
    internal static Action<Assembly>? ManifestLoaderInvocationOverrideForTests;

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
            return asm => RunManifestLoader(loaderType, inner => method.Invoke(null, new object?[] { inner }), asm);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The TIER B (fail-fast.json) manifest-invoker wrapper. <paramref name="invokeLoader"/> is the inner
    /// per-assembly loader call — normally the reflected <c>RegistryManifestLoader.PopulateFromAssembly</c>,
    /// but replaced by <see cref="ManifestLoaderInvocationOverrideForTests"/> when a spec needs to force the
    /// otherwise-unreachable escaping-exception branch. The unwrap / record / fail-loud policy below is the
    /// production behaviour under test and is identical regardless of which inner loader ran.
    /// </summary>
    private static void RunManifestLoader(Type loaderType, Action<Assembly> invokeLoader, Assembly asm)
    {
        var effectiveLoader = ManifestLoaderInvocationOverrideForTests ?? invokeLoader;

        // TIER B (fail-fast.json): a failure of the manifest-invoker itself can silently no-op
        // the ENTIRE framework (nothing is ever discovered, AddKoan() does nothing). Per-type
        // and ReflectionTypeLoadException leniency already lives INSIDE PopulateFromAssembly, so
        // an exception escaping to here is a framework bug — fail loud unless KOAN_BOOT_LENIENT=1.
        try { effectiveLoader(asm); }
        catch (Exception ex)
        {
            var actual = (ex as TargetInvocationException)?.InnerException ?? ex;
            var phase = $"manifest-invoker(scanning '{asm.GetName().Name}')";
            var asmName = loaderType.Assembly.GetName();
            RecordFailure(loaderType, asmName.Name ?? "<unknown>", phase, actual);
            if (!IsLenientBoot())
            {
                throw new KoanBootException(
                    loaderType,
                    asmName.Name ?? "<unknown>",
                    asmName.Version?.ToString() ?? "unknown",
                    phase,
                    actual);
            }
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
    int ServiceDiscoveryAdapters,
    IReadOnlyList<ModuleFailure> ModuleFailures);

/// <summary>
/// A boot-time module failure recorded into the registry summary so the boot report can render a
/// MODULES-FAILED block (Track F · fail-fast.json). Populated in BOTH fail-fast and lenient modes —
/// in fail-fast the host also throws <see cref="KoanBootException"/>; in lenient mode the recorded
/// entry is the only visibility the operator gets.
/// </summary>
internal readonly record struct ModuleFailure(string Module, string Assembly, string Phase, string Error);
