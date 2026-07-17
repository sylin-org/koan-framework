using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Data.Backup.Abstractions;
using Koan.Data.Backup.Extensions;
using Koan.Data.Backup.Infrastructure;
using Koan.Data.Backup.Models;
using Koan.Core.Hosting.Bootstrap;
using BackupItems = Koan.Data.Backup.Infrastructure.BackupProvenanceItems;
using ProvenanceModes = Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions;

namespace Koan.Data.Backup.Initialization;

public sealed class DataBackupModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanBackupRestore();
    }

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);

        var defaults = new BackupRestoreOptions();
        var retentionDefaults = defaults.RetentionPolicy ?? new BackupRetentionPolicy();

        var defaultStorageProfile = Configuration.ReadWithSource(
            cfg,
            BackupItems.DefaultStorageProfile.Key,
            defaults.DefaultStorageProfile ?? "");

        var defaultBatchSize = Configuration.ReadWithSource(
            cfg,
            BackupItems.DefaultBatchSize.Key,
            defaults.DefaultBatchSize);

        var warmupOnStartup = Configuration.ReadWithSource(
            cfg,
            BackupItems.WarmupEntitiesOnStartup.Key,
            defaults.WarmupEntitiesOnStartup);

        var enableMaintenance = Configuration.ReadWithSource(
            cfg,
            BackupItems.EnableBackgroundMaintenance.Key,
            defaults.EnableBackgroundMaintenance);

        var maintenanceInterval = Configuration.ReadWithSource(
            cfg,
            BackupItems.MaintenanceInterval.Key,
            defaults.MaintenanceInterval);

        var maxConcurrency = Configuration.ReadWithSource(
            cfg,
            BackupItems.MaxConcurrency.Key,
            defaults.MaxConcurrency);

        var autoValidateBackups = Configuration.ReadWithSource(
            cfg,
            BackupItems.AutoValidateBackups.Key,
            defaults.AutoValidateBackups);

        var compressionLevel = Configuration.ReadWithSource(
            cfg,
            BackupItems.CompressionLevel.Key,
            defaults.CompressionLevel);

        var keepDaily = Configuration.ReadWithSource(
            cfg,
            BackupItems.KeepDaily.Key,
            retentionDefaults.KeepDaily);

        var keepWeekly = Configuration.ReadWithSource(
            cfg,
            BackupItems.KeepWeekly.Key,
            retentionDefaults.KeepWeekly);

        var keepMonthly = Configuration.ReadWithSource(
            cfg,
            BackupItems.KeepMonthly.Key,
            retentionDefaults.KeepMonthly);

        var keepYearly = Configuration.ReadWithSource(
            cfg,
            BackupItems.KeepYearly.Key,
            retentionDefaults.KeepYearly);

        var excludeFromCleanup = ReadStringArray(
            cfg,
            BackupItems.ExcludeFromCleanup.Key,
            retentionDefaults.ExcludeFromCleanup);

        Publish(module, BackupItems.DefaultStorageProfile, defaultStorageProfile, ResolveDefaultProfileDisplay(defaultStorageProfile.Value));
        Publish(module, BackupItems.DefaultBatchSize, defaultBatchSize);
        Publish(module, BackupItems.WarmupEntitiesOnStartup, warmupOnStartup);
        Publish(module, BackupItems.EnableBackgroundMaintenance, enableMaintenance);
        Publish(module, BackupItems.MaintenanceInterval, maintenanceInterval);
        Publish(module, BackupItems.MaxConcurrency, maxConcurrency);
        Publish(module, BackupItems.AutoValidateBackups, autoValidateBackups);
        Publish(module, BackupItems.CompressionLevel, compressionLevel);
        Publish(module, BackupItems.KeepDaily, keepDaily);
        Publish(module, BackupItems.KeepWeekly, keepWeekly);
        Publish(module, BackupItems.KeepMonthly, keepMonthly);
        Publish(module, BackupItems.KeepYearly, keepYearly);
        Publish(module, BackupItems.ExcludeFromCleanup, excludeFromCleanup);

        // Backup capabilities
        module.AddNote("Capabilities: auto entity discovery, streaming backup, multi-provider support, zip compression, JSON lines format, integrity validation, schema snapshots, backup discovery, progress tracking, attribute-based opt-in, policy management.");
    }

    private static void Publish<T>(Koan.Core.Provenance.ProvenanceModuleWriter module, ProvenanceItem item, Koan.Core.ConfigurationValue<T> value, object? displayOverride = null, bool? sanitizeOverride = null)
    {
        module.AddSetting(
            item,
            ProvenanceModes.FromConfigurationValue(value),
            displayOverride ?? value.Value,
            sourceKey: value.ResolvedKey,
            usedDefault: value.UsedDefault,
            sanitizeOverride: sanitizeOverride);
    }

    private static void Publish(Koan.Core.Provenance.ProvenanceModuleWriter module, ProvenanceItem item, StringArrayValue value)
    {
        module.AddSetting(
            item,
            ProvenanceModes.FromBootSource(value.Source, value.UsedDefault),
            value.Display,
            sourceKey: value.SourceKey,
            usedDefault: value.UsedDefault,
            sanitizeOverride: false);
    }

    private static string ResolveDefaultProfileDisplay(string? value)
        => string.IsNullOrWhiteSpace(value) ? "(auto)" : value;

    private static StringArrayValue ReadStringArray(IConfiguration? cfg, string baseKey, IReadOnlyList<string> defaults)
    {
        if (cfg is null)
        {
            return new StringArrayValue(FormatList(defaults), BootSettingSource.Auto, baseKey, true);
        }

        var section = cfg.GetSection(baseKey);
        var children = section.GetChildren().OrderBy(c => c.Key, StringComparer.Ordinal).ToList();

        if (children.Count == 0)
        {
            return new StringArrayValue(FormatList(defaults), BootSettingSource.Auto, baseKey, true);
        }

        var resolved = new List<ConfigurationValue<string?>>();

        foreach (var child in children)
        {
            var option = Configuration.ReadWithSource(cfg, $"{baseKey}:{child.Key}", default(string?));
            if (!option.UsedDefault)
            {
                resolved.Add(option);
            }
        }

        if (resolved.Count == 0)
        {
            return new StringArrayValue(FormatList(defaults), BootSettingSource.Auto, baseKey, true);
        }

        var displayValues = resolved
            .Select(v => (v.Value ?? "").Trim())
            .Where(v => !string.IsNullOrEmpty(v))
            .ToArray();

        var display = displayValues.Length > 0 ? string.Join(", ", displayValues) : "(none)";
        var source = resolved.Any(v => v.Source == BootSettingSource.Environment)
            ? BootSettingSource.Environment
            : resolved[0].Source;
        var sourceKey = resolved.First(v => v.Source == source).ResolvedKey;

        return new StringArrayValue(display, source, sourceKey, false);
    }

    private static string FormatList(IReadOnlyCollection<string> items)
    {
        if (items is null || items.Count == 0)
        {
            return "(none)";
        }

        var trimmed = items
            .Select(i => (i ?? "").Trim())
            .Where(i => i.Length > 0)
            .ToArray();

        return trimmed.Length == 0 ? "(none)" : string.Join(", ", trimmed);
    }

    private static class BackupConsumersCache
    {
        public static readonly IReadOnlyCollection<string> OptionConsumers = new[]
        {
            "Koan.Data.Backup.Core.StreamingBackupService",
            "Koan.Data.Backup.Core.OptimizedRestoreService",
            "Koan.Data.Backup.Services.BackupMaintenanceService"
        };

        public static readonly IReadOnlyCollection<string> MaintenanceConsumers = new[]
        {
            "Koan.Data.Backup.Services.BackupMaintenanceService",
            "Koan.Data.Backup.Core.BackupDiscoveryService"
        };
    }

    private readonly record struct StringArrayValue(string Display, BootSettingSource Source, string? SourceKey, bool UsedDefault);
}
