using System;
using System.Collections.Generic;

namespace Koan.Jobs.Model;

public static class JobMetadataExtensions
{
    private const string TenantKey = "TenantId";
    private const string UserKey = "UserId";

    public static string? GetTenantId(this Job job)
        => job.Metadata.TryGetValue(TenantKey, out var value) ? value?.ToString() : null;

    public static Job With(
        this Job job,
        string? tenantId = null,
        string? userId = null,
        string? correlationId = null,
        Action<IDictionary<string, object?>>? metadata = null)
    {
        if (!string.IsNullOrWhiteSpace(tenantId))
            job.Metadata[TenantKey] = tenantId;

        if (!string.IsNullOrWhiteSpace(userId))
            job.Metadata[UserKey] = userId;

        if (!string.IsNullOrWhiteSpace(correlationId))
            job.CorrelationId = correlationId;

        metadata?.Invoke(job.Metadata);
        return job;
    }

    public static Job With(
        this Job job,
        Action<IDictionary<string, object?>> metadata)
        => job.With(metadata: metadata);

    public static Job WithMetadata(this Job job, string key, object? value)
    {
        job.Metadata[key] = value;
        return job;
    }

    /// <summary>
    /// Tag the job with the host platform it targets (e.g. <c>"nexusmods"</c>, <c>"ko-fi"</c>).
    /// Read by the <see cref="Execution.JobExecutor"/> at dispatch time to consult the
    /// <see cref="RateGating.IHostRateGate"/>: if the host is currently gated (typically because
    /// another job from the same host hit a 429), this job defers until the gate releases — without
    /// consuming its retry budget.
    /// </summary>
    public static Job WithHost(this Job job, string hostTag)
    {
        if (!string.IsNullOrWhiteSpace(hostTag))
        {
            job.Metadata[Execution.JobExecutor.HostMetadataKey] = hostTag;
        }
        return job;
    }

    /// <summary>Read the host tag previously set via <see cref="WithHost"/>, or <see langword="null"/>.</summary>
    public static string? GetHost(this Job job)
        => job.Metadata.TryGetValue(Execution.JobExecutor.HostMetadataKey, out var raw) ? raw?.ToString() : null;
}
