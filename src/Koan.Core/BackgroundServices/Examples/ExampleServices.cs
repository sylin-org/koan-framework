using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Koan.Core.Observability.Health;

namespace Koan.Core.BackgroundServices.Examples;

/// <summary>
/// Example 1: Simple background service that runs continuously
/// </summary>
[KoanBackgroundService(RunInProduction = true)]
public class SystemHealthMonitor : KoanBackgroundServiceBase
{
    public SystemHealthMonitor(ILogger<SystemHealthMonitor> logger, IConfiguration configuration)
        : base(logger, configuration) { }

    public override async Task ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        using var periodicTimer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        
        while (!cancellationToken.IsCancellationRequested && await periodicTimer.WaitForNextTickAsync(cancellationToken))
        {
            Logger.LogInformation("Performing system health check...");
            // Health check logic here
            await Task.Delay(1000, cancellationToken); // Simulate work
        }
    }

    public override async Task<HealthReport> CheckAsync(CancellationToken cancellationToken = default)
    {
        // Custom health check logic
        await Task.Delay(100, cancellationToken); // Simulate health check
        return HealthReport.Healthy("System health monitoring is operational");
    }
}

/// <summary>
/// Example 2: Periodic service with automatic scheduling
/// </summary>
[KoanPeriodicService(IntervalSeconds = 3600, RunOnStartup = true)]
public class DataCleanupService : KoanPokablePeriodicServiceBase
{
    public override TimeSpan Period => TimeSpan.FromHours(1);
    public override bool RunOnStartup => true;

    public DataCleanupService(ILogger<DataCleanupService> logger, IConfiguration configuration)
        : base(logger, configuration) { }

    protected override async Task ExecutePeriodicAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Starting data cleanup...");
        
        // Simulate cleanup work
        await Task.Delay(5000, cancellationToken);
        
        Logger.LogInformation("Data cleanup completed");
    }

    [ServiceAction("check-queue")]
    public async Task CheckQueueAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Checking cleanup queue for pending work...");
        await Task.Delay(1000, cancellationToken);
    }
}

/// <summary>
/// Example 3: Fluent service with actions and events
/// </summary>
[ServiceEvent(Koan.Core.Events.KoanServiceEvents.Translation.Started, EventArgsType = typeof(TranslationEventArgs))]
[ServiceEvent(Koan.Core.Events.KoanServiceEvents.Translation.Completed, EventArgsType = typeof(TranslationEventArgs))]
[ServiceEvent(Koan.Core.Events.KoanServiceEvents.Translation.Failed, EventArgsType = typeof(TranslationErrorArgs))]
public class TranslationService : KoanFluentServiceBase
{
    public TranslationService(ILogger<TranslationService> logger, IConfiguration configuration) 
        : base(logger, configuration) { }

    public override async Task ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        // This service is primarily event-driven, so just wait
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    [ServiceAction("translate", RequiresParameters = true, ParametersType = typeof(TranslationOptions))]
    public async Task TranslateAsync(TranslationOptions options, CancellationToken cancellationToken)
    {
        Logger.LogInformation("Starting translation from {From} to {To} for file {FileId}", 
            options.From, options.To, options.FileId);
        
    await EmitEventAsync(Koan.Core.Events.KoanServiceEvents.Translation.Started, new TranslationEventArgs 
        { 
            FileId = options.FileId, 
            From = options.From, 
            To = options.To 
        });

        try
        {
            // Simulate translation work
            await Task.Delay(10000, cancellationToken);
            
            await EmitEventAsync(Koan.Core.Events.KoanServiceEvents.Translation.Completed, new TranslationEventArgs 
            { 
                FileId = options.FileId, 
                From = options.From, 
                To = options.To 
            });
            
            Logger.LogInformation("Translation completed successfully for file {FileId}", options.FileId);
        }
        catch (Exception ex)
        {
            await EmitEventAsync(Koan.Core.Events.KoanServiceEvents.Translation.Failed, new TranslationErrorArgs 
            { 
                FileId = options.FileId, 
                Error = ex.Message 
            });
            throw;
        }
}

/// <summary>
/// Example 4: Startup service that runs once during application initialization
/// </summary>
[KoanStartupService(StartupOrder = 1, TimeoutSeconds = 60)]
public class DatabaseMigrationService : KoanStartupServiceBase
{
    public override int StartupOrder => 1;

    public DatabaseMigrationService(ILogger<DatabaseMigrationService> logger, IConfiguration configuration)
        : base(logger, configuration) { }

    public override async Task ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Starting database migration...");
        
        // Simulate migration work
        await Task.Delay(5000, cancellationToken);
        
        Logger.LogInformation("Database migration completed successfully");
    }

    public override async Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        // Simulate database connectivity check
        await Task.Delay(100, cancellationToken);
        return true; // In real implementation, check actual database connectivity
    }
}

/// <summary>
/// Example 5: Notification service that responds to other services' events
/// </summary>
public class NotificationService : KoanFluentServiceBase
{
    public NotificationService(ILogger<NotificationService> logger, IConfiguration configuration)
        : base(logger, configuration) { }

    public override async Task ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        // Subscribe to translation events using the fluent API
    await KoanServices.On<TranslationService>(Koan.Core.Events.KoanServiceEvents.Translation.Completed).Do<TranslationEventArgs>(async args =>
            {
                Logger.LogInformation("Sending notification for completed translation: {FileId}", args.FileId);
                await SendCompletionNotification(args.FileId);
            })
            .On("TranslationFailed").Do<TranslationErrorArgs>(async args =>
            {
                Logger.LogWarning("Sending error notification for failed translation: {FileId} - {Error}", 
                    args.FileId, args.Error);
                await SendErrorNotification(args.FileId, args.Error);
            })
            .SubscribeAsync();

        // Keep service running
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    private async Task SendCompletionNotification(string fileId)
    {
        // Simulate sending notification
        await Task.Delay(500);
        Logger.LogInformation("Notification sent for completed file: {FileId}", fileId);
    }

    private async Task SendErrorNotification(string fileId, string error)
    {
        // Simulate sending error notification
        await Task.Delay(500);
        Logger.LogWarning("Error notification sent for file: {FileId}", fileId);
    }
}
}

// Supporting data models
public record TranslationOptions(string FileId, string From, string To);

public record TranslationEventArgs
{
    public string FileId { get; init; } = string.Empty;
    public string From { get; init; } = string.Empty;
    public string To { get; init; } = string.Empty;
}

public record TranslationErrorArgs
{
    public string FileId { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
}