using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.Orchestration.Aspire.SelfOrchestration;

/// <summary>
/// Hosted service that manages the lifecycle of self-orchestrated dependencies
/// </summary>
public class KoanSelfOrchestrationService : IHostedService
{
    private readonly IKoanDependencyOrchestrator _orchestrator;
    private readonly IKoanContainerManager _containerManager;
    private readonly ILogger<KoanSelfOrchestrationService> _logger;

    public KoanSelfOrchestrationService(
        IKoanDependencyOrchestrator orchestrator,
        IKoanContainerManager containerManager,
        ILogger<KoanSelfOrchestrationService> logger)
    {
        _orchestrator = orchestrator;
        _containerManager = containerManager;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Self-orchestration service starting...");

        try
        {
            // Clean up containers from crashed app instances (primary crash recovery mechanism)
            _logger.LogDebug("Cleaning up containers from crashed app instances...");
            await _containerManager.CleanupAppInstanceContainersAsync(cancellationToken);

            // Clean up any orphaned containers from other apps (fallback cleanup)
            _logger.LogDebug("Cleaning up orphaned containers from previous sessions...");
            await _containerManager.CleanupOrphanedKoanContainersAsync(cancellationToken);

            // Start the dependencies
            await _orchestrator.StartDependenciesAsync(cancellationToken);
            _logger.LogInformation("Self-orchestration service started successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Self-orchestration service failed to start");
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Self-orchestration service stopping...");

        try
        {
            await _orchestrator.StopDependenciesAsync(cancellationToken);
            _logger.LogInformation("Self-orchestration service stopped successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Self-orchestration service encountered errors during shutdown");
        }
    }
}