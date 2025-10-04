using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Koan.Core;
using Koan.Core.Logging;
using Koan.Data.Backup.Abstractions;
using Koan.Data.Backup.Extensions;
using Koan.Data.Backup.Models;

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

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);

        // Add backup-specific configuration information
        var options = new BackupRestoreOptions();
        cfg.GetSection("Koan:Backup").Bind(options);

        report.AddSetting("DefaultStorageProfile", options.DefaultStorageProfile ?? "(default)");
        report.AddSetting("DefaultBatchSize", options.DefaultBatchSize.ToString());
        report.AddSetting("WarmupEntitiesOnStartup", options.WarmupEntitiesOnStartup.ToString());
        report.AddSetting("EnableBackgroundMaintenance", options.EnableBackgroundMaintenance.ToString());

        if (options.EnableBackgroundMaintenance)
        {
            report.AddSetting("MaintenanceInterval", options.MaintenanceInterval.ToString());
        }

        // Backup capabilities
        report.AddSetting("Capability:AutoEntityDiscovery", "true");
        report.AddSetting("Capability:MultiProviderSupport", "true");
        report.AddSetting("Capability:StreamingBackup", "true");
        report.AddSetting("Capability:ZipCompression", "true");
        report.AddSetting("Capability:JsonLinesFormat", "true");
        report.AddSetting("Capability:IntegrityValidation", "true");
        report.AddSetting("Capability:SchemaSnapshots", "true");
        report.AddSetting("Capability:BackupDiscovery", "true");
        report.AddSetting("Capability:ProgressTracking", "true");
        report.AddSetting("Capability:AttributeBasedOptIn", "true");
        report.AddSetting("Capability:PolicyManagement", "true");
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
}