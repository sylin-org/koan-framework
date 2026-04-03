using Koan.Rag.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Koan.Rag.Health;

/// <summary>
/// Health check for the RAG subsystem. Reports corpus readiness,
/// ingestion queue health, and graph quality.
/// Registered with tags ["ai", "rag", "ready"].
/// </summary>
internal sealed class RagCorpusHealthCheck(
    ILogger<RagCorpusHealthCheck> logger) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if the RAG service is available
            var ragService = Koan.Core.Hosting.App.AppHost.Current
                ?.GetService(typeof(IRagService)) as IRagService;

            if (ragService is null)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    "RAG service not registered. Ensure Koan.Rag is referenced."));
            }

            // TODO Phase 4: Check queue depth, error rates, graph health
            // For now, report healthy if the service is available
            var data = new Dictionary<string, object>
            {
                ["service_available"] = true,
            };

            return Task.FromResult(HealthCheckResult.Healthy(
                "RAG subsystem operational", data));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error performing RAG corpus health check");
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Failed to perform RAG health check", ex));
        }
    }
}
