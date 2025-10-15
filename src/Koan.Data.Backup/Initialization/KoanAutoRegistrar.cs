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
            Constants.Configuration.Keys.DefaultStorageProfile,
            defaults.DefaultStorageProfile ?? string.Empty);

        var defaultBatchSize = Configuration.ReadWithSource(
            cfg,
            Constants.Configuration.Keys.DefaultBatchSize,
            defaults.DefaultBatchSize);

        var warmupOnStartup = Configuration.ReadWithSource(
            cfg,
            Constants.Configuration.Keys.WarmupEntitiesOnStartup,
            defaults.WarmupEntitiesOnStartup);

        var enableMaintenance = Configuration.ReadWithSource(
            cfg,
            Constants.Configuration.Keys.EnableBackgroundMaintenance,
            defaults.EnableBackgroundMaintenance);

        var maintenanceInterval = Configuration.ReadWithSource(
            cfg,
            Constants.Configuration.Keys.MaintenanceInterval,
            defaults.MaintenanceInterval);

        var maxConcurrency = Configuration.ReadWithSource(
            cfg,
            Constants.Configuration.Keys.MaxConcurrency,
            defaults.MaxConcurrency);

        var autoValidateBackups = Configuration.ReadWithSource(
            cfg,
            Constants.Configuration.Keys.AutoValidateBackups,
            defaults.AutoValidateBackups);

        var compressionLevel = Configuration.ReadWithSource(
            cfg,
            Constants.Configuration.Keys.CompressionLevel,
            defaults.CompressionLevel);

        var keepDaily = Configuration.ReadWithSource(
            cfg,
            Constants.Configuration.Retention.KeepDaily,
            retentionDefaults.KeepDaily);

        var keepWeekly = Configuration.ReadWithSource(
            cfg,
            Constants.Configuration.Retention.KeepWeekly,
            retentionDefaults.KeepWeekly);

        var keepMonthly = Configuration.ReadWithSource(
            cfg,
            Constants.Configuration.Retention.KeepMonthly,
            retentionDefaults.KeepMonthly);

        var keepYearly = Configuration.ReadWithSource(
            cfg,
            Constants.Configuration.Retention.KeepYearly,
            retentionDefaults.KeepYearly);

        var excludeFromCleanup = ReadStringArray(
            cfg,
            Constants.Configuration.Retention.ExcludeFromCleanup,
            retentionDefaults.ExcludeFromCleanup);

        module.AddSetting(
            "DefaultStorageProfile",
            string.IsNullOrWhiteSpace(defaultStorageProfile.Value) ? "(default)" : defaultStorageProfile.Value,
            source: defaultStorageProfile.Source,
            sourceKey: defaultStorageProfile.ResolvedKey,
            consumers: BackupOptionConsumers());

        module.AddSetting(
            "DefaultBatchSize",
            defaultBatchSize.Value.ToString(),
            source: defaultBatchSize.Source,
            sourceKey: defaultBatchSize.ResolvedKey,
            consumers: BackupOptionConsumers());

        module.AddSetting(
            "WarmupEntitiesOnStartup",
            BoolString(warmupOnStartup.Value),
            source: warmupOnStartup.Source,
            sourceKey: warmupOnStartup.ResolvedKey,
            consumers: BackupMaintenanceConsumers());

        module.AddSetting(
            "EnableBackgroundMaintenance",
            BoolString(enableMaintenance.Value),
            source: enableMaintenance.Source,
            sourceKey: enableMaintenance.ResolvedKey,
            consumers: BackupMaintenanceConsumers());

        module.AddSetting(
            "MaintenanceInterval",
            maintenanceInterval.Value.ToString(),
            source: maintenanceInterval.Source,
            sourceKey: maintenanceInterval.ResolvedKey,
            consumers: BackupMaintenanceConsumers());

        module.AddSetting(
            "MaxConcurrency",
            maxConcurrency.Value.ToString(),
            source: maxConcurrency.Source,
            sourceKey: maxConcurrency.ResolvedKey,
            consumers: BackupOptionConsumers());

        module.AddSetting(
            "AutoValidateBackups",
            BoolString(autoValidateBackups.Value),
            source: autoValidateBackups.Source,
            sourceKey: autoValidateBackups.ResolvedKey,
            consumers: BackupMaintenanceConsumers());

        module.AddSetting(
            "CompressionLevel",
            compressionLevel.Value.ToString(),
            source: compressionLevel.Source,
            sourceKey: compressionLevel.ResolvedKey,
            consumers: BackupOptionConsumers());

        module.AddSetting(
            "RetentionPolicy.KeepDaily",
            keepDaily.Value.ToString(),
            source: keepDaily.Source,
            sourceKey: keepDaily.ResolvedKey,
            consumers: BackupMaintenanceConsumers());

        module.AddSetting(
            "RetentionPolicy.KeepWeekly",
            keepWeekly.Value.ToString(),
            source: keepWeekly.Source,
            sourceKey: keepWeekly.ResolvedKey,
            consumers: BackupMaintenanceConsumers());

        module.AddSetting(
            "RetentionPolicy.KeepMonthly",
            keepMonthly.Value.ToString(),
            source: keepMonthly.Source,
            sourceKey: keepMonthly.ResolvedKey,
            consumers: BackupMaintenanceConsumers());

        module.AddSetting(
            "RetentionPolicy.KeepYearly",
            keepYearly.Value.ToString(),
            source: keepYearly.Source,
            sourceKey: keepYearly.ResolvedKey,
            consumers: BackupMaintenanceConsumers());

        module.AddSetting(
            "RetentionPolicy.ExcludeFromCleanup",
            excludeFromCleanup.Display,
            source: excludeFromCleanup.Source,
            sourceKey: excludeFromCleanup.SourceKey,
            consumers: BackupMaintenanceConsumers());

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
