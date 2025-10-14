using Koan.Core;
using Koan.Core.Logging;
using Koan.Web.Backup.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Web.Extensions;

namespace Koan.Web.Backup.Initialization;

/// <summary>
/// Auto-registrar for Koan Web Backup services
/// </summary>
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    private static readonly KoanLog.KoanLogScope Log = KoanLog.For<KoanAutoRegistrar>();

    public string ModuleName => "Koan.Web.Backup";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        Log.BootDebug(LogActions.Init, "loaded", ("module", ModuleName));

        // Register Web Backup services
        services.AddKoanWebBackup();

        // Add background cleanup services with default settings
        services.AddKoanWebBackupBackgroundServices();

        // Ensure MVC discovers controllers from this assembly
        services.AddKoanControllersFrom<Controllers.BackupController>();

        Log.BootDebug(LogActions.Init, "services-registered", ("module", ModuleName));
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);

        // Add web backup capabilities
        report.AddSetting(
            "Capability:BackupWebAPI",
            "true",
            source: Koan.Core.Hosting.Bootstrap.BootSettingSource.Auto,
            consumers: new[] { "Koan.Web.Backup.BackupController" });
        report.AddSetting(
            "Capability:RestoreWebAPI",
            "true",
            source: Koan.Core.Hosting.Bootstrap.BootSettingSource.Auto,
            consumers: new[] { "Koan.Web.Backup.RestoreController" });
        report.AddSetting(
            "Capability:PollingProgressTracking",
            "true",
            source: Koan.Core.Hosting.Bootstrap.BootSettingSource.Auto,
            consumers: new[] { "Koan.Web.Backup.Operations" });
        report.AddSetting(
            "Capability:OperationManagement",
            "true",
            source: Koan.Core.Hosting.Bootstrap.BootSettingSource.Auto,
            consumers: new[] { "Koan.Web.Backup.Operations" });
        report.AddSetting(
            "Capability:BackupCatalogAPI",
            "true",
            source: Koan.Core.Hosting.Bootstrap.BootSettingSource.Auto,
            consumers: new[] { "Koan.Web.Backup.Catalog" });
        report.AddSetting(
            "Capability:BackupVerificationAPI",
            "true",
            source: Koan.Core.Hosting.Bootstrap.BootSettingSource.Auto,
            consumers: new[] { "Koan.Web.Backup.Verification" });
        report.AddSetting(
            "Capability:SystemStatusAPI",
            "true",
            source: Koan.Core.Hosting.Bootstrap.BootSettingSource.Auto,
            consumers: new[] { "Koan.Web.Backup.Status" });
        report.AddSetting(
            "Capability:CORSSupport",
            "true",
            source: Koan.Core.Hosting.Bootstrap.BootSettingSource.Auto,
            consumers: new[] { "Koan.Web.Backup.Middleware" });
        report.AddSetting(
            "Capability:APIVersioning",
            "true",
            source: Koan.Core.Hosting.Bootstrap.BootSettingSource.Auto,
            consumers: new[] { "Koan.Web.Backup.ApiSurface" });
        report.AddSetting(
            "Capability:BackgroundCleanup",
            "true",
            source: Koan.Core.Hosting.Bootstrap.BootSettingSource.Auto,
            consumers: new[] { "Koan.Web.Backup.BackgroundServices" });

        // Add architecture info
        report.AddSetting(
            "ProgressTracking",
            "Polling-based (REST endpoints)",
            source: Koan.Core.Hosting.Bootstrap.BootSettingSource.Auto,
            consumers: new[] { "Koan.Web.Backup.Progress" });
        report.AddSetting(
            "SignalRSupport",
            "false",
            source: Koan.Core.Hosting.Bootstrap.BootSettingSource.Auto,
            consumers: new[] { "Koan.Web.Backup.Progress" });
        report.AddSetting(
            "PollingInterval",
            "Client-controlled",
            source: Koan.Core.Hosting.Bootstrap.BootSettingSource.Auto,
            consumers: new[] { "Koan.Web.Backup.Progress" });

        report.AddTool(
            "Backup Operations API",
            "/api/backup",
            "Manage backup and restore operations",
            capability: "backup.operations");
    }

    private static class LogActions
    {
        public const string Init = "registrar.init";
    }
}

/// <summary>
/// Extension methods for configuring Koan Web Backup in the application pipeline
/// </summary>
public static class WebApplicationExtensions
{
    /// <summary>
    /// Configure Koan Web Backup middleware and endpoints
    /// </summary>
    /// <param name="app">Web application builder</param>
    /// <param name="basePath">Base path for backup API endpoints (default: "/api")</param>
    /// <returns>Web application for chaining</returns>
    public static WebApplication UseKoanWebBackup(this WebApplication app, string basePath = "/api")
    {
        // Enable CORS
        app.UseCors();

        // Controllers are automatically mapped by MapControllers()
        // No additional configuration needed as they use [ApiController] and [Route] attributes
        // Backup progress is available via polling REST endpoints:

        return app;
    }

    /// <summary>
    /// Configure Koan Web Backup with development-specific settings
    /// </summary>
    /// <param name="app">Web application builder</param>
    /// <param name="basePath">Base path for backup API endpoints (default: "/api")</param>
    /// <returns>Web application for chaining</returns>
    public static WebApplication UseKoanWebBackupDevelopment(this WebApplication app, string basePath = "/api")
    {
        if (app.Environment.IsDevelopment())
        {
            // Enable development-specific CORS and backup endpoints
            app.UseKoanWebBackup(basePath);

            // Add development-specific CORS policy
            app.UseCors(builder =>
            {
                builder
                    .AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .WithExposedHeaders("X-Operation-Id", "X-Backup-Status");
            });
        }
        else
        {
            app.UseKoanWebBackup(basePath);
        }

        return app;
    }

    /// <summary>
    /// Configure Koan Web Backup with production-specific settings
    /// </summary>
    /// <param name="app">Web application builder</param>
    /// <param name="allowedOrigins">Allowed CORS origins for production</param>
    /// <param name="basePath">Base path for backup API endpoints (default: "/api")</param>
    /// <returns>Web application for chaining</returns>
    public static WebApplication UseKoanWebBackupProduction(
        this WebApplication app,
        string[] allowedOrigins,
        string basePath = "/api")
    {
        if (!app.Environment.IsDevelopment())
        {
            // Production CORS policy
            app.UseCors(builder =>
            {
                builder
                    .WithOrigins(allowedOrigins)
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials()
                    .WithExposedHeaders("X-Operation-Id", "X-Backup-Status");
            });
        }

        app.UseKoanWebBackup(basePath);

        return app;
    }
}