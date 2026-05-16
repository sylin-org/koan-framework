using System;
using System.Collections.Generic;
using System.Threading;
using Koan.Jobs.Execution;
using Koan.Jobs.Model;
using Koan.Jobs.Support;

namespace Koan.Jobs.Recipes;

/// <summary>
/// Immutable recipe capturing job configuration defaults.
/// Created via <see cref="Jobs.Recipe"/> builder. Applied when starting jobs.
/// </summary>
public sealed class JobRecipe<TJob, TContext, TResult>
    where TJob : Job<TJob, TContext, TResult>, new()
{
    public string? Source { get; init; }
    public string? Partition { get; init; }
    public bool Persist { get; init; }
    public bool Audit { get; init; }
    public IReadOnlyList<Action<TJob>> Defaults { get; init; } = [];

    /// <summary>
    /// Start a job using this recipe's captured defaults.
    /// Returns the same <see cref="JobRunBuilder{TJob, TContext, TResult}"/> as
    /// <see cref="Job{TJob, TContext, TResult}.Start"/> so callers can chain
    /// additional overrides before calling <c>.Run()</c>.
    /// </summary>
    public JobRunBuilder<TJob, TContext, TResult> Start(
        TContext context,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        var builder = JobEnvironment.CreateBuilder<TJob, TContext, TResult>(context, correlationId, cancellationToken);

        if (Persist)
            builder = builder.Persist(Source, Partition);

        if (Audit)
            builder = builder.Audit();

        foreach (var configure in Defaults)
            builder = builder.With(configure);

        return builder;
    }
}
