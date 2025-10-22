using System;
using System.Collections.Generic;
using Koan.Jobs.Execution;
using Koan.Jobs.Model;

namespace Koan.Jobs.Extensions;

public static class JobRunBuilderExtensions
{
    public static JobRunBuilder<TJob, TContext, TResult> With<TJob, TContext, TResult>(
        this JobRunBuilder<TJob, TContext, TResult> builder,
        string? tenantId = null,
        string? userId = null,
        string? correlationId = null,
        Action<IDictionary<string, object?>>? metadata = null)
        where TJob : Job<TJob, TContext, TResult>, new()
    {
        return builder.With(job => job.With(tenantId, userId, correlationId, metadata));
    }

    public static JobRunBuilder<TJob, TContext, TResult> WithMetadata<TJob, TContext, TResult>(
        this JobRunBuilder<TJob, TContext, TResult> builder,
        string key,
        object? value)
        where TJob : Job<TJob, TContext, TResult>, new()
    {
        return builder.With(job => job.WithMetadata(key, value));
    }
}
