using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Logging;
using Koan.Core.Observability;
using Koan.Core;

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
            var report = new BootReport();
            var cfg = _sp.GetService<IConfiguration>();

            // Collect module information from all KoanAutoRegistrars
            CollectBootReport(report, cfg);

            var modules = report.GetModules();
            var modulePairs = modules
                .Select(m => (m.Name, m.Version ?? "unknown"))
                .ToList();

            var snapshot = KoanEnv.CurrentSnapshot;
            var runtimeVersion = ResolveRuntimeVersion(modulePairs);

            var hostDescription = DescribeHost();
            var headerBlock = KoanConsoleBlocks.BuildBootstrapHeaderBlock(snapshot, hostDescription, modulePairs, runtimeVersion);
            var inventoryBlock = KoanConsoleBlocks.BuildInventoryBlock(snapshot, modulePairs);

            if (_logger is not null)
            {
                _logger.LogInformation("{Block}", headerBlock);
                _logger.LogInformation("{Block}", inventoryBlock);
            }

            KoanStartupTimeline.Mark(KoanStartupStage.ConfigReady);

            var show = !KoanEnv.IsProduction;
            var obs = _sp.GetService<Microsoft.Extensions.Options.IOptions<Koan.Core.Observability.ObservabilityOptions>>();
            if (!show)
                show = obs?.Value?.Enabled == true && obs.Value?.Traces?.Enabled == true;

            if (show && _logger is null)
            {
                try
                {
                    Console.Write(headerBlock);
                    Console.Write(inventoryBlock);
                }
                catch
                {
                    // best-effort only
                }
            }
        }
        catch { /* best-effort */ }
    }

    private void CollectBootReport(BootReport report, IConfiguration? cfg)
    {
        if (cfg == null) return;

        var env = _sp.GetService<IHostEnvironment>();

        // Find and invoke all KoanAutoRegistrars to collect their reports
        // Use cached assemblies instead of bespoke AppDomain scanning
        var assemblies = AssemblyCache.Instance.GetAllAssemblies();
        foreach (var asm in assemblies)
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch { continue; }

            foreach (var t in types)
            {
                if (t.IsAbstract || !typeof(IKoanAutoRegistrar).IsAssignableFrom(t)) continue;
                try
                {
                    if (Activator.CreateInstance(t) is IKoanAutoRegistrar registrar)
                    {
                        var hostEnv = env ?? new DefaultHostEnvironment();
                        registrar.Describe(report, cfg, hostEnv);
                    }
                }
                catch { /* best-effort */ }
            }
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
