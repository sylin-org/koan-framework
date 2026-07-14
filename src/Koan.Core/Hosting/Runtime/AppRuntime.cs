using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Hosting.Registry;
using Koan.Core.Logging;
using Koan.Core;
using Koan.Core.Provenance;
using Koan.Core.Observability;
using Koan.Core.Observability.Health;
using Koan.Core.Diagnostics;
using Koan.Core.Infrastructure;

namespace Koan.Core.Hosting.Runtime;

internal sealed class AppRuntime : IAppRuntime
{
    private readonly IServiceProvider _sp;
    private readonly KoanRuntimeFactStore _runtimeFacts;
    private readonly ILogger<AppRuntime>? _logger;
    private int _discovered;
    private int _started;

    public AppRuntime(IServiceProvider sp, KoanRuntimeFactStore runtimeFacts, ILogger<AppRuntime>? logger = null)
    {
        _sp = sp;
        _runtimeFacts = runtimeFacts;
        _logger = logger;
    }

    public void Discover()
    {
        if (Interlocked.Exchange(ref _discovered, 1) != 0) return;

        var collectedFacts = new List<KoanFact>();
        // Initialize KoanEnv and print a bootstrap report if enabled
        try { KoanEnv.TryInitialize(_sp); } catch { }
        KoanStartupTimeline.Mark(KoanStartupStage.BootstrapStart);
        try
        {
            var bootstrap = _sp.GetService<KoanBootstrapSnapshot>();
            if (bootstrap is not null)
            {
                collectedFacts.AddRange(bootstrap.Facts);
            }

            IProvenanceRegistry registry = ProvenanceRegistry.Instance;
            var cfg = _sp.GetService<IConfiguration>();

            // Collect module information from all KoanAutoRegistrars
            CollectProvenance(registry, cfg, collectedFacts);

            var provenance = registry.CurrentSnapshot;
            var modulePairs = provenance.Pillars
                .SelectMany(p => p.Modules)
                .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => (g.Key, string.IsNullOrWhiteSpace(g.First().Version) ? "unknown" : g.First().Version!))
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var snapshot = KoanEnv.CurrentSnapshot;
            var runtimeVersion = ResolveRuntimeVersion(modulePairs);

            HealthSnapshot? healthSnapshot = null;
            try
            {
                var healthAggregator = _sp.GetService<IHealthAggregator>();
                healthSnapshot = healthAggregator?.GetSnapshot();
            }
            catch (Exception ex)
            {
                collectedFacts.Add(CollectionFailure(
                    "health",
                    typeof(IHealthAggregator),
                    ex,
                    "Koan could not collect the startup health snapshot."));
            }

            // StartupProbeService seeds the health snapshot on a background task that runs AFTER the host
            // starts, so at boot-report time the snapshot is typically empty even when probes ARE registered.
            // Capture how many contributors are registered so the report can honestly say "pending" rather
            // than fabricate an overall verdict the probes have not yet produced (H9 health-line race fix).
            var registeredProbes = 0;
            try { registeredProbes = _sp.GetService<IHealthRegistry>()?.All.Count ?? 0; }
            catch (Exception ex)
            {
                collectedFacts.Add(CollectionFailure(
                    "health-registry",
                    typeof(IHealthRegistry),
                    ex,
                    "Koan could not inspect registered health probes."));
            }

            var hostDescription = DescribeHost();
            var composition = TryBuildComposition(snapshot);
            collectedFacts.AddRange(composition.Facts);
            var factEnvelope = _runtimeFacts.Replace(collectedFacts, complete: true);
            var startupBlock = KoanConsoleBlocks.BuildStartupOverviewBlock(
                snapshot,
                hostDescription,
                modulePairs,
                runtimeVersion,
                bootstrap?.Registry ?? AppBootstrapper.RegistrySummary,
                healthSnapshot,
                registeredProbes,
                composition.Line,
                factEnvelope);

            KoanStartupTimeline.Mark(KoanStartupStage.ConfigReady);
            var timeline = KoanStartupTimeline.GetSummary();

            if (_logger is not null)
            {
                if (timeline.HasValues)
                {
                    _logger.LogInformation("[K:PHASE] {Timeline}", KoanConsoleBlocks.FormatStartupPhases(timeline));
                }

                _logger.LogInformation("{Block}", startupBlock);
            }

            var show = !KoanEnv.IsProduction;
            var obs = _sp.GetService<Microsoft.Extensions.Options.IOptions<Koan.Core.Observability.ObservabilityOptions>>();
            if (!show)
                show = obs?.Value?.Enabled == true && obs.Value?.Traces?.Enabled == true;

            if (show && _logger is null)
            {
                try
                {
                    if (timeline.HasValues)
                    {
                        Console.WriteLine($"[K:PHASE] {KoanConsoleBlocks.FormatStartupPhases(timeline)}");
                    }
                    Console.Write(startupBlock);
                }
                catch
                {
                    // best-effort only
                }
            }
        }
        catch (Exception ex)
        {
            collectedFacts.Add(CollectionFailure(
                "startup-report",
                typeof(AppRuntime),
                ex,
                "Koan could not complete runtime fact collection."));
            _runtimeFacts.Replace(collectedFacts, complete: true);
        }
    }

    private void CollectProvenance(
        IProvenanceRegistry registry,
        IConfiguration? cfg,
        ICollection<KoanFact> facts)
    {
        if (cfg == null) return;

        var env = _sp.GetService<IHostEnvironment>();

        foreach (var registrarType in KoanRegistry.GetAutoRegistrarTypes())
        {
            try
            {
                if (registrarType.IsAbstract) continue;
                if (Activator.CreateInstance(registrarType) is not IKoanAutoRegistrar registrar) continue;

                var hostEnv = env ?? new DefaultHostEnvironment();
                var module = registry.GetOrCreateModule("", registrar.ModuleName);
                module.Describe(registrar.ModuleVersion);
                registrar.Describe(module, cfg, hostEnv);
            }
            catch (Exception ex)
            {
                facts.Add(CollectionFailure(
                    $"provenance:{registrarType.FullName ?? registrarType.Name}",
                    registrarType,
                    ex,
                    "A module could not report its configuration provenance."));
            }
        }
    }

    // P1.1: build the one-line composition verdict and write the resolved-twin lockfile. Best-effort
    // — a composition failure must never disrupt the boot report. Runs here (Discover) because _sp is
    // fully built; the resolved twin is a non-production artifact.
    private CompositionReport TryBuildComposition(KoanEnvironmentSnapshot snapshot)
    {
        try
        {
            var cfg = _sp.GetService<IConfiguration>();
            var contentRoot = _sp.GetService<IHostEnvironment>()?.ContentRootPath;
            var appName = string.IsNullOrWhiteSpace(snapshot.Application.Name) ? "app" : snapshot.Application.Name;

            var result = Composition.KoanCompositionSnapshot.BuildResult(_sp, appName, cfg);
            var resolved = result.Lockfile;
            if (!KoanEnv.IsProduction)
                Composition.KoanCompositionSnapshot.TryWriteResolvedTwin(resolved, contentRoot);

            var lockedPath = string.IsNullOrEmpty(contentRoot) ? null : Path.Combine(contentRoot!, "koan.lock.json");
            var locked = lockedPath is null ? null : Composition.KoanLockfileSerializer.TryReadFile(lockedPath);
            var comparison = Composition.KoanLockfileComparer.Compare(locked, resolved);
            return new CompositionReport(
                comparison.Format(resolved.Modules.Count),
                result.Facts.Append(comparison.ToFact(resolved.Modules.Count)).ToArray());
        }
        catch (Exception ex)
        {
            return new CompositionReport(
                null,
                [CollectionFailure(
                    "composition",
                    typeof(Composition.KoanCompositionSnapshot),
                    ex,
                    "Koan could not collect the resolved composition snapshot.")]);
        }
    }

    private static KoanFact CollectionFailure(
        string subject,
        Type source,
        Exception exception,
        string summary)
        => KoanFact.Create(
            Constants.Diagnostics.Codes.CollectionFailed,
            KoanFactKind.Degradation,
            KoanFactState.CollectionFailed,
            subject,
            summary,
            Constants.Diagnostics.Reasons.ReporterFailed,
            "Inspect the named fact source and retry after correcting its reporting failure.",
            source.Assembly.GetName().Name ?? source.Name,
            $"diagnostics:{subject}:{exception.GetType().Name}");

    private string ResolveRuntimeVersion(IReadOnlyList<(string Name, string Version)> modules)
    {
        var coreVersion = modules.FirstOrDefault(m => string.Equals(m.Name, "Koan.Core", StringComparison.OrdinalIgnoreCase)).Version;
        if (!string.IsNullOrWhiteSpace(coreVersion) && !string.Equals(coreVersion, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            return coreVersion;
        }

        return typeof(AppRuntime).Assembly.GetName().Version?.ToString() ?? "unknown";
    }

    private string DescribeHost()
    {
        var hostEnv = _sp.GetService<IHostEnvironment>();
        var applicationName = hostEnv?.ApplicationName ?? "Koan";

        var webHostEnvType = Type.GetType("Microsoft.AspNetCore.Hosting.IWebHostEnvironment, Microsoft.AspNetCore.Hosting.Abstractions", throwOnError: false);
        if (webHostEnvType is not null && _sp.GetService(webHostEnvType) is not null)
        {
            return $"ASP.NET Core ({applicationName})";
        }

        return $"Generic Host ({applicationName})";
    }

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;

        // No-op by default; features (e.g., data) can hook into hosted services for start-up work.
        // Keep best-effort guards and avoid throwing during host start.
        try { /* intentional no-op */ } catch { }
    }
}

/// <summary>
/// Simple default implementation of IHostEnvironment for fallback scenarios
/// </summary>
internal class DefaultHostEnvironment : IHostEnvironment
{
    public string ApplicationName { get; set; } = "KoanApp";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public string EnvironmentName { get; set; } = Microsoft.Extensions.Hosting.Environments.Production;
}
