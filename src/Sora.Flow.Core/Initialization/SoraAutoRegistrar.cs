using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sora.Core;
using Sora.Flow.Options;
using Sora.Flow.Sending;
using Sora.Flow.Attributes;

namespace Sora.Flow.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Flow.Core";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Do not auto-start the Flow runtime in every process.
        // Producer-only adapters reference Sora.Flow.Core for types/attributes but must not run background workers.
        // Orchestrators/APIs should call services.AddSoraFlow() explicitly in Program.cs.
        // Safe default here: install naming only so storage naming remains consistent if used.
        services.AddSoraFlowNaming();

        // Provide identity stamper for server-side stamping (safe in producers)
        services.TryAddSingleton<IFlowIdentityStamper, FlowIdentityStamper>();

    // Lightweight, producer-safe self-announcement: enable adapters to publish heartbeat announcements
    // without pulling in full Flow runtime. Options are needed for timings/gates.
    services.AddOptions<AdapterRegistryOptions>().BindConfiguration("Sora:Flow:AdapterRegistry");
    services.TryAddSingleton<IAdapterIdentity>(sp =>
    {
        var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AdapterRegistryOptions>>().Value;
        return new AdapterIdentity(opts.HeartbeatSeconds);
    });
    services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, AdapterSelfAnnouncer>());

        // Auto-register adapter publishers: BackgroundService types annotated with [FlowAdapter]
        // Config gates: Sora:Flow:Adapters:AutoStart (bool), Include (string[] "system:adapter"), Exclude (string[])
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISoraInitializer>(sp => new AdapterScannerInitializer()));
    }

    private sealed class AdapterScannerInitializer : ISoraInitializer
    {
        public void Initialize(IServiceCollection services)
        {
            // Resolve configuration if available via existing DI registrations during StartSora/AddSora pipelines
            IConfiguration? cfg = null;
            try
            {
                var existing = services.FirstOrDefault(d => d.ServiceType == typeof(IConfiguration));
                cfg = existing?.ImplementationInstance as IConfiguration;
            }
            catch { }

            bool inContainer = SoraEnv.InContainer;
            bool autoStart = inContainer; // default: on in containers, off elsewhere
            string[] include = Array.Empty<string>();
            string[] exclude = Array.Empty<string>();
            try
            {
                if (cfg is not null)
                {
                    autoStart = cfg.GetValue<bool?>("Sora:Flow:Adapters:AutoStart") ?? autoStart;
                    include = cfg.GetSection("Sora:Flow:Adapters:Include").Get<string[]>() ?? include;
                    exclude = cfg.GetSection("Sora:Flow:Adapters:Exclude").Get<string[]>() ?? exclude;
                }
            }
            catch { }

            if (!autoStart) return;

            bool Matches(string sys, string adp)
            {
                var code = $"{sys}:{adp}";
                if (exclude.Any(x => string.Equals(x, code, StringComparison.OrdinalIgnoreCase))) return false;
                if (include.Length == 0) return true;
                return include.Any(x => string.Equals(x, code, StringComparison.OrdinalIgnoreCase));
            }

            foreach (var (t, attr) in DiscoverAdapterHostedServices())
            {
                if (!Matches(attr.System, attr.Adapter)) continue;
                services.AddSingleton(typeof(IHostedService), t);
            }
        }

        private static IEnumerable<(Type Type, FlowAdapterAttribute Attr)> DiscoverAdapterHostedServices()
        {
            var results = new List<(Type, FlowAdapterAttribute)>();
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in assemblies)
            {
                Type?[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException rtle) { types = rtle.Types; }
                catch { continue; }
                foreach (var t in types)
                {
                    if (t is null || t.IsAbstract) continue;
                    if (!typeof(BackgroundService).IsAssignableFrom(t)) continue;
                    var attr = t.GetCustomAttribute<FlowAdapterAttribute>(inherit: true);
                    if (attr is null) continue;
                    results.Add((t, attr));
                }
            }
            return results;
        }
    }

    public void Describe(Sora.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        var opts = cfg.GetSection("Sora:Flow").Get<FlowOptions>() ?? new FlowOptions();
    report.AddModule(ModuleName, ModuleVersion);
    report.AddSetting("runtime", "InMemory");
    report.AddSetting("batch", opts.BatchSize.ToString());
    report.AddSetting("concurrency.Standardize", opts.StandardizeConcurrency.ToString());
    report.AddSetting("concurrency.Key", opts.KeyConcurrency.ToString());
    report.AddSetting("concurrency.Associate", opts.AssociateConcurrency.ToString());
    report.AddSetting("concurrency.Project", opts.ProjectConcurrency.ToString());
    report.AddSetting("ttl.Intake", opts.IntakeTtl.ToString());
    report.AddSetting("ttl.Standardized", opts.StandardizedTtl.ToString());
    report.AddSetting("ttl.Keyed", opts.KeyedTtl.ToString());
    report.AddSetting("ttl.ProjectionTask", opts.ProjectionTaskTtl.ToString());
    report.AddSetting("ttl.RejectionReport", opts.RejectionReportTtl.ToString());
    report.AddSetting("purge.Enabled", opts.PurgeEnabled.ToString().ToLowerInvariant());
    report.AddSetting("purge.Interval", opts.PurgeInterval.ToString());
    report.AddSetting("dlq", opts.DeadLetterEnabled.ToString().ToLowerInvariant());
    report.AddSetting("defaultView", opts.DefaultViewName);
    if (opts.AggregationTags?.Length > 0)
        report.AddSetting("aggregation.tags", string.Join(",", opts.AggregationTags));
    }
}
