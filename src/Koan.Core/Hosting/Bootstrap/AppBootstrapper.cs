using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Hosting.Modules;
using Koan.Core.Hosting.Registry;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Koan.Core.Diagnostics;
using Koan.Core.Composition;
using Koan.Core.Infrastructure;
using Koan.Core.Semantics;

namespace Koan.Core.Hosting.Bootstrap;

// Greenfield bootstrapper: compiles and activates the modules present in the host constitution.
public static class AppBootstrapper
{
    /// <summary>
    /// Degraded-boot opt-out for an escaping reflection-manifest failure. Semantic module activation
    /// always remains fail-closed because a partially registered constitution is not a valid host.
    /// </summary>
    private const string LenientBootEnvVar = "KOAN_BOOT_LENIENT";

    private static RegistrySummarySnapshot? _registrySummary;

    // Boot-time failures accumulated by the manifest-invoker fallback and surfaced through the
    // registry snapshot's MODULES-FAILED block.
    [ThreadStatic]
    private static List<ModuleFailure>? _bootFailures;

    [ThreadStatic]
    private static List<KoanFact>? _bootFacts;

    internal static RegistrySummarySnapshot? RegistrySummary => _registrySummary;

    private static bool IsLenientBoot()
        => string.Equals(Environment.GetEnvironmentVariable(LenientBootEnvVar), "1", StringComparison.OrdinalIgnoreCase);

    private static KoanFact RecordFailure(Type module, string assembly, string phase, Exception ex)
    {
        // Console.Error is the only diagnostic channel available at InitializeModules time
        // (no ILogger yet) — precedent: EmitAssemblySummary writes to Console. Mirrors
        // KoanBackgroundServiceOrchestrator's unconditional LogError on startup failure.
        Console.Error.WriteLine($"[KOAN] BOOT-FAILED module={module.FullName ?? module.Name} assembly={assembly} phase={phase}");
        Console.Error.WriteLine(ex.ToString());
        (_bootFailures ??= new List<ModuleFailure>())
            .Add(new ModuleFailure(module.FullName ?? module.Name, assembly, phase, ex.Message));

        var fact = KoanFact.Create(
            Constants.Diagnostics.Codes.ModuleRejected,
            KoanFactKind.Rejection,
            KoanFactState.Rejected,
            module.FullName ?? module.Name,
            "Koan rejected a module during activation.",
            Constants.Diagnostics.Reasons.ModuleActivationFailed,
            "Fix the module activation failure or remove the module reference. Use lenient boot only for diagnosis.",
            assembly,
            $"bootstrap:{module.FullName ?? module.Name}:{phase}");
        (_bootFacts ??= new List<KoanFact>()).Add(fact);
        return fact;
    }

    public static void InitializeModules(IServiceCollection services)
    {
        var semanticSession = SemanticCompositionSession.GetOrCreate(services);
        if (!semanticSession.TryBeginModuleInitialization()) return;

        try
        {
            InitializeModulesCore(services, semanticSession);
        }
        catch (Exception exception)
        {
            semanticSession.FailModuleInitialization(exception);
            throw;
        }
    }

