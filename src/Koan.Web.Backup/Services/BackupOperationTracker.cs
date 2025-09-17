using System.Collections.Concurrent;
using Koan.Web.Backup.Models;
using Microsoft.Extensions.Logging;

namespace Koan.Web.Backup.Services;

/// <summary>
/// Tracks long-running backup and restore operations with progress reporting
/// </summary>
public interface IBackupOperationTracker
{
    /// <summary>
    /// Start tracking a new backup operation
    /// </summary>
    Task<string> StartBackupOperationAsync(string backupName, CancellationToken cancellationToken);

    /// <summary>
    /// Start tracking a new restore operation
    /// </summary>
    Task<string> StartRestoreOperationAsync(string backupName, CancellationToken cancellationToken);

    /// <summary>
    /// Update progress for a backup operation
    /// </summary>
    Task UpdateBackupProgressAsync(string operationId, BackupProgressInfo progress);

    /// <summary>
    /// Update progress for a restore operation
    /// </summary>
    Task UpdateRestoreProgressAsync(string operationId, RestoreProgressInfo progress);

    /// <summary>
    /// Mark a backup operation as completed successfully
    /// </summary>
    Task CompleteBackupOperationAsync(string operationId, Data.Backup.Models.BackupManifest result);

    /// <summary>
    /// Mark a restore operation as completed successfully
    /// </summary>
    Task CompleteRestoreOperationAsync(string operationId, RestoreResult result);

    /// <summary>
    /// Mark an operation as failed
    /// </summary>
    Task FailOperationAsync(string operationId, string errorMessage);

    /// <summary>
    /// Cancel an operation
    /// </summary>
    Task CancelOperationAsync(string operationId);

    /// <summary>
    /// Get backup operation status
    /// </summary>
    Task<BackupOperationResponse?> GetBackupOperationAsync(string operationId);

    /// <summary>
    /// Get restore operation status
    /// </summary>
    Task<RestoreOperationResponse?> GetRestoreOperationAsync(string operationId);

    /// <summary>
    /// Get all active operations
    /// </summary>
    Task<IEnumerable<string>> GetActiveOperationsAsync();

    /// <summary>
    /// Clean up completed operations older than specified time
    /// </summary>
    Task CleanupCompletedOperationsAsync(TimeSpan olderThan);
}

/// <summary>
/// In-memory implementation of backup operation tracker
/// </summary>
public class InMemoryBackupOperationTracker : IBackupOperationTracker
{
    private readonly ConcurrentDictionary<string, BackupOperationResponse> _backupOperations = new();
    private readonly ConcurrentDictionary<string, RestoreOperationResponse> _restoreOperations = new();
    private readonly ILogger<InMemoryBackupOperationTracker> _logger;

    public InMemoryBackupOperationTracker(ILogger<InMemoryBackupOperationTracker> logger)
    {
        _logger = logger;
    }

    public Task<string> StartBackupOperationAsync(string backupName, CancellationToken cancellationToken)
    {
        var operationId = Guid.NewGuid().ToString("N");
        var operation = new BackupOperationResponse
        {
            OperationId = operationId,
            BackupName = backupName,
            Status = BackupOperationStatus.Queued,
            StartedAt = DateTimeOffset.UtcNow,
            StatusUrl = $"/api/backup/operations/{operationId}",
            CancelUrl = $"/api/backup/operations/{operationId}/cancel"
        };

        _backupOperations[operationId] = operation;
        _logger.LogInformation("Started tracking backup operation {OperationId} for backup '{BackupName}'",
            operationId, backupName);

        return Task.FromResult(operationId);
    }

    public Task<string> StartRestoreOperationAsync(string backupName, CancellationToken cancellationToken)
    {
        var operationId = Guid.NewGuid().ToString("N");
        var operation = new RestoreOperationResponse
        {
            OperationId = operationId,
            BackupName = backupName,
            Status = RestoreOperationStatus.Queued,
            StartedAt = DateTimeOffset.UtcNow,
            StatusUrl = $"/api/restore/operations/{operationId}",
            CancelUrl = $"/api/restore/operations/{operationId}/cancel"
        };

        _restoreOperations[operationId] = operation;
        _logger.LogInformation("Started tracking restore operation {OperationId} for backup '{BackupName}'",
            operationId, backupName);

        return Task.FromResult(operationId);
    }

    public Task UpdateBackupProgressAsync(string operationId, BackupProgressInfo progress)
    {
        if (_backupOperations.TryGetValue(operationId, out var operation))
        {
            operation.Status = BackupOperationStatus.Running;
            operation.Progress = progress;

            // Update estimated completion time
            if (progress.EstimatedTimeRemaining.HasValue)
            {
                operation.EstimatedCompletionAt = DateTimeOffset.UtcNow.Add(progress.EstimatedTimeRemaining.Value);
            }

            _logger.LogDebug("Updated backup operation {OperationId} progress: {Percent:F1}% complete, {Stage}",
                operationId, progress.PercentComplete, progress.CurrentStage);
        }

        return Task.CompletedTask;
    }

    public Task UpdateRestoreProgressAsync(string operationId, RestoreProgressInfo progress)
    {
        if (_restoreOperations.TryGetValue(operationId, out var operation))
        {
            operation.Status = RestoreOperationStatus.Running;
            operation.Progress = progress;

            // Update estimated completion time
            if (progress.EstimatedTimeRemaining.HasValue)
            {
                operation.EstimatedCompletionAt = DateTimeOffset.UtcNow.Add(progress.EstimatedTimeRemaining.Value);
            }

            _logger.LogDebug("Updated restore operation {OperationId} progress: {Percent:F1}% complete, {Stage}",
                operationId, progress.PercentComplete, progress.CurrentStage);
        }

        return Task.CompletedTask;
    }

