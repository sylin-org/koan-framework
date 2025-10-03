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
}
