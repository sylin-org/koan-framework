using Koan.Data.Backup.Abstractions;
using Koan.Data.Backup.Models;
using Koan.Web.Backup.Models;
using Koan.Web.Backup.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace Koan.Web.Backup.Controllers;

/// <summary>
/// API controller for entity-specific backup and restore operations
/// </summary>
[ApiController]
[Route("api/entities")]
[Produces("application/json")]
public class EntityController : ControllerBase
{
    private readonly IBackupService _backupService;
    private readonly IRestoreService _restoreService;
    private readonly IBackupDiscoveryService _backupDiscoveryService;
    private readonly IEntityDiscoveryService _entityDiscoveryService;
    private readonly IBackupOperationTracker _operationTracker;
    private readonly ILogger<EntityController> _logger;

    public EntityController(
        IBackupService backupService,
        IRestoreService restoreService,
        IBackupDiscoveryService backupDiscoveryService,
        IEntityDiscoveryService entityDiscoveryService,
        IBackupOperationTracker operationTracker,
        ILogger<EntityController> logger)
    {
        _backupService = backupService;
        _restoreService = restoreService;
        _backupDiscoveryService = backupDiscoveryService;
        _entityDiscoveryService = entityDiscoveryService;
        _operationTracker = operationTracker;
        _logger = logger;
    }

    /// <summary>
    /// Get list of all discoverable entity types
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of entity types available for backup</returns>
    [HttpGet]
    [ProducesResponseType(typeof(EntityTypeInfo[]), 200)]
    [ProducesResponseType(500)]
    public async Task<ActionResult<EntityTypeInfo[]>> GetEntityTypes(CancellationToken ct = default)
    {
        try
        {
            var discovery = await _entityDiscoveryService.DiscoverAllEntitiesAsync(ct);
            return Ok(discovery.Entities.ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover entity types");
            return StatusCode(500, new { error = "Failed to discover entity types", details = ex.Message });
        }
    }

    /// <summary>
    /// Create a backup of a specific entity type
    /// </summary>
    /// <param name="entityType">Entity type name (e.g., "User", "Order")</param>
    /// <param name="request">Backup configuration</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Operation tracking information</returns>
    [HttpPost("{entityType}/backup")]
    [ProducesResponseType(typeof(BackupOperationResponse), 202)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(500)]
    public async Task<ActionResult<BackupOperationResponse>> CreateEntityBackup(
        [Required] string entityType,
        [FromBody] EntityBackupRequest request,
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Starting backup for entity type '{EntityType}' with name '{BackupName}'",
                entityType, request.Name);

            // Find the entity type in discovered entities
            var discovery = await _entityDiscoveryService.DiscoverAllEntitiesAsync(ct);
            var entityInfo = discovery.Entities.FirstOrDefault(e =>
                string.Equals(e.EntityType.Name, entityType, StringComparison.OrdinalIgnoreCase));

            if (entityInfo == null)
            {
                return NotFound(new { error = "Entity type not found", entityType });
            }

            // Start tracking the operation
            var operationId = await _operationTracker.StartBackupOperationAsync(request.Name, ct);

            // Start the backup operation asynchronously using reflection
            _ = Task.Run(async () =>
            {
                try
                {
                    var options = MapToBackupOptions(request);

                    await _operationTracker.UpdateBackupProgressAsync(operationId, new BackupProgressInfo
                    {
                        CurrentStage = $"Backing up {entityType}",
                        PercentComplete = 0,
                        CurrentEntityType = entityType
                    });

                    // Use reflection to call the generic backup method
                    var result = await BackupEntityByReflection(entityInfo, request.Name, options, ct);
                    await _operationTracker.CompleteBackupOperationAsync(operationId, result);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Entity backup operation {OperationId} for {EntityType} failed",
                        operationId, entityType);
                    await _operationTracker.FailOperationAsync(operationId, ex.Message);
                }
            }, ct);

