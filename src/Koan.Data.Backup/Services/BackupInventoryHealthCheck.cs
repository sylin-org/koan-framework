using Koan.Data.Backup.Abstractions;
using Koan.Data.Backup.Initialization;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Koan.Data.Backup.Services;

/// <summary>
/// Health check that validates backup inventory coverage.
/// </summary>
/// <remarks>
/// Reports healthy status when all entities have backup coverage (either included or excluded with reason).
/// Reports degraded status when entities lack backup coverage.
/// </remarks>
public class BackupInventoryHealthCheck : IHealthCheck
{
    private readonly IEntityDiscoveryService _discoveryService;
    private readonly ILogger<BackupInventoryHealthCheck> _logger;

    public BackupInventoryHealthCheck(
        IEntityDiscoveryService discoveryService,
        ILogger<BackupInventoryHealthCheck> logger)
    {
        _discoveryService = discoveryService;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Try to get cached inventory first (populated during startup)
            var inventory = KoanAutoRegistrar.GetCachedInventory();

            // If not cached, build it now
            if (inventory == null)
            {
                _logger.LogDebug("Backup inventory not cached, building now...");
                inventory = await _discoveryService.BuildInventoryAsync(cancellationToken);
            }

            var data = new Dictionary<string, object>
            {
                ["TotalIncludedEntities"] = inventory.TotalIncludedEntities,
                ["TotalExcludedEntities"] = inventory.TotalExcludedEntities,
                ["TotalWarnings"] = inventory.TotalWarnings,
                ["GeneratedAt"] = inventory.GeneratedAt
            };

            if (inventory.IsHealthy)
            {
                return HealthCheckResult.Healthy(
                    "All entities have backup coverage",
                    data);
            }
            else
            {
                data["Warnings"] = inventory.Warnings;

                return HealthCheckResult.Degraded(
                    $"{inventory.TotalWarnings} entity/entities lack backup coverage. " +
                    $"Review warnings and add [EntityBackup] attributes or assembly-level [EntityBackupScope].",
                    data: data);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup inventory health check failed");

            return HealthCheckResult.Unhealthy(
                "Failed to build backup inventory",
                ex,
                new Dictionary<string, object>
                {
                    ["Error"] = ex.Message
                });
        }
    }
}
