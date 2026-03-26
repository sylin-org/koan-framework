using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Jobs.Model;

namespace Koan.Jobs.Recipes;

/// <summary>
/// Fluent builder for constructing immutable <see cref="JobRecipe{TJob, TContext, TResult}"/> instances.
/// Obtained via <see cref="Jobs.Recipe"/>.
/// </summary>
public sealed class JobRecipeBuilder
{
    private bool _persist;
    private string? _source;
    private string? _partition;
    private bool _audit;
    private readonly List<object> _defaults = [];

    public JobRecipeBuilder Persist(string? source = null, string? partition = null)
    {
        _persist = true;
        _source = source;
        _partition = partition;
        return this;
    }

    public JobRecipeBuilder Audit(bool enabled = true)
    {
        _audit = enabled;
        return this;
    }

    public JobRecipeBuilder WithDefaults<TJob>(Action<TJob> configure) where TJob : class
    {
        ArgumentNullException.ThrowIfNull(configure);
        _defaults.Add(configure);
        return this;
    }

    public JobRecipe<TJob, TContext, TResult> Build<TJob, TContext, TResult>()
        where TJob : Job<TJob, TContext, TResult>, new()
    {
        var typedDefaults = _defaults.OfType<Action<TJob>>().ToList();

        return new JobRecipe<TJob, TContext, TResult>
        {
            Source = _source,
            Partition = _partition,
            Persist = _persist,
            Audit = _audit,
            Defaults = typedDefaults
        };
    }
}
