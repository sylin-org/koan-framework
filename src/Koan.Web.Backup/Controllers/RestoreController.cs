using Koan.Data.Backup.Abstractions;
using Koan.Data.Backup.Models;
using Koan.Web.Backup.Models;
using Koan.Web.Backup.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;

namespace Koan.Web.Backup.Controllers;

/// <summary>
/// API controller for restore operations
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class RestoreController : ControllerBase
{
    private readonly IRestoreService _restoreService;
    private readonly IBackupDiscoveryService _backupDiscoveryService;
    private readonly IBackupOperationTracker _operationTracker;
    private readonly ILogger<RestoreController> _logger;

    public RestoreController(
        IRestoreService restoreService,
        IBackupDiscoveryService backupDiscoveryService,
        IBackupOperationTracker operationTracker,
        ILogger<RestoreController> logger)
    {
        _restoreService = restoreService;
        _backupDiscoveryService = backupDiscoveryService;
        _operationTracker = operationTracker;
        _logger = logger;
    }

    /// <summary>
    /// Restore all entities from a backup
    /// </summary>
    /// <param name="backupName">Name or ID of the backup to restore</param>
    /// <param name="request">Restore configuration</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Operation tracking information</returns>
    [HttpPost("{backupName}")]
    [ProducesResponseType(typeof(RestoreOperationResponse), 202)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(500)]
    public async Task<ActionResult<RestoreOperationResponse>> RestoreAllEntities(
        [Required] string backupName,
        [FromBody] RestoreGlobalBackupRequest request,
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Starting global restore from backup '{BackupName}'", backupName);

            // Verify backup exists
            var backupInfo = await _backupDiscoveryService.GetBackupAsync(backupName, ct);
            if (backupInfo == null)
            {
                return NotFound(new { error = "Backup not found", backupName });
            }

            // Start tracking the operation
            var operationId = await _operationTracker.StartRestoreOperationAsync(backupName, ct);

            // Start the restore operation asynchronously
            _ = Task.Run(async () =>
            {
                try
                {
                    var options = MapToGlobalRestoreOptions(request);

                    await _operationTracker.UpdateRestoreProgressAsync(operationId, new RestoreProgressInfo
                    {
                        CurrentStage = "Validating backup",
                        PercentComplete = 0
                    });

                    await _restoreService.RestoreAllEntitiesAsync(backupName, options, ct);

                    var restoreResult = new RestoreResult
                    {
                        TotalItemsRestored = 0, // TODO: Get actual count from service
                        EntityTypesRestored = 0,
                        TotalBytesProcessed = 0,
                        Duration = TimeSpan.Zero,
                        ErrorCount = 0,
                        EntityResults = Array.Empty<EntityRestoreResult>()
                    };

                    await _operationTracker.CompleteRestoreOperationAsync(operationId, restoreResult);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Global restore operation {OperationId} failed", operationId);
                    await _operationTracker.FailOperationAsync(operationId, ex.Message);
                }
            }, ct);

            var response = await _operationTracker.GetRestoreOperationAsync(operationId);
            return Accepted(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start global restore from backup '{BackupName}'", backupName);
            return StatusCode(500, new { error = "Failed to start restore operation", details = ex.Message });
        }
    }

    /// <summary>
    /// Test restore viability without actually performing the restore
    /// </summary>
    /// <param name="backupName">Name or ID of the backup to test</param>
    /// <param name="request">Restore configuration for testing</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Viability test results</returns>
    [HttpPost("{backupName}/test")]
    [ProducesResponseType(typeof(RestoreViabilityReport), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(500)]
    public async Task<ActionResult<RestoreViabilityReport>> TestRestoreViability(
        [Required] string backupName,
        [FromBody] RestoreGlobalBackupRequest? request = null,
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Testing restore viability for backup '{BackupName}'", backupName);

            var report = await _restoreService.TestRestoreViabilityAsync(backupName, ct);
            return Ok(report);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to test restore viability for backup '{BackupName}'", backupName);
            return StatusCode(500, new { error = "Failed to test restore viability", details = ex.Message });
        }
    }

    /// <summary>
    /// Get status of a restore operation
    /// </summary>
    /// <param name="operationId">Operation ID</param>
    /// <returns>Operation status</returns>
    [HttpGet("operations/{operationId}")]
    [ProducesResponseType(typeof(RestoreOperationResponse), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<RestoreOperationResponse>> GetRestoreOperation(
        [Required] string operationId)
    {
        var operation = await _operationTracker.GetRestoreOperationAsync(operationId);

        if (operation == null)
        {
            return NotFound(new { error = "Operation not found", operationId });
        }

        return Ok(operation);
    }

    /// <summary>
    /// Cancel a running restore operation
    /// </summary>
    /// <param name="operationId">Operation ID</param>
    /// <returns>Confirmation of cancellation</returns>
    [HttpPost("operations/{operationId}/cancel")]
    [ProducesResponseType(typeof(RestoreOperationResponse), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<RestoreOperationResponse>> CancelRestoreOperation(
        [Required] string operationId)
    {
        var operation = await _operationTracker.GetRestoreOperationAsync(operationId);

        if (operation == null)
        {
            return NotFound(new { error = "Operation not found", operationId });
        }

        // Check if operation can be cancelled
        var cancellableStatuses = new[]
        {
            RestoreOperationStatus.Queued,
            RestoreOperationStatus.Validating,
            RestoreOperationStatus.Preparing,
            RestoreOperationStatus.Running,
            RestoreOperationStatus.Finalizing
        };

        if (!cancellableStatuses.Contains(operation.Status))
        {
            return BadRequest(new { error = "Operation cannot be cancelled", status = operation.Status.ToString() });
        }

        await _operationTracker.CancelOperationAsync(operationId);

        var updatedOperation = await _operationTracker.GetRestoreOperationAsync(operationId);
        return Ok(updatedOperation);
    }

    /// <summary>
    /// Get restore history and statistics
    /// </summary>
    /// <param name="page">Page number (1-based)</param>
    /// <param name="pageSize">Number of items per page</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Paginated restore history</returns>
    [HttpGet("history")]
    [ProducesResponseType(typeof(RestoreHistoryResponse), 200)]
    [ProducesResponseType(500)]
    public ActionResult<RestoreHistoryResponse> GetRestoreHistory(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        try
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            // This would typically come from a persistent store
            // For now, returning empty result as placeholder
            var response = new RestoreHistoryResponse
            {
                Operations = Array.Empty<RestoreOperationSummary>(),
                TotalCount = 0,
                Page = page,
                PageSize = pageSize
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve restore history");
            return StatusCode(500, new { error = "Failed to retrieve restore history", details = ex.Message });
        }
    }

    private static GlobalRestoreOptions MapToGlobalRestoreOptions(RestoreGlobalBackupRequest request)
    {
        return new GlobalRestoreOptions
        {
            TargetSet = request.TargetSet,
            StorageProfile = request.StorageProfile ?? string.Empty,
            ReplaceExisting = request.ReplaceExisting,
            DisableConstraints = request.DisableConstraints,
            DisableIndexes = request.DisableIndexes,
            UseBulkMode = request.UseBulkMode,
            BatchSize = request.BatchSize,
            OptimizationLevel = request.OptimizationLevel,
            DryRun = request.DryRun,
            ContinueOnError = request.ContinueOnError,
            Timeout = request.Timeout,
            MaxConcurrency = request.MaxConcurrency,
            IncludeEntityTypes = request.IncludeEntityTypes,
            ExcludeEntityTypes = request.ExcludeEntityTypes,
            EntitySetMapping = request.EntitySetMapping,
            RestoreToOriginalSets = request.RestoreToOriginalSets,
            ValidateBeforeRestore = request.ValidateBeforeRestore
        };
    }
}

/// <summary>
/// Response model for restore history
/// </summary>
public class RestoreHistoryResponse
{
    /// <summary>
    /// List of restore operation summaries
    /// </summary>
    public RestoreOperationSummary[] Operations { get; set; } = Array.Empty<RestoreOperationSummary>();

    /// <summary>
    /// Total number of operations
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Current page number (1-based)
    /// </summary>
    public int Page { get; set; }

    /// <summary>
    /// Number of items per page
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Total number of pages
    /// </summary>
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}

/// <summary>
/// Summary of a restore operation
/// </summary>
public class RestoreOperationSummary
{
    /// <summary>
    /// Operation ID
    /// </summary>
    public string OperationId { get; set; } = string.Empty;

    /// <summary>
    /// Backup name that was restored
    /// </summary>
    public string BackupName { get; set; } = string.Empty;

    /// <summary>
    /// Operation status
    /// </summary>
    public RestoreOperationStatus Status { get; set; }

    /// <summary>
    /// Started timestamp
    /// </summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>
    /// Completed timestamp (if finished)
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Duration of the operation
    /// </summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>
    /// Number of items restored
    /// </summary>
    public long? ItemsRestored { get; set; }

    /// <summary>
    /// Number of errors encountered
    /// </summary>
    public int? ErrorCount { get; set; }
}