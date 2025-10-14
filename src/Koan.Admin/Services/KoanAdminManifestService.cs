using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Admin.Contracts;
using Koan.Core;
using Koan.Core.Observability.Health;
using Koan.Core.Provenance;
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

        var snapshot = KoanEnv.Provenance ?? ProvenanceRegistry.Instance.CurrentSnapshot;

        var modules = snapshot.Pillars
            .SelectMany(pillar => pillar.Modules.Select(module => MapModule(pillar.Label, module)))
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
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

    private static KoanAdminModuleManifest MapModule(string pillarLabel, ProvenanceModule module)
    {
        var settings = module.Settings
            .Select(setting => new KoanAdminModuleSetting(
                setting.Key,
                setting.Value ?? string.Empty,
                setting.IsSecret,
                ConvertSource(setting.Source),
                setting.SourceKey ?? string.Empty,
                setting.Consumers.Count == 0 ? Array.Empty<string>() : setting.Consumers.ToArray()))
            .ToList();

        var notes = module.Notes
            .Select(n => n.Message)
            .ToList();

        if (!string.IsNullOrWhiteSpace(module.Status))
        {
            var statusLine = string.IsNullOrWhiteSpace(module.StatusDetail)
                ? module.Status
                : $"{module.Status}: {module.StatusDetail}";
            notes.Insert(0, statusLine!);
        }

        var tools = module.Tools
            .Select(t => new KoanAdminModuleTool(t.Name, t.Route, t.Description, t.Capability))
            .ToList();

        var description = string.IsNullOrWhiteSpace(module.Description)
            ? pillarLabel
            : module.Description;

        var isStub = settings.Count == 0 && notes.Count == 0 && tools.Count == 0;

        return new KoanAdminModuleManifest(
            module.Name,
            module.Version,
            description,
            isStub,
            settings,
            notes,
            tools);
    }

    private static KoanAdminSettingSource ConvertSource(ProvenanceSettingSource source)
        => source switch
        {
            ProvenanceSettingSource.Auto => KoanAdminSettingSource.Auto,
            ProvenanceSettingSource.AppSettings => KoanAdminSettingSource.AppSettings,
            ProvenanceSettingSource.Environment => KoanAdminSettingSource.Environment,
            ProvenanceSettingSource.LaunchKit => KoanAdminSettingSource.LaunchKit,
            ProvenanceSettingSource.Custom => KoanAdminSettingSource.Custom,
            _ => KoanAdminSettingSource.Unknown
        };
}
