using System.Collections.Generic;
using Koan.Core.Hosting.Bootstrap;

namespace Koan.Data.Backup.Infrastructure;

internal static class BackupProvenanceItems
{
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
        DefaultConsumers: OptionConsumers);

    internal static readonly ProvenanceItem DefaultBatchSize = new(
        "Koan:Backup:DefaultBatchSize",
        "Default Batch Size",
        "Number of records processed per batch when creating backups.",
        DefaultConsumers: OptionConsumers);

    internal static readonly ProvenanceItem WarmupEntitiesOnStartup = new(
        "Koan:Backup:WarmupEntitiesOnStartup",
        "Warmup Entities On Startup",
        "Warms up entity metadata so backup discovery has cached models on process start.",
        DefaultConsumers: MaintenanceConsumers);

    internal static readonly ProvenanceItem EnableBackgroundMaintenance = new(
        "Koan:Backup:EnableBackgroundMaintenance",
        "Enable Background Maintenance",
        "Enables automatic cleanup and validation background tasks for backups.",
        DefaultConsumers: MaintenanceConsumers);

    internal static readonly ProvenanceItem MaintenanceInterval = new(
        "Koan:Backup:MaintenanceInterval",
        "Maintenance Interval",
        "Refresh interval for background maintenance tasks (ISO 8601 duration).",
        DefaultConsumers: MaintenanceConsumers);

    internal static readonly ProvenanceItem MaxConcurrency = new(
        "Koan:Backup:MaxConcurrency",
        "Max Concurrency",
        "Maximum number of backup operations allowed to run in parallel.",
        DefaultConsumers: OptionConsumers);

    internal static readonly ProvenanceItem AutoValidateBackups = new(
        "Koan:Backup:AutoValidateBackups",
        "Auto-Validate Backups",
        "Automatically verifies backup integrity after completion.",
        DefaultConsumers: MaintenanceConsumers);

    internal static readonly ProvenanceItem CompressionLevel = new(
        "Koan:Backup:CompressionLevel",
        "Compression Level",
        "ZIP compression level applied to backup archives.",
        DefaultConsumers: OptionConsumers);

    internal static readonly ProvenanceItem KeepDaily = new(
        "Koan:Backup:Retention:KeepDaily",
        "Retention: Keep Daily",
        "Number of daily backups retained before cleanup.",
        DefaultConsumers: MaintenanceConsumers);

    internal static readonly ProvenanceItem KeepWeekly = new(
        "Koan:Backup:Retention:KeepWeekly",
        "Retention: Keep Weekly",
        "Number of weekly backups retained before cleanup.",
        DefaultConsumers: MaintenanceConsumers);

    internal static readonly ProvenanceItem KeepMonthly = new(
        "Koan:Backup:Retention:KeepMonthly",
        "Retention: Keep Monthly",
        "Number of monthly backups retained before cleanup.",
        DefaultConsumers: MaintenanceConsumers);

    internal static readonly ProvenanceItem KeepYearly = new(
        "Koan:Backup:Retention:KeepYearly",
        "Retention: Keep Yearly",
        "Number of yearly backups retained before cleanup.",
        DefaultConsumers: MaintenanceConsumers);

    internal static readonly ProvenanceItem ExcludeFromCleanup = new(
        "Koan:Backup:Retention:ExcludeFromCleanup",
        "Retention: Exclude From Cleanup",
        "Comma separated list of backup names that are never removed by retention policies.",
        DefaultConsumers: MaintenanceConsumers);
}
