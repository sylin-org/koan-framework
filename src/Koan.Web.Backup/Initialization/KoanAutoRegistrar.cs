using Koan.Core;
using Koan.Core.Logging;
using Koan.Web.Backup.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Web.Extensions;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using WebBackupItems = Koan.Web.Backup.Infrastructure.WebBackupProvenanceItems;
using PublicationMode = Koan.Core.Hosting.Bootstrap.ProvenancePublicationMode;
using ProvenanceWriter = Koan.Core.Provenance.ProvenanceModuleWriter;

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

    public void Describe(ProvenanceWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        // Add web backup capabilities
        module.AddSetting(
            WebBackupItems.BackupWebApi,
            PublicationMode.Auto,
            true,
            usedDefault: true);
        module.AddSetting(
            WebBackupItems.RestoreWebApi,
            PublicationMode.Auto,
            true,
            usedDefault: true);
        module.AddSetting(
            WebBackupItems.PollingProgressTracking,
            PublicationMode.Auto,
            true,
            usedDefault: true);
        module.AddSetting(
            WebBackupItems.OperationManagement,
            PublicationMode.Auto,
            true,
            usedDefault: true);
        module.AddSetting(
            WebBackupItems.BackupCatalogApi,
            PublicationMode.Auto,
            true,
            usedDefault: true);
        module.AddSetting(
            WebBackupItems.BackupVerificationApi,
            PublicationMode.Auto,
            true,
            usedDefault: true);
        module.AddSetting(
            WebBackupItems.SystemStatusApi,
            PublicationMode.Auto,
            true,
            usedDefault: true);
        module.AddSetting(
            WebBackupItems.CorsSupport,
            PublicationMode.Auto,
            true,
            usedDefault: true);
        module.AddSetting(
            WebBackupItems.ApiVersioning,
            PublicationMode.Auto,
            true,
            usedDefault: true);
        module.AddSetting(
            WebBackupItems.BackgroundCleanup,
            PublicationMode.Auto,
            true,
            usedDefault: true);

        // Add architecture info
        module.AddSetting(
            WebBackupItems.ProgressTracking,
            PublicationMode.Auto,
            "Polling-based (REST endpoints)",
            usedDefault: true);
        module.AddSetting(
            WebBackupItems.SignalRSupport,
            PublicationMode.Auto,
            false,
            usedDefault: true);
        module.AddSetting(
            WebBackupItems.PollingInterval,
            PublicationMode.Auto,
            "Client-controlled",
            usedDefault: true);

        module.AddTool(
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