            var response = await _operationTracker.GetBackupOperationAsync(operationId);
            return Accepted(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start backup for entity type '{EntityType}'", entityType);
            return StatusCode(500, new { error = "Failed to start backup operation", details = ex.Message });
        }
    }

    /// <summary>
    /// Restore a specific entity type from backup
    /// </summary>
    /// <param name="entityType">Entity type name (e.g., "User", "Order")</param>
    /// <param name="backupName">Name or ID of the backup to restore from</param>
    /// <param name="request">Restore configuration</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Operation tracking information</returns>
    [HttpPost("{entityType}/restore/{backupName}")]
    [ProducesResponseType(typeof(RestoreOperationResponse), 202)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(500)]
    public async Task<ActionResult<RestoreOperationResponse>> RestoreEntity(
        [Required] string entityType,
        [Required] string backupName,
        [FromBody] EntityRestoreRequest request,
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Starting restore for entity type '{EntityType}' from backup '{BackupName}'",
                entityType, backupName);

            // Verify backup exists and contains the entity type
            var backupInfo = await _backupDiscoveryService.GetBackupAsync(backupName, ct);
            if (backupInfo == null)
            {
                return NotFound(new { error = "Backup not found", backupName });
            }

            var entityInBackup = backupInfo.EntityTypes?.FirstOrDefault(e =>
                string.Equals(e, entityType, StringComparison.OrdinalIgnoreCase));

            if (entityInBackup == null)
            {
                return NotFound(new { error = "Entity type not found in backup", entityType, backupName });
            }

            // Find the entity type in discovered entities
            var discovery = await _entityDiscoveryService.DiscoverAllEntitiesAsync(ct);
            var entityInfo = discovery.Entities.FirstOrDefault(e =>
                string.Equals(e.EntityType.Name, entityType, StringComparison.OrdinalIgnoreCase));

            if (entityInfo == null)
            {
                return NotFound(new { error = "Entity type not found in current system", entityType });
            }

            // Start tracking the operation
            var operationId = await _operationTracker.StartRestoreOperationAsync(backupName, ct);

            // Start the restore operation asynchronously using reflection
            _ = Task.Run(async () =>
            {
                try
                {
                    var options = MapToRestoreOptions(request);

                    await _operationTracker.UpdateRestoreProgressAsync(operationId, new RestoreProgressInfo
                    {
                        CurrentStage = $"Restoring {entityType}",
                        PercentComplete = 0,
                        CurrentEntityType = entityType,
                        TotalItemsInBackup = 0
                    });

                    // Use reflection to call the generic restore method
                    await RestoreEntityByReflection(entityInfo, backupName, options, ct);

                    var restoreResult = new RestoreResult
                    {
                        TotalItemsRestored = 0,
                        EntityTypesRestored = 1,
                        Duration = TimeSpan.Zero, // Would be calculated in real implementation
                        ErrorCount = 0,
                        EntityResults = new[]
                        {
                            new EntityRestoreResult
                            {
                                EntityType = entityType,
                                ItemsRestored = 0,
                                ItemsSkipped = 0,
                                ErrorCount = 0,
                                Duration = TimeSpan.Zero,
                                OptimizationUsed = false // Would be determined by actual implementation
                            }
                        }
                    };

                    await _operationTracker.CompleteRestoreOperationAsync(operationId, restoreResult);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Entity restore operation {OperationId} for {EntityType} failed",
                        operationId, entityType);
                    await _operationTracker.FailOperationAsync(operationId, ex.Message);
                }
            }, ct);

            var response = await _operationTracker.GetRestoreOperationAsync(operationId);
            return Accepted(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start restore for entity type '{EntityType}' from backup '{BackupName}'",
                entityType, backupName);
            return StatusCode(500, new { error = "Failed to start restore operation", details = ex.Message });
        }
    }

    /// <summary>
    /// Get backup history for a specific entity type
    /// </summary>
    /// <param name="entityType">Entity type name</param>
    /// <param name="page">Page number (1-based)</param>
    /// <param name="pageSize">Number of items per page</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Backups containing the specified entity type</returns>
    [HttpGet("{entityType}/backups")]
    [ProducesResponseType(typeof(BackupCatalogResponse), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(500)]
    public async Task<ActionResult<BackupCatalogResponse>> GetEntityBackups(
        [Required] string entityType,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
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
                EntityTypes = new[] { entityType }
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
            _logger.LogError(ex, "Failed to retrieve backups for entity type '{EntityType}'", entityType);
            return StatusCode(500, new { error = "Failed to retrieve entity backups", details = ex.Message });
        }
    }

    private async Task<Data.Backup.Models.BackupManifest> BackupEntityByReflection(
        EntityTypeInfo entityInfo,
        string backupName,
        BackupOptions options,
        CancellationToken ct)
    {
        try
        {
            // Get the generic method BackupEntityAsync<TEntity, TKey>
            var method = typeof(IBackupService).GetMethod(nameof(IBackupService.BackupEntityAsync));
            if (method == null)
                throw new InvalidOperationException("BackupEntityAsync method not found");

            // Make it generic with the specific entity and key types
            var genericMethod = method.MakeGenericMethod(entityInfo.EntityType, entityInfo.KeyType);

            // Invoke the method
            var task = (Task<Data.Backup.Models.BackupManifest>)genericMethod.Invoke(_backupService,
                new object[] { backupName, options, ct })!;

            return await task;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to backup entity {EntityType} using reflection", entityInfo.EntityType.Name);
            throw;
        }
    }

    private async Task RestoreEntityByReflection(
        EntityTypeInfo entityInfo,
        string backupName,
        RestoreOptions options,
        CancellationToken ct)
    {
        try
        {
            // Get the generic method RestoreEntityAsync<TEntity, TKey>
            var method = typeof(IRestoreService).GetMethod(nameof(IRestoreService.RestoreEntityAsync));
            if (method == null)
                throw new InvalidOperationException("RestoreEntityAsync method not found");

            // Make it generic with the specific entity and key types
            var genericMethod = method.MakeGenericMethod(entityInfo.EntityType, entityInfo.KeyType);

            // Invoke the method
            var task = (Task)genericMethod.Invoke(_restoreService,
                new object[] { backupName, options, ct })!;

            await task;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore entity {EntityType} using reflection", entityInfo.EntityType.Name);
            throw;
        }
    }

    private static BackupOptions MapToBackupOptions(EntityBackupRequest request)
    {
        return new BackupOptions
        {
            Description = request.Description,
            Tags = request.Tags,
            Partition = request.Partition,
            StorageProfile = request.StorageProfile ?? string.Empty,
            CompressionLevel = request.CompressionLevel,
            VerificationEnabled = request.VerificationEnabled,
            BatchSize = request.BatchSize,
            Metadata = request.Metadata
        };
    }

    private static RestoreOptions MapToRestoreOptions(EntityRestoreRequest request)
    {
        return new RestoreOptions
        {
            TargetPartition = request.TargetPartition,
            StorageProfile = request.StorageProfile ?? string.Empty,
            ReplaceExisting = request.ReplaceExisting,
            DisableConstraints = request.DisableConstraints,
            DisableIndexes = request.DisableIndexes,
            UseBulkMode = request.UseBulkMode,
            BatchSize = request.BatchSize,
            OptimizationLevel = request.OptimizationLevel,
            DryRun = request.DryRun,
            ContinueOnError = request.ContinueOnError,
            Timeout = request.Timeout
        };
    }
}