using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace S13.DocMind.Infrastructure;

public sealed class DocMindStorageHealthCheck : IHealthCheck
{
    private readonly DocMindOptions _options;

    public DocMindStorageHealthCheck(IOptions<DocMindOptions> options)
    {
        _options = options.Value;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var path = _options.Storage.BasePath;
        if (!Path.IsPathFullyQualified(path))
        {
            path = Path.Combine(AppContext.BaseDirectory, path);
        }

        if (!Directory.Exists(path))
        {
            return Task.FromResult(HealthCheckResult.Unhealthy($"DocMind storage path missing: {path}"));
        }

        return Task.FromResult(HealthCheckResult.Healthy("DocMind storage available"));
    }
}

public sealed class DocMindVectorHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var snapshot = DocMindVectorHealth.LatestSnapshot;
        if (!snapshot.AdapterAvailable)
        {
            return Task.FromResult(HealthCheckResult.Degraded("Vector adapter unavailable", data: new Dictionary<string, object?>
            {
                ["missingProfiles"] = snapshot.MissingProfiles,
                ["lastAuditError"] = snapshot.LastAuditError ?? string.Empty
            }));
        }

        if (snapshot.FallbackActive)
        {
            return Task.FromResult(HealthCheckResult.Degraded("Vector fallback active", data: new Dictionary<string, object?>
            {
                ["missingProfiles"] = snapshot.MissingProfiles,
                ["lastAuditError"] = snapshot.LastAuditError ?? string.Empty,
                ["lastAdapterModel"] = snapshot.LastAdapterModel ?? string.Empty
            }));
        }

        return Task.FromResult(HealthCheckResult.Healthy("Vector adapter ready", data: new Dictionary<string, object?>
        {
            ["lastAuditAt"] = snapshot.LastAuditAt,
            ["lastSearchLatencyMs"] = snapshot.LastSearchLatencyMs,
            ["lastGenerationDurationMs"] = snapshot.LastGenerationDurationMs,
            ["lastAdapterModel"] = snapshot.LastAdapterModel ?? string.Empty
        }));
    }
}

public sealed class DocMindDiscoveryHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var status = DocumentDiscoveryRefreshService.LatestStatus;
        if (!string.IsNullOrWhiteSpace(status.LastError))
        {
            return Task.FromResult(HealthCheckResult.Degraded("Discovery refresh failing", data: new Dictionary<string, object?>
            {
                ["pending"] = status.PendingCount,
                ["lastError"] = status.LastError,
                ["lastReason"] = status.LastReason ?? string.Empty
            }));
        }

        if (status.PendingCount > 10)
        {
            return Task.FromResult(HealthCheckResult.Degraded("Discovery refresh backlog", data: new Dictionary<string, object?>
            {
                ["pending"] = status.PendingCount,
                ["lastQueuedAt"] = status.LastQueuedAt
            }));
        }

        return Task.FromResult(HealthCheckResult.Healthy("Discovery refresh healthy", data: new Dictionary<string, object?>
        {
            ["pending"] = status.PendingCount,
            ["lastCompletedAt"] = status.LastCompletedAt,
            ["lastDurationMs"] = status.LastDuration?.TotalMilliseconds
        }));
    }
}
