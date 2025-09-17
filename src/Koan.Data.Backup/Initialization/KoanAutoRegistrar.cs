using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Koan.Core;
using Koan.Data.Backup.Extensions;
using Koan.Data.Backup.Models;

namespace Koan.Data.Backup.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Data.Backup";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        var logger = services.BuildServiceProvider().GetService<ILoggerFactory>()?.CreateLogger("Koan.Data.Backup.Initialization.KoanAutoRegistrar");
        logger?.Log(LogLevel.Debug, "Koan.Data.Backup KoanAutoRegistrar loaded.");

        // Register backup and restore services automatically
        services.AddKoanBackupRestore();

        logger?.Log(LogLevel.Debug, "Koan.Data.Backup services registered successfully.");
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
    }
}