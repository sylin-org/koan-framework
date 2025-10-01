using Koan.Data.Backup.Abstractions;
using Koan.Data.Backup.Models;
using Koan.Web.Backup.Models;
using Koan.Web.Backup.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;

namespace Koan.Web.Backup.Controllers;

/// <summary>
/// API controller for backup operations
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class BackupController : ControllerBase
{
    private readonly IBackupService _backupService;
    private readonly IBackupDiscoveryService _backupDiscoveryService;
    private readonly IBackupOperationTracker _operationTracker;
    private readonly ILogger<BackupController> _logger;

    public BackupController(
        IBackupService backupService,
        IBackupDiscoveryService backupDiscoveryService,
        IBackupOperationTracker operationTracker,
        ILogger<BackupController> logger)
    {
        _backupService = backupService;
        _backupDiscoveryService = backupDiscoveryService;
        _operationTracker = operationTracker;
        _logger = logger;
    }

    /// <summary>
    /// Create a backup of all entities in the system
    /// </summary>
    /// <param name="request">Backup configuration</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Operation tracking information</returns>
    [HttpPost("all")]
    [ProducesResponseType(typeof(BackupOperationResponse), 202)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    [ProducesResponseType(500)]
    public async Task<ActionResult<BackupOperationResponse>> CreateGlobalBackup(
        [FromBody] CreateGlobalBackupRequest request,
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Starting global backup '{BackupName}' with {EntityTypesIncluded} included entity types",
                request.Name, request.IncludeEntityTypes?.Length ?? 0);

            // Start tracking the operation
            var operationId = await _operationTracker.StartBackupOperationAsync(request.Name, ct);

            // Start the backup operation asynchronously
            _ = Task.Run(async () =>
            {
                try
                {
                    var options = MapToGlobalBackupOptions(request);

                    await _operationTracker.UpdateBackupProgressAsync(operationId, new BackupProgressInfo
                    {
                        CurrentStage = "Initializing",
                        PercentComplete = 0
                    });

                    var result = await _backupService.BackupAllEntitiesAsync(request.Name, options, ct);
                    await _operationTracker.CompleteBackupOperationAsync(operationId, result);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Global backup operation {OperationId} failed", operationId);
                    await _operationTracker.FailOperationAsync(operationId, ex.Message);
                }
            }, ct);

            var response = await _operationTracker.GetBackupOperationAsync(operationId);
            return Accepted(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start global backup '{BackupName}'", request.Name);
            return StatusCode(500, new { error = "Failed to start backup operation", details = ex.Message });
        }
    }

    /// <summary>
    /// Create a selective backup based on filters
    /// </summary>
    /// <param name="request">Backup configuration with filters</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Operation tracking information</returns>
    [HttpPost("selective")]
    [ProducesResponseType(typeof(BackupOperationResponse), 202)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    [ProducesResponseType(500)]
    public async Task<ActionResult<BackupOperationResponse>> CreateSelectiveBackup(
        [FromBody] CreateGlobalBackupRequest request,
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Starting selective backup '{BackupName}'", request.Name);

            var operationId = await _operationTracker.StartBackupOperationAsync(request.Name, ct);

            _ = Task.Run(async () =>
            {
                try
                {
                    var options = MapToGlobalBackupOptions(request);

                    await _operationTracker.UpdateBackupProgressAsync(operationId, new BackupProgressInfo
                    {
                        CurrentStage = "Discovering entities",
                        PercentComplete = 5
                    });

                    // Create filter predicate based on request
                    Func<EntityTypeInfo, bool> filter = entity =>
                    {
                        // Include/exclude providers
                        if (request.IncludeProviders?.Length > 0 &&
                            !request.IncludeProviders.Contains(entity.Provider, StringComparer.OrdinalIgnoreCase))
                            return false;

                        if (request.ExcludeProviders?.Length > 0 &&
                            request.ExcludeProviders.Contains(entity.Provider, StringComparer.OrdinalIgnoreCase))
                            return false;

                        // Include/exclude entity types
                        if (request.IncludeEntityTypes?.Length > 0 &&
                            !request.IncludeEntityTypes.Contains(entity.EntityType.Name, StringComparer.OrdinalIgnoreCase))
                            return false;

                        if (request.ExcludeEntityTypes?.Length > 0 &&
                            request.ExcludeEntityTypes.Contains(entity.EntityType.Name, StringComparer.OrdinalIgnoreCase))
                            return false;

                        return true;
                    };

                    var result = await _backupService.BackupSelectedAsync(request.Name, filter, options, ct);
                    await _operationTracker.CompleteBackupOperationAsync(operationId, result);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Selective backup operation {OperationId} failed", operationId);
                    await _operationTracker.FailOperationAsync(operationId, ex.Message);
                }
            }, ct);

            var response = await _operationTracker.GetBackupOperationAsync(operationId);
            return Accepted(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start selective backup '{BackupName}'", request.Name);
            return StatusCode(500, new { error = "Failed to start backup operation", details = ex.Message });
        }
    }

    /// <summary>
    /// Get status of a backup operation
    /// </summary>
    /// <param name="operationId">Operation ID</param>
    /// <returns>Operation status</returns>
    [HttpGet("operations/{operationId}")]
    [ProducesResponseType(typeof(BackupOperationResponse), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<BackupOperationResponse>> GetBackupOperation(
        [Required] string operationId)
    {
        var operation = await _operationTracker.GetBackupOperationAsync(operationId);

        if (operation == null)
        {
            return NotFound(new { error = "Operation not found", operationId });
        }

        return Ok(operation);
    }

    /// <summary>
    /// Cancel a running backup operation
    /// </summary>
    /// <param name="operationId">Operation ID</param>
    /// <returns>Confirmation of cancellation</returns>
    [HttpPost("operations/{operationId}/cancel")]
    [ProducesResponseType(typeof(BackupOperationResponse), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<BackupOperationResponse>> CancelBackupOperation(
        [Required] string operationId)
    {
        var operation = await _operationTracker.GetBackupOperationAsync(operationId);

        if (operation == null)
        {
            return NotFound(new { error = "Operation not found", operationId });
        }

        if (operation.Status != BackupOperationStatus.Running && operation.Status != BackupOperationStatus.Queued)
        {
            return BadRequest(new { error = "Operation cannot be cancelled", status = operation.Status.ToString() });
        }

        await _operationTracker.CancelOperationAsync(operationId);

        var updatedOperation = await _operationTracker.GetBackupOperationAsync(operationId);
        return Ok(updatedOperation);
    }

    /// <summary>
    /// Get list of backup manifests/catalogs
    /// </summary>
    /// <param name="page">Page number (1-based)</param>
    /// <param name="pageSize">Number of items per page</param>
    /// <param name="tags">Filter by tags</param>
    /// <param name="search">Search term</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Paginated list of backups</returns>
    [HttpGet("manifests")]
    [ProducesResponseType(typeof(BackupCatalogResponse), 200)]
    [ProducesResponseType(500)]
    public async Task<ActionResult<BackupCatalogResponse>> GetBackupCatalog(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string[]? tags = null,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        try
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = new BackupQuery
            {
                Skip = (page - 1) * pageSize,
                Take = pageSize,
                Tags = tags,
                SearchTerm = search
            };

            var catalog = await _backupDiscoveryService.QueryBackupsAsync(query, ct);

            var response = new BackupCatalogResponse
            {
                Backups = catalog.Backups.ToArray(),
                TotalCount = catalog.TotalCount,
                Page = page,
                PageSize = pageSize
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve backup catalog");
            return StatusCode(500, new { error = "Failed to retrieve backup catalog", details = ex.Message });
        }
    }

    /// <summary>
    /// Get details of a specific backup
    /// </summary>
    /// <param name="backupId">Backup ID or name</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Backup information</returns>
    [HttpGet("manifests/{backupId}")]
    [ProducesResponseType(typeof(BackupInfo), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(500)]
    public async Task<ActionResult<BackupInfo>> GetBackup(
        [Required] string backupId,
        CancellationToken ct = default)
    {
        try
        {
            var backup = await _backupDiscoveryService.GetBackupAsync(backupId, ct);

            if (backup == null)
            {
                return NotFound(new { error = "Backup not found", backupId });
            }

            return Ok(backup);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve backup {BackupId}", backupId);
            return StatusCode(500, new { error = "Failed to retrieve backup", details = ex.Message });
        }
    }

    /// <summary>
    /// Verify integrity of a backup
    /// </summary>
    /// <param name="backupId">Backup ID or name</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Verification result</returns>
    [HttpPost("verify/{backupId}")]
    [ProducesResponseType(typeof(BackupValidationResult), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(500)]
    public async Task<ActionResult<BackupValidationResult>> VerifyBackup(
        [Required] string backupId,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _backupDiscoveryService.ValidateBackupAsync(backupId, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify backup {BackupId}", backupId);
            return StatusCode(500, new { error = "Failed to verify backup", details = ex.Message });
        }
    }

    /// <summary>
    /// Get system status and health information
    /// </summary>
    /// <returns>System status</returns>
    [HttpGet("status")]
    [ProducesResponseType(typeof(BackupSystemStatusResponse), 200)]
    public async Task<ActionResult<BackupSystemStatusResponse>> GetSystemStatus()
    {
        try
        {
            var activeOperations = await _operationTracker.GetActiveOperationsAsync();
            var activeBackups = 0;
            var activeRestores = 0;

            foreach (var operationId in activeOperations)
            {
                var backupOp = await _operationTracker.GetBackupOperationAsync(operationId);
                if (backupOp != null)
                {
                    activeBackups++;
                    continue;
                }

                var restoreOp = await _operationTracker.GetRestoreOperationAsync(operationId);
                if (restoreOp != null)
                {
                    activeRestores++;
                }
            }

            var response = new BackupSystemStatusResponse
            {
                Status = BackupSystemStatus.Healthy,
                ActiveBackupOperations = activeBackups,
                ActiveRestoreOperations = activeRestores,
                AvailableStorageProfiles = new[] { "default" }, // TODO: Get from config
                HealthIndicators = new Dictionary<string, object>
                {
                    ["active_operations"] = activeBackups + activeRestores,
                    ["last_check"] = DateTimeOffset.UtcNow
                }
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get system status");

            var errorResponse = new BackupSystemStatusResponse
            {
                Status = BackupSystemStatus.Error,
                HealthIndicators = new Dictionary<string, object>
                {
                    ["error"] = ex.Message,
                    ["last_check"] = DateTimeOffset.UtcNow
                }
            };

            return StatusCode(500, errorResponse);
        }
    }

    private static GlobalBackupOptions MapToGlobalBackupOptions(CreateGlobalBackupRequest request)
    {
        return new GlobalBackupOptions
        {
            Description = request.Description,
            Tags = request.Tags,
            Partition = request.Partition,
            StorageProfile = request.StorageProfile ?? string.Empty,
            CompressionLevel = request.CompressionLevel,
            VerificationEnabled = request.VerificationEnabled,
            BatchSize = request.BatchSize,
            Metadata = request.Metadata,
            MaxConcurrency = request.MaxConcurrency,
            IncludeProviders = request.IncludeProviders,
            ExcludeProviders = request.ExcludeProviders,
            IncludeEntityTypes = request.IncludeEntityTypes,
            ExcludeEntityTypes = request.ExcludeEntityTypes,
            IncludeEmptyEntities = request.IncludeEmptyEntities,
            MaxEntitySizeBytes = request.MaxEntitySizeBytes,
            Timeout = request.Timeout
        };
    }
}