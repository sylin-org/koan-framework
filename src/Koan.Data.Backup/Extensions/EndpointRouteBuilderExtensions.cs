using Koan.Data.Backup.Abstractions;
using Koan.Data.Backup.Initialization;
using Koan.Data.Backup.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Backup.Extensions;

/// <summary>
/// Extension methods for registering backup diagnostic endpoints.
/// </summary>
public static class EndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps backup diagnostic endpoints for inventory and health checks.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">Optional route pattern prefix. Default is "backup".</param>
    /// <returns>The endpoint route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapBackupDiagnostics(
        this IEndpointRouteBuilder endpoints,
        string pattern = "backup")
    {
        var group = endpoints.MapGroup(pattern)
            .WithTags("Backup Diagnostics")
            .WithOpenApi();

        // GET /backup/inventory - Get current backup inventory
        group.MapGet("/inventory", async (IEntityDiscoveryService discoveryService) =>
        {
            // Try to get cached inventory first
            var inventory = KoanAutoRegistrar.GetCachedInventory();

            // If not cached, build it now
            inventory ??= await discoveryService.BuildInventoryAsync();

            return Results.Ok(new
            {
                generatedAt = inventory.GeneratedAt,
                summary = new
                {
                    totalIncluded = inventory.TotalIncludedEntities,
                    totalExcluded = inventory.TotalExcludedEntities,
                    totalWarnings = inventory.TotalWarnings,
                    isHealthy = inventory.IsHealthy
                },
                includedEntities = inventory.IncludedEntities.Select(p => new
                {
                    entityName = p.EntityName,
                    entityFullName = p.EntityFullName,
                    encrypt = p.Encrypt,
                    includeSchema = p.IncludeSchema,
                    source = p.Source
                }),
                excludedEntities = inventory.ExcludedEntities.Select(p => new
                {
                    entityName = p.EntityName,
                    entityFullName = p.EntityFullName,
                    reason = p.Reason
                }),
                warnings = inventory.Warnings
            });
        })
        .WithName("GetBackupInventory")
        .WithSummary("Get backup inventory with policy details")
        .WithDescription("Returns the current backup inventory including included entities, excluded entities, and coverage warnings.");

        // GET /backup/inventory/health - Get inventory health status
        group.MapGet("/inventory/health", async (IEntityDiscoveryService discoveryService) =>
        {
            var inventory = KoanAutoRegistrar.GetCachedInventory()
                ?? await discoveryService.BuildInventoryAsync();

            if (inventory.IsHealthy)
            {
                return Results.Ok(new
                {
                    status = "Healthy",
                    message = "All entities have backup coverage",
                    totalEntities = inventory.TotalIncludedEntities + inventory.TotalExcludedEntities,
                    includedEntities = inventory.TotalIncludedEntities,
                    excludedEntities = inventory.TotalExcludedEntities
                });
            }
            else
            {
                return Results.Json(new
                {
                    status = "Warning",
                    message = $"{inventory.TotalWarnings} entity/entities lack backup coverage",
                    totalEntities = inventory.TotalIncludedEntities + inventory.TotalExcludedEntities,
                    includedEntities = inventory.TotalIncludedEntities,
                    excludedEntities = inventory.TotalExcludedEntities,
                    warnings = inventory.Warnings
                }, statusCode: 200); // 200 OK but with warnings in response
            }
        })
        .WithName("GetBackupInventoryHealth")
        .WithSummary("Get backup inventory health status")
        .WithDescription("Returns health status indicating whether all entities have backup coverage.");

        // POST /backup/inventory/refresh - Force inventory rebuild
        group.MapPost("/inventory/refresh", async (IEntityDiscoveryService discoveryService) =>
        {
            var inventory = await discoveryService.BuildInventoryAsync();

            return Results.Ok(new
            {
                message = "Inventory refreshed successfully",
                generatedAt = inventory.GeneratedAt,
                totalIncluded = inventory.TotalIncludedEntities,
                totalExcluded = inventory.TotalExcludedEntities,
                totalWarnings = inventory.TotalWarnings
            });
        })
        .WithName("RefreshBackupInventory")
        .WithSummary("Force rebuild of backup inventory")
        .WithDescription("Rebuilds the backup inventory by re-scanning all entities and applying backup policies.");

        return endpoints;
    }
}
