using Koan.Data.Backup.Abstractions;
using Koan.Data.Backup.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Data.Backup.Services;

public class BackupMaintenanceService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BackupMaintenanceService> _logger;
    private readonly BackupRestoreOptions _options;

    public BackupMaintenanceService(
        IServiceProvider serviceProvider,
        ILogger<BackupMaintenanceService> logger,
        IOptions<BackupRestoreOptions> options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Backup maintenance service started");

        // Initial warmup if enabled
        if (_options.WarmupEntitiesOnStartup)
        {
            await WarmupEntitiesAsync(stoppingToken);
        }

        // Main maintenance loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.MaintenanceInterval, stoppingToken);

                if (stoppingToken.IsCancellationRequested)
                    break;

                using var scope = _serviceProvider.CreateScope();
                await PerformMaintenanceTasksAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // Expected when service is stopping
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during backup maintenance cycle");
                // Continue running despite errors
            }
        }

        _logger.LogInformation("Backup maintenance service stopped");
    }

    private async Task WarmupEntitiesAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting entity discovery warmup");

            using var scope = _serviceProvider.CreateScope();
            var discoveryService = scope.ServiceProvider.GetRequiredService<IEntityDiscoveryService>();

            await discoveryService.WarmupAllEntitiesAsync(cancellationToken);

            _logger.LogInformation("Entity discovery warmup completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Entity discovery warmup failed");
        }
    }

    private async Task PerformMaintenanceTasksAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var tasks = new List<Task>();

        // Background entity discovery
        if (_options.EnableBackgroundMaintenance)
        {
            tasks.Add(RefreshEntityDiscoveryAsync(serviceProvider, cancellationToken));
        }

        // Backup catalog refresh
        tasks.Add(RefreshBackupCatalogAsync(serviceProvider, cancellationToken));

        // Backup validation (sample)
        tasks.Add(ValidateBackupsAsync(serviceProvider, cancellationToken));

        // Cleanup old backups
        tasks.Add(CleanupOldBackupsAsync(serviceProvider, cancellationToken));

        await Task.WhenAll(tasks);
    }

    private async Task RefreshEntityDiscoveryAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        try
        {
            var discoveryService = serviceProvider.GetRequiredService<IEntityDiscoveryService>();
            await discoveryService.RefreshDiscoveryAsync(cancellationToken);
            _logger.LogDebug("Entity discovery cache refreshed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh entity discovery cache");
        }
    }

    private async Task RefreshBackupCatalogAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        try
        {
            var backupDiscoveryService = serviceProvider.GetRequiredService<IBackupDiscoveryService>();
            await backupDiscoveryService.RefreshCatalogAsync(cancellationToken);
            _logger.LogDebug("Backup catalog refreshed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh backup catalog");
        }
    }

    private async Task ValidateBackupsAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        try
        {
            var backupDiscoveryService = serviceProvider.GetRequiredService<IBackupDiscoveryService>();

            // Get a sample of backups to validate (not all, to avoid overload)
            var catalog = await backupDiscoveryService.DiscoverAllBackupsAsync(ct: cancellationToken);
            var backupsToValidate = catalog.Backups
                .Where(b => !b.LastValidatedAt.HasValue ||
                           b.LastValidatedAt.Value.AddDays(7) < DateTimeOffset.UtcNow)
                .Take(5) // Validate max 5 backups per cycle
                .ToList();

            var validationTasks = backupsToValidate.Select(async backup =>
            {
                try
                {
                    var result = await backupDiscoveryService.ValidateBackupAsync(backup.Id, cancellationToken);
                    if (!result.IsValid)
                    {
                        _logger.LogWarning("Backup {BackupId} validation failed: {Issues}",
                            backup.Id, string.Join(", ", result.Issues));
                    }
                    else
                    {
                        _logger.LogDebug("Backup {BackupId} validation succeeded", backup.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to validate backup {BackupId}", backup.Id);
                }
            });

            await Task.WhenAll(validationTasks);

            if (backupsToValidate.Any())
            {
                _logger.LogDebug("Validated {Count} backups", backupsToValidate.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Backup validation cycle failed");
        }
    }

    private async Task CleanupOldBackupsAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        try
        {
            if (_options.RetentionPolicy == null || _options.RetentionPolicy.KeepDaily <= 0)
                return; // Retention disabled

            var backupDiscoveryService = serviceProvider.GetRequiredService<IBackupDiscoveryService>();

            var cutoffDate = DateTimeOffset.UtcNow.AddDays(-_options.RetentionPolicy.KeepDaily);
            var query = new BackupQuery
            {
                DateTo = cutoffDate,
                Take = 100 // Fixed number for cleanup
            };

            var oldBackups = await backupDiscoveryService.QueryBackupsAsync(query, cancellationToken);

            if (oldBackups.Backups.Any())
            {
                _logger.LogInformation("Found {Count} backups older than {Days} days that may need cleanup",
                    oldBackups.Backups.Count, _options.RetentionPolicy.KeepDaily);

                // For now, just log the candidates. In a full implementation, you'd implement actual deletion
                // This would require additional storage service methods for deletion
                foreach (var backup in oldBackups.Backups)
                {
                    _logger.LogDebug("Cleanup candidate: {BackupName} created {CreatedAt}",
                        backup.Name, backup.CreatedAt);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Backup cleanup cycle failed");
        }
    }
}