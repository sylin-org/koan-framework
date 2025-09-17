using Koan.Web.Backup.Hubs;
using Koan.Web.Backup.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Koan.Web.Backup.Extensions;

/// <summary>
/// Extension methods for registering Koan Web Backup services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Koan Web Backup services to the DI container
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddKoanWebBackup(this IServiceCollection services)
    {
        // Operation tracking service
        services.TryAddSingleton<IBackupOperationTracker, InMemoryBackupOperationTracker>();

        // Progress notification service (polling-based, no SignalR)
        services.TryAddScoped<IBackupProgressNotifier, PollingBackupProgressNotifier>();

        // Add controllers
        services.AddControllers()
            .AddNewtonsoftJson(options =>
            {
                options.SerializerSettings.DateTimeZoneHandling = Newtonsoft.Json.DateTimeZoneHandling.Utc;
                options.SerializerSettings.NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore;
            });

        // API versioning removed for simplicity - backup APIs are stable and don't require versioning

        // Add CORS support
        services.AddCors(options =>
        {
            options.AddDefaultPolicy(builder =>
            {
                builder
                    .AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader();
            });
        });

        return services;
    }

    /// <summary>
    /// Adds enhanced operation tracking with persistent storage
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddKoanWebBackupWithPersistentTracking(this IServiceCollection services)
    {
        // Add base services first
        services.AddKoanWebBackup();

        // TODO: Replace in-memory tracker with persistent implementation
        // services.Replace(ServiceDescriptor.Singleton<IBackupOperationTracker, PersistentBackupOperationTracker>());

        return services;
    }

    /// <summary>
    /// Adds enhanced progress notifications with additional channels
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddKoanWebBackupWithEnhancedNotifications(this IServiceCollection services)
    {
        // Add base services first
        services.AddKoanWebBackup();

        // TODO: Add additional notification channels (email, webhooks, etc.)
        // services.TryAddScoped<IEmailNotificationService, EmailNotificationService>();
        // services.TryAddScoped<IWebhookNotificationService, WebhookNotificationService>();

        return services;
    }

    /// <summary>
    /// Configure background services for cleanup and monitoring
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="cleanupInterval">Interval for cleaning up completed operations</param>
    /// <param name="maxOperationAge">Maximum age for completed operations before cleanup</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddKoanWebBackupBackgroundServices(
        this IServiceCollection services,
        TimeSpan? cleanupInterval = null,
        TimeSpan? maxOperationAge = null)
    {
        // Add background cleanup service
        services.AddHostedService<BackupOperationCleanupService>(provider =>
            new BackupOperationCleanupService(
                provider.GetRequiredService<IBackupOperationTracker>(),
                provider.GetRequiredService<ILogger<BackupOperationCleanupService>>(),
                cleanupInterval ?? TimeSpan.FromHours(1),
                maxOperationAge ?? TimeSpan.FromDays(7)
            ));

        return services;
    }
}

/// <summary>
/// Background service for cleaning up completed operations
/// </summary>
public class BackupOperationCleanupService : Microsoft.Extensions.Hosting.BackgroundService
{
    private readonly IBackupOperationTracker _operationTracker;
    private readonly ILogger<BackupOperationCleanupService> _logger;
    private readonly TimeSpan _cleanupInterval;
    private readonly TimeSpan _maxOperationAge;

    public BackupOperationCleanupService(
        IBackupOperationTracker operationTracker,
        ILogger<BackupOperationCleanupService> logger,
        TimeSpan cleanupInterval,
        TimeSpan maxOperationAge)
    {
        _operationTracker = operationTracker;
        _logger = logger;
        _cleanupInterval = cleanupInterval;
        _maxOperationAge = maxOperationAge;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting backup operation cleanup service with interval {Interval} and max age {MaxAge}",
            _cleanupInterval, _maxOperationAge);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_cleanupInterval, stoppingToken);

                if (stoppingToken.IsCancellationRequested)
                    break;

                await _operationTracker.CleanupCompletedOperationsAsync(_maxOperationAge);
            }
            catch (TaskCanceledException)
            {
                // Expected when service is stopping
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during operation cleanup cycle");
                // Continue running despite errors
            }
        }

        _logger.LogInformation("Backup operation cleanup service stopped");
    }
}