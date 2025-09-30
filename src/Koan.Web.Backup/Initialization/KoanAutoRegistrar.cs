using Koan.Core;
using Koan.Core.Logging;
using Koan.Web.Backup.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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

        Log.BootDebug(LogActions.Init, "services-registered", ("module", ModuleName));
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);

        // Add web backup capabilities
        report.AddSetting("Capability:BackupWebAPI", "true");
        report.AddSetting("Capability:RestoreWebAPI", "true");
        report.AddSetting("Capability:PollingProgressTracking", "true");
        report.AddSetting("Capability:OperationManagement", "true");
        report.AddSetting("Capability:BackupCatalogAPI", "true");
        report.AddSetting("Capability:BackupVerificationAPI", "true");
        report.AddSetting("Capability:SystemStatusAPI", "true");
        report.AddSetting("Capability:CORSSupport", "true");
        report.AddSetting("Capability:APIVersioning", "true");
        report.AddSetting("Capability:BackgroundCleanup", "true");

        // Add architecture info
        report.AddSetting("ProgressTracking", "Polling-based (REST endpoints)");
        report.AddSetting("SignalRSupport", "false");
        report.AddSetting("PollingInterval", "Client-controlled");
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