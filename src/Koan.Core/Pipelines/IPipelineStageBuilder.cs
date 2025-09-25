using System;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Core.Pipelines;

/// <summary>
/// Represents a builder that can register pipeline stages operating on envelopes.
/// </summary>
/// <typeparam name="TEntity">Pipeline entity type.</typeparam>
/// <typeparam name="TBuilder">Self type to support fluent chaining.</typeparam>
public interface IPipelineStageBuilder<TEntity, TBuilder>
    where TBuilder : IPipelineStageBuilder<TEntity, TBuilder>
{
    /// <summary>
    /// Adds an asynchronous stage to the builder.
    /// </summary>
    /// <param name="stage">Delegate invoked for each envelope.</param>
    /// <returns>The builder for fluent chaining.</returns>
    TBuilder AddStage(Func<PipelineEnvelope<TEntity>, CancellationToken, ValueTask> stage);
}
