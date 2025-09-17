using Microsoft.Extensions.Logging;
using Koan.Web.Backup.Models;

namespace Koan.Web.Backup.Hubs;

// NOTE: SignalR hub removed - replaced with polling-based architecture
// Clients should poll the REST endpoints for operation status:
// - GET /api/backup/operations/{operationId}
// - GET /api/restore/operations/{operationId}

/// <summary>
/// Service for progress notifications - replaced SignalR with polling-based architecture.
/// All notifications are now available via REST endpoints instead of push notifications.
/// </summary>
public interface IBackupProgressNotifier
{
    /// <summary>
    /// Log backup progress update (no-op - clients poll for updates)
    /// </summary>
    Task NotifyBackupProgressAsync(string operationId, BackupProgressInfo progress);

    /// <summary>
    /// Log restore progress update (no-op - clients poll for updates)
    /// </summary>
    Task NotifyRestoreProgressAsync(string operationId, RestoreProgressInfo progress);

    /// <summary>
    /// Log backup operation completion (no-op - clients poll for updates)
    /// </summary>
    Task NotifyBackupCompletedAsync(string operationId, Data.Backup.Models.BackupManifest result);

    /// <summary>
    /// Log restore operation completion (no-op - clients poll for updates)
    /// </summary>
    Task NotifyRestoreCompletedAsync(string operationId, RestoreResult result);

    /// <summary>
    /// Log operation failure (no-op - clients poll for updates)
    /// </summary>
    Task NotifyOperationFailedAsync(string operationId, string errorMessage);

    /// <summary>
    /// Log operation cancellation (no-op - clients poll for updates)
    /// </summary>
    Task NotifyOperationCancelledAsync(string operationId);

    /// <summary>
    /// Log system status update (no-op - clients poll for updates)
    /// </summary>
    Task NotifySystemStatusAsync(BackupSystemStatusResponse status);
}

/// <summary>
/// No-op implementation of backup progress notifier for polling-based architecture.
/// All operation state is tracked in IBackupOperationTracker and available via REST endpoints.
/// </summary>
public class PollingBackupProgressNotifier : IBackupProgressNotifier
{
    private readonly ILogger<PollingBackupProgressNotifier> _logger;

    public PollingBackupProgressNotifier(ILogger<PollingBackupProgressNotifier> logger)
    {
        _logger = logger;
    }

    public Task NotifyBackupProgressAsync(string operationId, BackupProgressInfo progress)
    {
        _logger.LogDebug("Backup progress for operation {OperationId}: {Percent:F1}% complete, {Stage} (poll /api/backup/operations/{OperationId} for status)",
            operationId, progress.PercentComplete, progress.CurrentStage, operationId);
        return Task.CompletedTask;
    }

    public Task NotifyRestoreProgressAsync(string operationId, RestoreProgressInfo progress)
    {
        _logger.LogDebug("Restore progress for operation {OperationId}: {Percent:F1}% complete, {Stage} (poll /api/restore/operations/{OperationId} for status)",
            operationId, progress.PercentComplete, progress.CurrentStage, operationId);
        return Task.CompletedTask;
    }

    public Task NotifyBackupCompletedAsync(string operationId, Data.Backup.Models.BackupManifest result)
    {
        _logger.LogInformation("Backup completed for operation {OperationId} (poll /api/backup/operations/{OperationId} for full results)",
            operationId, operationId);
        return Task.CompletedTask;
    }

    public Task NotifyRestoreCompletedAsync(string operationId, RestoreResult result)
    {
        _logger.LogInformation("Restore completed for operation {OperationId} (poll /api/restore/operations/{OperationId} for full results)",
            operationId, operationId);
        return Task.CompletedTask;
    }

    public Task NotifyOperationFailedAsync(string operationId, string errorMessage)
    {
        _logger.LogWarning("Operation {OperationId} failed: {ErrorMessage} (poll operation endpoint for details)",
            operationId, errorMessage);
        return Task.CompletedTask;
    }

    public Task NotifyOperationCancelledAsync(string operationId)
    {
        _logger.LogInformation("Operation {OperationId} cancelled (poll operation endpoint for confirmation)",
            operationId);
        return Task.CompletedTask;
    }

    public Task NotifySystemStatusAsync(BackupSystemStatusResponse status)
    {
        _logger.LogDebug("System status update: {Status} (poll /api/backup/status for current status)",
            status.Status);
        return Task.CompletedTask;
    }
}