using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Sources;
using Koan.AI.Sources.Policies;

namespace Koan.AI.Sources;

/// <summary>
/// Background service that monitors health of AI sources and manages circuit breaker state transitions.
/// Probes unhealthy sources periodically to detect recovery.
/// </summary>
public sealed class AiSourceHealthMonitor : BackgroundService
{
    private readonly IAiSourceRegistry _sourceRegistry;
    private readonly IAiGroupRegistry _groupRegistry;
    private readonly ISourceHealthRegistry _healthRegistry;
    private readonly Contracts.Routing.IAiAdapterRegistry _adapterRegistry;
    private readonly ILogger<AiSourceHealthMonitor> _logger;

    public AiSourceHealthMonitor(
        IAiSourceRegistry sourceRegistry,
        IAiGroupRegistry groupRegistry,
        ISourceHealthRegistry healthRegistry,
        Contracts.Routing.IAiAdapterRegistry adapterRegistry,
        ILogger<AiSourceHealthMonitor> logger)
    {
        _sourceRegistry = sourceRegistry;
        _groupRegistry = groupRegistry;
        _healthRegistry = healthRegistry;
        _adapterRegistry = adapterRegistry;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AI source health monitor started");

        // Wait a bit before first health check (let system stabilize)
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PerformHealthChecksAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error during health check cycle");
            }

            // Wait for next health check interval
            // Use minimum interval across all groups
            var nextInterval = GetMinimumHealthCheckInterval();
            await Task.Delay(nextInterval, stoppingToken).ConfigureAwait(false);
        }

        _logger.LogInformation("AI source health monitor stopped");
    }

    private async Task PerformHealthChecksAsync(CancellationToken ct)
    {
        var groups = _groupRegistry.GetAllGroups();

        foreach (var group in groups)
        {
            if (!group.HealthCheck.Enabled)
            {
                continue;
            }

            var sources = _sourceRegistry.GetSourcesInGroup(group.Name);
            if (sources.Count == 0)
            {
                continue;
            }

            foreach (var source in sources)
            {
                var health = _healthRegistry.GetHealth(source.Name);

                // Only probe sources that are unhealthy or half-open
                if (health.State == CircuitState.Closed)
                {
                    continue;
                }

                await ProbeSourceHealthAsync(source, group, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task ProbeSourceHealthAsync(
        AiSourceDefinition source,
        AiGroupDefinition group,
        CancellationToken ct)
    {
        var timeout = TimeSpan.FromSeconds(group.HealthCheck.TimeoutSeconds);
        using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        probeCts.CancelAfter(timeout);

        try
        {
            _logger.LogDebug(
                "Probing health of source '{SourceName}' (group '{GroupName}')",
                source.Name,
                group.Name);

            // Find adapter for this source
            var policy = GroupPolicyFactory.CreatePolicy(group.Policy, _adapterRegistry);
            var adapter = policy.SelectAdapter(new[] { source }, _healthRegistry);

            if (adapter == null)
            {
                _logger.LogWarning(
                    "No adapter found for source '{SourceName}' during health probe",
                    source.Name);
                return;
            }

            // Try to list models as health check (lightweight operation)
            var models = await adapter.ListModelsAsync(probeCts.Token).ConfigureAwait(false);

            if (models.Count > 0)
            {
                // Successful probe
                _logger.LogInformation(
                    "Health probe succeeded for source '{SourceName}' (found {ModelCount} models)",
                    source.Name,
                    models.Count);

                _healthRegistry.RecordSuccess(source.Name);
            }
            else
            {
                // No models returned - treat as failure
                _logger.LogWarning(
                    "Health probe returned no models for source '{SourceName}'",
                    source.Name);

                _healthRegistry.RecordFailure(source.Name);
            }
        }
        catch (OperationCanceledException) when (probeCts.Token.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Probe timeout
            _logger.LogWarning(
                "Health probe timed out for source '{SourceName}' after {TimeoutSeconds}s",
                source.Name,
                group.HealthCheck.TimeoutSeconds);

            _healthRegistry.RecordFailure(source.Name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Health probe failed for source '{SourceName}': {ErrorMessage}",
                source.Name,
                ex.Message);

            _healthRegistry.RecordFailure(source.Name);
        }
    }

    private TimeSpan GetMinimumHealthCheckInterval()
    {
        var groups = _groupRegistry.GetAllGroups();
        var enabledGroups = groups.Where(g => g.HealthCheck.Enabled).ToList();

        if (enabledGroups.Count == 0)
        {
            return TimeSpan.FromSeconds(30); // Default interval
        }

        var minInterval = enabledGroups.Min(g => g.HealthCheck.IntervalSeconds);
        return TimeSpan.FromSeconds(Math.Max(5, minInterval)); // Minimum 5 seconds
    }
}
