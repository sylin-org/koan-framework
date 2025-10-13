using Koan.Admin.Contracts;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Observability.Health;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Koan.Admin.Services;

internal sealed class KoanAdminManifestService : IKoanAdminManifestService
{
    private readonly IServiceProvider _services;
    private readonly IHealthAggregator? _healthAggregator;

    public KoanAdminManifestService(IServiceProvider services, IHealthAggregator? healthAggregator = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _healthAggregator = healthAggregator;
    }

    public Task<KoanAdminManifest> BuildAsync(CancellationToken cancellationToken = default)
    {
        var configuration = _services.GetService(typeof(IConfiguration)) as IConfiguration;
        var environment = _services.GetService(typeof(IHostEnvironment)) as IHostEnvironment
            ?? new DefaultHostEnvironment();

        var report = new BootReport();
        if (configuration is not null)
        {
            Collect(report, configuration, environment);
        }

        var modules = report.GetModules()
            .Select(m => new KoanAdminModuleManifest(
                m.Name,
                m.Version,
                m.Settings.Select(s => new KoanAdminModuleSetting(s.Key, s.Value, s.Secret)).ToList(),
                m.Notes.ToList()))
            .ToList();

        var health = BuildHealth();
        var manifest = new KoanAdminManifest(DateTimeOffset.UtcNow, modules, health);
        return Task.FromResult(manifest);
    }

    public Task<KoanAdminHealthDocument> GetHealthAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(BuildHealth());

    private KoanAdminHealthDocument BuildHealth()
    {
        if (_healthAggregator is null)
        {
            return KoanAdminHealthDocument.Empty;
        }

        HealthSnapshot snapshot;
        try
        {
            snapshot = _healthAggregator.GetSnapshot();
        }
        catch
        {
            return KoanAdminHealthDocument.Empty;
        }

        var components = snapshot.Components
            .Select(c => new KoanAdminHealthComponent(
                c.Component,
                c.Status,
                c.Message,
                c.TimestampUtc,
                c.Facts ?? new Dictionary<string, string>()))
            .ToList();

        return new KoanAdminHealthDocument(snapshot.Overall, components, snapshot.ComputedAtUtc);
    }

    private static void Collect(BootReport report, IConfiguration configuration, IHostEnvironment environment)
    {
        var assemblies = AssemblyCache.Instance.GetAllAssemblies();
        foreach (var asm in assemblies)
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch { continue; }

            foreach (var type in types)
            {
                if (type.IsAbstract || !typeof(IKoanAutoRegistrar).IsAssignableFrom(type)) continue;

                try
                {
                    if (Activator.CreateInstance(type) is IKoanAutoRegistrar registrar)
                    {
                        registrar.Describe(report, configuration, environment);
                    }
                }
                catch
                {
                    // Swallow failures to keep manifest resilient
                }
            }
        }
    }

    private sealed class DefaultHostEnvironment : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "KoanApp";
        public IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public string EnvironmentName { get; set; } = Environments.Production;
    }
}
