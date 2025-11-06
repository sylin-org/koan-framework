using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Core.Pipelines;

/// <summary>
/// Builds and executes semantic pipelines backed by <see cref="IAsyncEnumerable{T}"/> sources.
/// </summary>
/// <typeparam name="TEntity">Entity type flowing through the pipeline.</typeparam>
public sealed class PipelineBuilder<TEntity> : IPipelineStageBuilder<TEntity, PipelineBuilder<TEntity>>
{
    private readonly IAsyncEnumerable<TEntity> _source;
    private readonly List<Func<PipelineEnvelope<TEntity>, CancellationToken, Task>> _stages = new();
    private bool _sealed;

    internal PipelineBuilder(IAsyncEnumerable<TEntity> source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    PipelineBuilder<TEntity> IPipelineStageBuilder<TEntity, PipelineBuilder<TEntity>>.AddStage(
        Func<PipelineEnvelope<TEntity>, CancellationToken, Task> stage)
        => AddStage(stage);

    public PipelineBuilder<TEntity> AddStage(Func<PipelineEnvelope<TEntity>, CancellationToken, Task> stage)
    {
        if (stage is null) throw new ArgumentNullException(nameof(stage));
        if (_sealed) throw new InvalidOperationException("Pipeline is sealed; no stages can be appended after branching.");
        _stages.Add(stage);
        return this;
    }

    internal void Seal() => _sealed = true;

    /// <summary>
    /// Executes the pipeline and drains the source stream.
    /// </summary>
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        await foreach (var entity in _source.WithCancellation(cancellationToken))
        {
            var envelope = new PipelineEnvelope<TEntity>(entity);
            foreach (var stage in _stages)
            {
                await stage(envelope, cancellationToken);
                if (envelope.IsCompleted)
                {
                    break;
                }
            }
        }
    }
}