    private static void InitializeModulesCore(
        IServiceCollection services,
        SemanticCompositionSession semanticSession)
    {
        using var composition = Composition.KoanCompositionScope.Enter(services);
        KoanStartupTimeline.Mark(KoanStartupStage.BootstrapStart);
        _bootFailures = new List<ModuleFailure>();
        _bootFacts = new List<KoanFact>();
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

        // Reference=Intent modules (X-aot-substrate). A connector referenced via <ProjectReference> but never
        // symbol-used is omitted from the compiled metadata (so the reference fixpoint above can't see it), and
        // under a single-file publish it leaves no loose Koan.*.dll on disk to scan. The build embeds the Koan
        // module list (the same @(ReferencePath) Koan-filter that drives the composition lockfile) as the
        // "koan.modules.manifest" resource in the app assembly — which DOES survive single-file bundling, unlike
        // the loose DLLs and the .deps.json (DependencyContext.Default is null under single-file). Load each.
        // Assembly.Load is a no-op for already-loaded assemblies, so this is safe to run unconditionally.
        lenientAssemblySkips += LoadIntentModulesFromManifest(AddAsm);

        // Loose Koan.*.dll fallback: a directory deployment that drops a module assembly NOT in the embedded
        // manifest (e.g. a side-loaded plugin, or a consumer that doesn't import the Koan build targets) is
        // still discovered. No-op under single-file (no loose DLLs).
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

        // Build provenance is host-owned application composition, not assembly discovery. Register it
        // before pillar modules so provider elections can distinguish direct intent from
        // transitive module presence without probing the load graph.
        var applicationAssembly = Assembly.GetEntryAssembly();
        var referenceManifest = KoanApplicationReferenceManifest.Load(applicationAssembly);

        var backgroundServices = KoanRegistry.GetBackgroundServices();
        var serviceDiscoveryAdapters = KoanRegistry.GetServiceDiscoveryAdapters();
        var semanticDescriptors = KoanRegistry.GetSemanticModuleDescriptors();

        // Filter and validate every descriptor before invoking any factory, then complete the entire
        // construction barrier before registration begins.
        var constitution = SemanticActivationCompiler.Compile(referenceManifest, semanticDescriptors, applicationAssembly);
        _bootFacts.AddRange(constitution.ToFacts());
        var semanticModules = SemanticModuleRuntime.Create(constitution);
        semanticSession.CompleteModuleInitialization(referenceManifest, constitution, semanticModules);
        if (semanticModules.Modules.Count > 0)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, KoanModuleHost>());
        }

        // Publish an early snapshot (no failures yet) so a fail-fast crash still leaves the registry
        // summary populated for any best-effort boot-report rendering up the stack.
        _registrySummary = BuildRegistrySummary(semanticModules, backgroundServices, serviceDiscoveryAdapters);

        // One retained instance owns registration, startup, composition evidence, and provenance.
        // A registration failure rejects the compiled constitution; there is no partial/legacy lane.
        try
        {
            semanticModules.Register(services);
        }
        catch (SemanticModuleRuntime.SemanticRuntimeException exception)
        {
            var moduleType = semanticModules.GetModule(exception.Problem.Owner).GetType();
            var assembly = moduleType.Assembly.GetName();
            var fact = RecordFailure(
                moduleType,
                assembly.Name ?? "<unknown>",
                "register",
                exception);
            _registrySummary = BuildRegistrySummary(semanticModules, backgroundServices, serviceDiscoveryAdapters);
            throw new KoanBootException(
                moduleType,
                assembly.Name ?? "<unknown>",
                assembly.Version?.ToString() ?? "unknown",
                "register",
                exception.InnerException ?? exception,
                fact);
        }

        // Re-publish the registry summary now that the failures list is fully populated so the boot
        // report (AppRuntime → KoanConsoleBlocks) can render a MODULES-FAILED block in lenient mode.
        var registrySummary = BuildRegistrySummary(semanticModules, backgroundServices, serviceDiscoveryAdapters);
        _registrySummary = registrySummary;
        services.AddSingleton(new KoanBootstrapSnapshot(registrySummary, (_bootFacts ?? []).ToArray()));

        KoanStartupTimeline.Mark(KoanStartupStage.DataReady);
    }

    /// <summary>The build-embedded module manifest resource (one <c>Koan.*</c> assembly name per line). Emitted
    /// by <c>build/Sylin.Koan.Core.targets</c> from the same <c>@(ReferencePath)</c> Koan-filter as the
    /// composition lockfile, into the app assembly — so it survives single-file bundling.</summary>
    internal const string ModuleManifestResourceName = Constants.Composition.ModuleManifestResourceName;

    /// <summary>
    /// Loads every <c>Koan.*</c> assembly named in the entry assembly's embedded <see cref="ModuleManifestResourceName"/>
    /// — the build-time intent manifest that survives single-file bundling (unlike loose DLLs, whose directory is
    /// empty, and the .deps.json, whose <c>DependencyContext.Default</c> is null when bundled). Reference=Intent
    /// connectors are listed here even when no symbol uses them. <see cref="Assembly.Load(AssemblyName)"/> is a
    /// no-op for already-loaded assemblies, so this runs unconditionally. Returns the count of lenient skips.
    /// </summary>
    // internal + injectable source so EmbeddedModuleManifestSpec can exercise the read/parse/load path against a
    // test assembly carrying a planted manifest (InternalsVisibleTo: Koan.Tests.Integration.Bootstrap).
    internal static int LoadIntentModulesFromManifest(Func<Assembly, bool, bool> addAsm, Assembly? source = null)
    {
        var skips = 0;
        try
        {
            var entry = source ?? Assembly.GetEntryAssembly();
            using var stream = entry?.GetManifestResourceStream(ModuleManifestResourceName);
            if (stream is null) return 0; // consumer didn't import the Koan build targets — other prongs run
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                var name = line.Trim();
                if (name.Length == 0 || !name.StartsWith("Koan.", StringComparison.Ordinal)) continue;
                try { addAsm(Assembly.Load(new AssemblyName(name)), /* isDiscovery: */ true); }
                catch { skips++; /* TIER A: a named-but-unloadable module is counted, not fatal */ }
            }
        }
        catch { skips++; /* manifest unreadable — fall back to the other prongs */ }
        return skips;
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
            Console.WriteLine($"ASSEMBLY|{name.Name} {name.Version} :: ALC={entry.LoadContext}");
        }
    }

    private static RegistrySummarySnapshot BuildRegistrySummary(
        SemanticModuleRuntime modules,
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

        var moduleTypes = modules.ImplementationTypes;
        var moduleBreakdown = moduleTypes
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
            moduleTypes.Count,
            moduleBreakdown,
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
            var fact = RecordFailure(loaderType, asmName.Name ?? "<unknown>", phase, actual);
            if (!IsLenientBoot())
            {
                throw new KoanBootException(
                    loaderType,
                    asmName.Name ?? "<unknown>",
                    asmName.Version?.ToString() ?? "unknown",
                    phase,
                    actual,
                    fact);
            }
        }
    }
}

internal readonly record struct RegistrySummarySnapshot(
    int Modules,
    IReadOnlyList<(string Namespace, int Count)> ModuleBreakdown,
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
