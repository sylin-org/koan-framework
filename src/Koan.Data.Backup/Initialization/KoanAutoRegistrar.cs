using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Koan.Core;
using Koan.Core.Logging;
using Koan.Data.Backup.Abstractions;
using Koan.Data.Backup.Extensions;
using Koan.Data.Backup.Infrastructure;
using Koan.Data.Backup.Models;
using Koan.Core.Hosting.Bootstrap;
using BackupItems = Koan.Data.Backup.Infrastructure.BackupProvenanceItems;
using ProvenanceModes = Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions;

namespace Koan.Data.Backup.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    private static readonly KoanLog.KoanLogScope Log = KoanLog.For<KoanAutoRegistrar>();
    private static BackupInventory? _cachedInventory;

    public string ModuleName => "Koan.Data.Backup";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        Log.BootDebug(LogActions.Init, "loaded", ("module", ModuleName));

        // Register backup and restore services automatically
        services.AddKoanBackupRestore();

        Log.BootDebug(LogActions.Init, "services-registered", ("module", ModuleName));
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);

        var defaults = new BackupRestoreOptions();
        var retentionDefaults = defaults.RetentionPolicy ?? new BackupRetentionPolicy();

        var defaultStorageProfile = Configuration.ReadWithSource(
            cfg,
            BackupItems.DefaultStorageProfile.Key,
            defaults.DefaultStorageProfile ?? string.Empty);

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

        module.AddSetting(
            BackupItems.DefaultStorageProfile,
            ProvenanceModes.FromConfigurationValue(defaultStorageProfile),
            string.IsNullOrWhiteSpace(defaultStorageProfile.Value) ? "(auto)" : defaultStorageProfile.Value,
            sourceKey: defaultStorageProfile.ResolvedKey,
            usedDefault: defaultStorageProfile.UsedDefault);

        module.AddSetting(
            BackupItems.DefaultBatchSize,
            ProvenanceModes.FromConfigurationValue(defaultBatchSize),
            defaultBatchSize.Value,
            sourceKey: defaultBatchSize.ResolvedKey,
            usedDefault: defaultBatchSize.UsedDefault);

        module.AddSetting(
            BackupItems.WarmupEntitiesOnStartup,
            ProvenanceModes.FromConfigurationValue(warmupOnStartup),
            warmupOnStartup.Value,
            sourceKey: warmupOnStartup.ResolvedKey,
            usedDefault: warmupOnStartup.UsedDefault);

        module.AddSetting(
            BackupItems.EnableBackgroundMaintenance,
            ProvenanceModes.FromConfigurationValue(enableMaintenance),
            enableMaintenance.Value,
            sourceKey: enableMaintenance.ResolvedKey,
            usedDefault: enableMaintenance.UsedDefault);

        module.AddSetting(
            BackupItems.MaintenanceInterval,
            ProvenanceModes.FromConfigurationValue(maintenanceInterval),
            maintenanceInterval.Value,
            sourceKey: maintenanceInterval.ResolvedKey,
            usedDefault: maintenanceInterval.UsedDefault);

        module.AddSetting(
            BackupItems.MaxConcurrency,
            ProvenanceModes.FromConfigurationValue(maxConcurrency),
            maxConcurrency.Value,
            sourceKey: maxConcurrency.ResolvedKey,
            usedDefault: maxConcurrency.UsedDefault);

        module.AddSetting(
            BackupItems.AutoValidateBackups,
            ProvenanceModes.FromConfigurationValue(autoValidateBackups),
            autoValidateBackups.Value,
            sourceKey: autoValidateBackups.ResolvedKey,
            usedDefault: autoValidateBackups.UsedDefault);

        module.AddSetting(
            BackupItems.CompressionLevel,
            ProvenanceModes.FromConfigurationValue(compressionLevel),
            compressionLevel.Value,
            sourceKey: compressionLevel.ResolvedKey,
            usedDefault: compressionLevel.UsedDefault);

        module.AddSetting(
            BackupItems.KeepDaily,
            ProvenanceModes.FromConfigurationValue(keepDaily),
            keepDaily.Value,
            sourceKey: keepDaily.ResolvedKey,
            usedDefault: keepDaily.UsedDefault);

        module.AddSetting(
            BackupItems.KeepWeekly,
            ProvenanceModes.FromConfigurationValue(keepWeekly),
            keepWeekly.Value,
            sourceKey: keepWeekly.ResolvedKey,
            usedDefault: keepWeekly.UsedDefault);

        module.AddSetting(
            BackupItems.KeepMonthly,
            ProvenanceModes.FromConfigurationValue(keepMonthly),
            keepMonthly.Value,
            sourceKey: keepMonthly.ResolvedKey,
            usedDefault: keepMonthly.UsedDefault);

        module.AddSetting(
            BackupItems.KeepYearly,
            ProvenanceModes.FromConfigurationValue(keepYearly),
            keepYearly.Value,
            sourceKey: keepYearly.ResolvedKey,
            usedDefault: keepYearly.UsedDefault);

        module.AddSetting(
            BackupItems.ExcludeFromCleanup,
            ProvenanceModes.FromBootSource(excludeFromCleanup.Source, excludeFromCleanup.UsedDefault),
            excludeFromCleanup.Display,
            sourceKey: excludeFromCleanup.SourceKey,
            usedDefault: excludeFromCleanup.UsedDefault,
            sanitizeOverride: false);

        // Backup capabilities
        module.AddNote("Capabilities: auto entity discovery, streaming backup, multi-provider support, zip compression, JSON lines format, integrity validation, schema snapshots, backup discovery, progress tracking, attribute-based opt-in, policy management.");
    }

    /// <summary>
    /// Validates backup inventory after services are built (called by framework post-startup).
    /// </summary>
    /// <remarks>
    /// This method is called after all services are registered and the service provider is built.
    /// It builds the backup inventory and emits warnings for uncovered entities.
    /// </remarks>
    public static async Task ValidateBackupInventoryAsync(IServiceProvider services, ILogger logger)
    {
        try
        {
            var discoveryService = services.GetService<IEntityDiscoveryService>();
            if (discoveryService == null)
            {
                logger.LogWarning("Entity discovery service not available - skipping backup inventory validation");
                return;
            }

            Log.BootDebug(LogActions.Inventory, "building", ("module", "Koan.Data.Backup"));

            var inventory = await discoveryService.BuildInventoryAsync();
            _cachedInventory = inventory;

            // Emit inventory summary
            Log.BootInfo(LogActions.Inventory, "validation",
                ("included", inventory.TotalIncludedEntities.ToString()),
                ("excluded", inventory.TotalExcludedEntities.ToString()),
                ("warnings", inventory.TotalWarnings.ToString()));

            // Log included entities
            if (inventory.IncludedEntities.Any())
            {
                foreach (var policy in inventory.IncludedEntities.Take(10)) // Limit to first 10 in boot log
                {
                    Log.BootDebug(LogActions.Inventory, "included",
                        ("entity", policy.EntityName),
                        ("encrypt", policy.Encrypt.ToString().ToLowerInvariant()),
                        ("schema", policy.IncludeSchema.ToString().ToLowerInvariant()),
                        ("source", policy.Source.ToLowerInvariant()));
                }

                if (inventory.IncludedEntities.Count > 10)
                {
                    Log.BootDebug(LogActions.Inventory, "included-summary",
                        ("additional", (inventory.IncludedEntities.Count - 10).ToString()));
                }
            }

            // Log excluded entities
            if (inventory.ExcludedEntities.Any())
            {
                foreach (var policy in inventory.ExcludedEntities)
                {
                    Log.BootDebug(LogActions.Inventory, "excluded",
                        ("entity", policy.EntityName),
                        ("reason", policy.Reason ?? "no reason provided"));
                }
            }

            // Emit warnings
            if (inventory.HasWarnings)
            {
                Log.BootWarning(LogActions.Inventory, "uncovered",
                    ("count", inventory.TotalWarnings.ToString()));

                foreach (var warning in inventory.Warnings)
                {
                    logger.LogWarning("[Koan:backup:inventory] {Warning}", warning);
                }
            }
            else
            {
                Log.BootInfo(LogActions.Inventory, "healthy",
                    ("status", "all entities have backup coverage"));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to validate backup inventory during startup");
        }
    }

    /// <summary>
    /// Gets the cached backup inventory (populated during startup validation).
    /// </summary>
    public static BackupInventory? GetCachedInventory() => _cachedInventory;

    private static class LogActions
    {
        public const string Init = "registrar.init";
        public const string Inventory = "backup.inventory";
    }

    private static string BoolString(bool value) => value ? "true" : "false";

    private static IReadOnlyCollection<string> BackupOptionConsumers() => BackupConsumersCache.OptionConsumers;

    private static IReadOnlyCollection<string> BackupMaintenanceConsumers() => BackupConsumersCache.MaintenanceConsumers;

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
            .Select(v => (v.Value ?? string.Empty).Trim())
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
            .Select(i => (i ?? string.Empty).Trim())
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
