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

namespace Koan.Core.Hosting.Runtime;

internal sealed class AppRuntime : IAppRuntime
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<AppRuntime>? _logger;

    public AppRuntime(IServiceProvider sp, ILogger<AppRuntime>? logger = null)
    {
        _sp = sp;
        _logger = logger;
    }

    public void Discover()
    {
        // Initialize KoanEnv and print a bootstrap report if enabled
        try { KoanEnv.TryInitialize(_sp); } catch { }
        KoanStartupTimeline.Mark(KoanStartupStage.BootstrapStart);
        try
        {
            IProvenanceRegistry registry = ProvenanceRegistry.Instance;
            var cfg = _sp.GetService<IConfiguration>();

            // Collect module information from all KoanAutoRegistrars
            CollectProvenance(registry, cfg);

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
            catch
            {
                // intentionally best-effort
            }

            // StartupProbeService seeds the health snapshot on a background task that runs AFTER the host
            // starts, so at boot-report time the snapshot is typically empty even when probes ARE registered.
            // Capture how many contributors are registered so the report can honestly say "pending" rather
            // than fabricate an overall verdict the probes have not yet produced (H9 health-line race fix).
            var registeredProbes = 0;
            try { registeredProbes = _sp.GetService<IHealthRegistry>()?.All.Count ?? 0; }
            catch { /* best-effort */ }

            var hostDescription = DescribeHost();
            var compositionLine = TryBuildCompositionLine(snapshot);
            var startupBlock = KoanConsoleBlocks.BuildStartupOverviewBlock(
                snapshot,
                hostDescription,
                modulePairs,
                runtimeVersion,
                AppBootstrapper.RegistrySummary,
                healthSnapshot,
                registeredProbes,
                compositionLine);

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
        catch { /* best-effort */ }
    }

    private void CollectProvenance(IProvenanceRegistry registry, IConfiguration? cfg)
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
            catch
            {
                // best-effort only; provenance should never block host start
            }
        }
    }

    // P1.1: build the one-line composition verdict and write the resolved-twin lockfile. Best-effort
    // — a composition failure must never disrupt the boot report. Runs here (Discover) because _sp is
    // fully built; the resolved twin is a non-production artifact.
    private string? TryBuildCompositionLine(KoanEnvironmentSnapshot snapshot)
    {
        try
        {
            var cfg = _sp.GetService<IConfiguration>();
            var contentRoot = _sp.GetService<IHostEnvironment>()?.ContentRootPath;
            var appName = string.IsNullOrWhiteSpace(snapshot.Application.Name) ? "app" : snapshot.Application.Name;

            var resolved = Composition.KoanCompositionSnapshot.Build(_sp, appName, cfg);
            if (!KoanEnv.IsProduction)
                Composition.KoanCompositionSnapshot.TryWriteResolvedTwin(resolved, contentRoot);

            var lockedPath = string.IsNullOrEmpty(contentRoot) ? null : Path.Combine(contentRoot!, "koan.lock.json");
            var locked = lockedPath is null ? null : Composition.KoanLockfileSerializer.TryReadFile(lockedPath);
            var comparison = Composition.KoanLockfileComparer.Compare(locked, resolved);
            return comparison.Format(resolved.Modules.Count);
        }
        catch
        {
            return null;
        }
    }

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
