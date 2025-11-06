using Koan.ServiceMesh.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.ServiceMesh.ServiceMesh;

/// <summary>
/// Background hosted service that coordinates service mesh operations.
/// Handles discovery broadcasts, periodic announcements, and maintenance.
/// </summary>
internal class KoanServiceMeshCoordinator : BackgroundService
{
    private readonly IKoanServiceMesh _serviceMesh;
    private readonly IEnumerable<KoanServiceDescriptor> _descriptors;
    private readonly ILogger<KoanServiceMeshCoordinator> _logger;

    public KoanServiceMeshCoordinator(
        IKoanServiceMesh serviceMesh,
        IEnumerable<KoanServiceDescriptor> descriptors,
        ILogger<KoanServiceMeshCoordinator> logger)
    {
        _serviceMesh = serviceMesh;
        _descriptors = descriptors;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Get the first descriptor for timing configuration
        // All services share the same orchestrator channel timing
        var descriptor = _descriptors.FirstOrDefault();
        if (descriptor == null)
        {
            _logger.LogWarning("Koan:services:coordinator no services registered, exiting");
            return;
        }

        _logger.LogInformation(
            "Koan:services:coordinator starting for {Count} service(s): {Services}",
            _descriptors.Count(),
            string.Join(", ", _descriptors.Select(d => d.ServiceId)));

        try
        {
            // Initial discovery broadcast
            _logger.LogInformation("Koan:services:coordinator broadcasting discovery request");
            await _serviceMesh.DiscoverAsync(stoppingToken);

            // Wait briefly for responses
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

            // Initial announcement
            _logger.LogInformation("Koan:services:coordinator announcing services");
            await _serviceMesh.AnnounceAsync(stoppingToken);

            // Start maintenance task (listens for announcements, cleans stale instances)
            var maintenanceTask = _serviceMesh.MaintainAsync(stoppingToken);

            // Start heartbeat task (periodic announcements)
            var heartbeatTask = HeartbeatLoopAsync(descriptor.HeartbeatInterval, stoppingToken);

            // Wait for cancellation
            await Task.WhenAll(maintenanceTask, heartbeatTask);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Koan:services:coordinator stopping");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Koan:services:coordinator error");
            throw;
        }
    }

    private async Task HeartbeatLoopAsync(TimeSpan heartbeatInterval, CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Koan:services:coordinator heartbeat started (interval: {Seconds}s)",
            heartbeatInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(heartbeatInterval, stoppingToken);

                _logger.LogDebug("Koan:services:coordinator sending heartbeat");
                await _serviceMesh.AnnounceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Koan:services:coordinator heartbeat error");
            }
        }

        _logger.LogInformation("Koan:services:coordinator heartbeat stopped");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Koan:services:coordinator shutting down");
        await base.StopAsync(cancellationToken);
    }
}