    public Task CompleteBackupOperationAsync(string operationId, Data.Backup.Models.BackupManifest result)
    {
        if (_backupOperations.TryGetValue(operationId, out var operation))
        {
            operation.Status = BackupOperationStatus.Completed;
            operation.Result = result;
            operation.CancelUrl = null; // Can't cancel completed operation
            operation.EstimatedCompletionAt = DateTimeOffset.UtcNow;

            _logger.LogInformation("Completed backup operation {OperationId} for backup '{BackupName}'",
                operationId, operation.BackupName);
        }

        return Task.CompletedTask;
    }

    public Task CompleteRestoreOperationAsync(string operationId, RestoreResult result)
    {
        if (_restoreOperations.TryGetValue(operationId, out var operation))
        {
            operation.Status = RestoreOperationStatus.Completed;
            operation.Result = result;
            operation.CancelUrl = null; // Can't cancel completed operation
            operation.EstimatedCompletionAt = DateTimeOffset.UtcNow;

            _logger.LogInformation("Completed restore operation {OperationId} for backup '{BackupName}'",
                operationId, operation.BackupName);
        }

        return Task.CompletedTask;
    }

    public Task FailOperationAsync(string operationId, string errorMessage)
    {
        // Try backup operations first
        if (_backupOperations.TryGetValue(operationId, out var backupOperation))
        {
            backupOperation.Status = BackupOperationStatus.Failed;
            backupOperation.ErrorMessage = errorMessage;
            backupOperation.CancelUrl = null;

            _logger.LogError("Failed backup operation {OperationId}: {ErrorMessage}", operationId, errorMessage);
            return Task.CompletedTask;
        }

        // Try restore operations
        if (_restoreOperations.TryGetValue(operationId, out var restoreOperation))
        {
            restoreOperation.Status = RestoreOperationStatus.Failed;
            restoreOperation.ErrorMessage = errorMessage;
            restoreOperation.CancelUrl = null;

            _logger.LogError("Failed restore operation {OperationId}: {ErrorMessage}", operationId, errorMessage);
        }

        return Task.CompletedTask;
    }

    public Task CancelOperationAsync(string operationId)
    {
        // Try backup operations first
        if (_backupOperations.TryGetValue(operationId, out var backupOperation))
        {
            backupOperation.Status = BackupOperationStatus.Cancelled;
            backupOperation.CancelUrl = null;

            _logger.LogInformation("Cancelled backup operation {OperationId}", operationId);
            return Task.CompletedTask;
        }

        // Try restore operations
        if (_restoreOperations.TryGetValue(operationId, out var restoreOperation))
        {
            restoreOperation.Status = RestoreOperationStatus.Cancelled;
            restoreOperation.CancelUrl = null;

            _logger.LogInformation("Cancelled restore operation {OperationId}", operationId);
        }

        return Task.CompletedTask;
    }

    public Task<BackupOperationResponse?> GetBackupOperationAsync(string operationId)
    {
        _backupOperations.TryGetValue(operationId, out var operation);
        return Task.FromResult(operation);
    }

    public Task<RestoreOperationResponse?> GetRestoreOperationAsync(string operationId)
    {
        _restoreOperations.TryGetValue(operationId, out var operation);
        return Task.FromResult(operation);
    }

    public Task<IEnumerable<string>> GetActiveOperationsAsync()
    {
        var activeBackups = _backupOperations.Values
            .Where(op => op.Status == BackupOperationStatus.Running || op.Status == BackupOperationStatus.Queued)
            .Select(op => op.OperationId);

        var activeRestores = _restoreOperations.Values
            .Where(op => op.Status == RestoreOperationStatus.Running ||
                        op.Status == RestoreOperationStatus.Queued ||
                        op.Status == RestoreOperationStatus.Validating ||
                        op.Status == RestoreOperationStatus.Preparing ||
                        op.Status == RestoreOperationStatus.Finalizing)
            .Select(op => op.OperationId);

        var allActive = activeBackups.Concat(activeRestores);
        return Task.FromResult(allActive);
    }

    public Task CleanupCompletedOperationsAsync(TimeSpan olderThan)
    {
        var cutoffTime = DateTimeOffset.UtcNow.Subtract(olderThan);
        var removedCount = 0;

        // Clean up completed backup operations
        var backupKeysToRemove = _backupOperations
            .Where(kvp => IsCompletedStatus(kvp.Value.Status) && kvp.Value.StartedAt < cutoffTime)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in backupKeysToRemove)
        {
            if (_backupOperations.TryRemove(key, out _))
            {
                removedCount++;
            }
        }

        // Clean up completed restore operations
        var restoreKeysToRemove = _restoreOperations
            .Where(kvp => IsCompletedRestoreStatus(kvp.Value.Status) && kvp.Value.StartedAt < cutoffTime)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in restoreKeysToRemove)
        {
            if (_restoreOperations.TryRemove(key, out _))
            {
                removedCount++;
            }
        }

        if (removedCount > 0)
        {
            _logger.LogInformation("Cleaned up {Count} completed operations older than {Age}",
                removedCount, olderThan);
        }

        return Task.CompletedTask;
    }

    private static bool IsCompletedStatus(BackupOperationStatus status)
    {
        return status is BackupOperationStatus.Completed or
               BackupOperationStatus.Failed or
               BackupOperationStatus.Cancelled or
               BackupOperationStatus.CompletedWithWarnings;
    }

    private static bool IsCompletedRestoreStatus(RestoreOperationStatus status)
    {
        return status is RestoreOperationStatus.Completed or
               RestoreOperationStatus.Failed or
               RestoreOperationStatus.Cancelled or
               RestoreOperationStatus.CompletedWithWarnings;
    }
}