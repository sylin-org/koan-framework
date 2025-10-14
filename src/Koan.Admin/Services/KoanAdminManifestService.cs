using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Koan.Admin.Contracts;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Observability.Health;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.Admin.Services;

internal sealed class KoanAdminManifestService : IKoanAdminManifestService
{
    private readonly IServiceProvider _services;
    private readonly IHealthAggregator? _healthAggregator;
    private readonly ILogger<KoanAdminManifestService> _logger;
    private readonly object _cacheLock = new();
    private KoanAdminManifest? _cachedManifest;
    private DateTimeOffset _manifestCacheExpires;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    public KoanAdminManifestService(
        IServiceProvider services,
        ILogger<KoanAdminManifestService> logger,
        IHealthAggregator? healthAggregator = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _healthAggregator = healthAggregator;
    }

    public Task<KoanAdminManifest> BuildAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (TryGetCached(out var cached))
        {
            return Task.FromResult(cached);
        }

        var configuration = _services.GetService(typeof(IConfiguration)) as IConfiguration;
        var environment = _services.GetService(typeof(IHostEnvironment)) as IHostEnvironment
            ?? new DefaultHostEnvironment();

        var report = new BootReport();
        if (configuration is not null)
        {
            Collect(report, configuration, environment, _logger);
        }

        var modules = report.GetModules()
            .Select(m => new KoanAdminModuleManifest(
                m.Name,
                m.Version,
                m.Description,
                m.IsStub,
                m.Settings
                    .Select(s => new KoanAdminModuleSetting(
                        s.Key,
                        s.Value,
                        s.Secret,
                        ConvertSource(s.Source),
                        s.SourceKey,
                        s.Consumers.Count == 0 ? Array.Empty<string>() : s.Consumers.ToArray()))
                    .ToList(),
                m.Notes.ToList(),
                m.Tools
                    .Select(t => new KoanAdminModuleTool(t.Name, t.Route, t.Description, t.Capability))
                    .ToList()))
            .ToList();

        var health = BuildHealth();
        var manifest = new KoanAdminManifest(DateTimeOffset.UtcNow, modules, health);
        Cache(manifest);

        _logger.LogDebug("Koan Admin manifest generated with {ModuleCount} modules.", modules.Count);

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

    private static readonly Lazy<Type[]> RegistrarTypes = new(DiscoverRegistrars);

    private static void Collect(
        BootReport report,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger logger)
    {
        foreach (var type in RegistrarTypes.Value)
        {
            try
            {
                if (Activator.CreateInstance(type) is IKoanAutoRegistrar registrar)
                {
                    var before = report.ModuleCount;
                    registrar.Describe(report, configuration, environment);
                    var after = report.ModuleCount;
                    if (after > before)
                    {
                        var description = ReadAssemblyDescription(type.Assembly);
                        for (var i = before; i < after; i++)
                        {
                            report.SetModuleDescription(i, description);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to collect manifest details from registrar {Registrar}", type.FullName);
            }
        }
    }

    private static Type[] DiscoverRegistrars()
    {
        var registrars = new List<Type>();
        foreach (var asm in AssemblyCache.Instance.GetAllAssemblies())
        {
            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch (Exception ex)
            {
                // Assembly load failures shouldn't break manifest generation
                System.Diagnostics.Debug.WriteLine($"KoanAdminManifestService: failed to enumerate types for {asm.FullName}: {ex.Message}");
                continue;
            }

            foreach (var type in types)
            {
                if (!type.IsAbstract && typeof(IKoanAutoRegistrar).IsAssignableFrom(type))
                {
                    registrars.Add(type);
                }
            }
        }

        return registrars.ToArray();
    }

    private static string? ReadAssemblyDescription(Assembly assembly)
    {
        if (assembly is null) return null;
        try
        {
            return assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description;
        }
        catch
        {
            return null;
        }
    }

    private bool TryGetCached(out KoanAdminManifest manifest)
    {
        lock (_cacheLock)
        {
            if (_cachedManifest is not null && _manifestCacheExpires > DateTimeOffset.UtcNow)
            {
                manifest = _cachedManifest;
                return true;
            }
        }

        manifest = null!;
        return false;
    }

    private void Cache(KoanAdminManifest manifest)
    {
        lock (_cacheLock)
        {
            _cachedManifest = manifest;
            _manifestCacheExpires = DateTimeOffset.UtcNow.Add(CacheDuration);
        }
    }

    private static KoanAdminSettingSource ConvertSource(BootSettingSource source)
        => source switch
        {
            BootSettingSource.Auto => KoanAdminSettingSource.Auto,
            BootSettingSource.AppSettings => KoanAdminSettingSource.AppSettings,
            BootSettingSource.Environment => KoanAdminSettingSource.Environment,
            BootSettingSource.LaunchKit => KoanAdminSettingSource.LaunchKit,
            BootSettingSource.Custom => KoanAdminSettingSource.Custom,
            _ => KoanAdminSettingSource.Unknown
        };

    private sealed class DefaultHostEnvironment : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "KoanApp";
        public IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public string EnvironmentName { get; set; } = Environments.Production;
    }
}
