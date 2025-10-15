using System.Collections.Generic;
using System.Globalization;
using Koan.Core.Hosting.Bootstrap;
using Koan.Data.Backup.Models;

namespace Koan.Data.Backup.Infrastructure;

internal static class BackupProvenanceItems
{
    private static readonly BackupRestoreOptions Defaults = new();
    private static readonly BackupRetentionPolicy RetentionDefaults = Defaults.RetentionPolicy ?? new();

    private static readonly IReadOnlyCollection<string> OptionConsumers = new[]
    {
        "Koan.Data.Backup.Core.StreamingBackupService",
        "Koan.Data.Backup.Core.OptimizedRestoreService",
        "Koan.Data.Backup.Services.BackupMaintenanceService"
    };

    private static readonly IReadOnlyCollection<string> MaintenanceConsumers = new[]
    {
        "Koan.Data.Backup.Services.BackupMaintenanceService",
        "Koan.Data.Backup.Core.BackupDiscoveryService"
    };

    internal static readonly ProvenanceItem DefaultStorageProfile = new(
        "Koan:Backup:DefaultStorageProfile",
        "Default Storage Profile",
        "Storage profile used when an entity policy does not override the target.",
        DefaultValue: DefaultStorageProfileDefault(),
        DefaultConsumers: OptionConsumers);

    internal static readonly ProvenanceItem DefaultBatchSize = new(
        "Koan:Backup:DefaultBatchSize",
        "Default Batch Size",
        "Number of records processed per batch when creating backups.",
        DefaultValue: Defaults.DefaultBatchSize.ToString(CultureInfo.InvariantCulture),
        DefaultConsumers: OptionConsumers);

    internal static readonly ProvenanceItem WarmupEntitiesOnStartup = new(
        "Koan:Backup:WarmupEntitiesOnStartup",
        "Warmup Entities On Startup",
        "Warms up entity metadata so backup discovery has cached models on process start.",
        DefaultValue: BoolString(Defaults.WarmupEntitiesOnStartup),
        DefaultConsumers: MaintenanceConsumers);

    internal static readonly ProvenanceItem EnableBackgroundMaintenance = new(
        "Koan:Backup:EnableBackgroundMaintenance",
        "Enable Background Maintenance",
        "Enables automatic cleanup and validation background tasks for backups.",
        DefaultValue: BoolString(Defaults.EnableBackgroundMaintenance),
        DefaultConsumers: MaintenanceConsumers);

    internal static readonly ProvenanceItem MaintenanceInterval = new(
        "Koan:Backup:MaintenanceInterval",
        "Maintenance Interval",
        "Refresh interval for background maintenance tasks (ISO 8601 duration).",
        DefaultValue: Defaults.MaintenanceInterval.ToString("c", CultureInfo.InvariantCulture),
        DefaultConsumers: MaintenanceConsumers);

    internal static readonly ProvenanceItem MaxConcurrency = new(
        "Koan:Backup:MaxConcurrency",
        "Max Concurrency",
        "Maximum number of backup operations allowed to run in parallel.",
        DefaultValue: Defaults.MaxConcurrency.ToString(CultureInfo.InvariantCulture),
        DefaultConsumers: OptionConsumers);

    internal static readonly ProvenanceItem AutoValidateBackups = new(
        "Koan:Backup:AutoValidateBackups",
        "Auto-Validate Backups",
        "Automatically verifies backup integrity after completion.",
        DefaultValue: BoolString(Defaults.AutoValidateBackups),
        DefaultConsumers: MaintenanceConsumers);

    internal static readonly ProvenanceItem CompressionLevel = new(
        "Koan:Backup:CompressionLevel",
        "Compression Level",
        "ZIP compression level applied to backup archives.",
        DefaultValue: Defaults.CompressionLevel.ToString(),
        DefaultConsumers: OptionConsumers);

    internal static readonly ProvenanceItem KeepDaily = new(
        "Koan:Backup:Retention:KeepDaily",
        "Retention: Keep Daily",
        "Number of daily backups retained before cleanup.",
        DefaultValue: RetentionDefaults.KeepDaily.ToString(CultureInfo.InvariantCulture),
        DefaultConsumers: MaintenanceConsumers);

    internal static readonly ProvenanceItem KeepWeekly = new(
        "Koan:Backup:Retention:KeepWeekly",
        "Retention: Keep Weekly",
        "Number of weekly backups retained before cleanup.",
        DefaultValue: RetentionDefaults.KeepWeekly.ToString(CultureInfo.InvariantCulture),
        DefaultConsumers: MaintenanceConsumers);

    internal static readonly ProvenanceItem KeepMonthly = new(
        "Koan:Backup:Retention:KeepMonthly",
        "Retention: Keep Monthly",
        "Number of monthly backups retained before cleanup.",
        DefaultValue: RetentionDefaults.KeepMonthly.ToString(CultureInfo.InvariantCulture),
        DefaultConsumers: MaintenanceConsumers);

    internal static readonly ProvenanceItem KeepYearly = new(
        "Koan:Backup:Retention:KeepYearly",
        "Retention: Keep Yearly",
        "Number of yearly backups retained before cleanup.",
        DefaultValue: RetentionDefaults.KeepYearly.ToString(CultureInfo.InvariantCulture),
        DefaultConsumers: MaintenanceConsumers);

    internal static readonly ProvenanceItem ExcludeFromCleanup = new(
        "Koan:Backup:Retention:ExcludeFromCleanup",
        "Retention: Exclude From Cleanup",
        "Comma separated list of backup names that are never removed by retention policies.",
        DefaultValue: FormatList(RetentionDefaults.ExcludeFromCleanup),
        DefaultConsumers: MaintenanceConsumers);

    private static string BoolString(bool value) => value ? "true" : "false";

    private static string DefaultStorageProfileDefault()
    {
        var value = Defaults.DefaultStorageProfile;
        return string.IsNullOrWhiteSpace(value) ? "(auto)" : value;
    }

    private static string FormatList(IReadOnlyCollection<string> items)
    {
        if (items is null || items.Count == 0)
        {
            return "(none)";
        }

        var buffer = new List<string>(items.Count);
        foreach (var item in items)
        {
            var candidate = (item ?? string.Empty).Trim();
            if (candidate.Length > 0)
            {
                buffer.Add(candidate);
            }
        }

        return buffer.Count == 0 ? "(none)" : string.Join(", ", buffer);
    }
}
