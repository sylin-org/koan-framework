using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Logging;
using Koan.Data.Backup.Extensions;
using Koan.Data.Backup.Models;

namespace Koan.Data.Backup.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    private static readonly KoanLog.KoanLogScope Log = KoanLog.For<KoanAutoRegistrar>();

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
    }

    private static class LogActions
    {
        public const string Init = "registrar.init";
    }
}