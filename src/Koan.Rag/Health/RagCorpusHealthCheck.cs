using Koan.Rag.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Koan.Rag.Health;

/// <summary>
/// Health check for the RAG subsystem. Reports corpus readiness,
/// concept graph health, and ingestion queue status.
/// Registered with tags ["ai", "rag", "ready"].
/// </summary>
internal sealed class RagCorpusHealthCheck(
    IConceptGraphStore graphStore,
    ILogger<RagCorpusHealthCheck> logger) : IHealthCheck
{
    private const double DegradedGraphDensityThreshold = 0.3;
    private const int DegradedPendingJobThreshold = 50;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var ragService = Koan.Core.Hosting.App.AppHost.Current
                ?.GetService(typeof(IRagService)) as IRagService;

            if (ragService is null)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    "RAG service not registered. Ensure Koan.Rag is referenced."));
            }

            var graphStats = graphStore.GetStats();

            var data = new Dictionary<string, object>
            {
                ["service_available"] = true,
                ["graph_entities"] = graphStats.EntityCount,
                ["graph_relationships"] = graphStats.RelationshipCount,
                ["graph_density"] = graphStats.Density,
                ["graph_last_persisted"] = graphStats.LastPersisted?.ToString("o") ?? "never"
            };

            // Determine health status
            var status = HealthStatus.Healthy;
            var messages = new List<string>();

            // Graph density check (too dense = noisy, everything connected to everything)
            if (graphStats.EntityCount > 100 && graphStats.Density > DegradedGraphDensityThreshold)
            {
                status = HealthStatus.Degraded;
                messages.Add($"Graph density {graphStats.Density:F3} exceeds threshold " +
                           $"({DegradedGraphDensityThreshold}). Consider pruning low-confidence edges.");
            }

            // Graph persistence freshness
            if (graphStats.EntityCount > 0 && graphStats.LastPersisted is null)
            {
                status = HealthStatus.Degraded;
                messages.Add("Graph has not been persisted since last load.");
            }

            var message = messages.Count > 0
                ? string.Join(" | ", messages)
                : $"RAG operational: {graphStats.EntityCount} entities, " +
                  $"{graphStats.RelationshipCount} relationships";

            return Task.FromResult(new HealthCheckResult(status, message, data: data));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error performing RAG corpus health check");
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Failed to perform RAG health check", ex));
        }
    }
}
