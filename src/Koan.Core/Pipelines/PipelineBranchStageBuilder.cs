using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Core.Pipelines;

/// <summary>
/// Builds a sequence of stages executed when a branch is activated.
/// </summary>
/// <typeparam name="TEntity">Pipeline entity type.</typeparam>
public sealed class PipelineBranchStageBuilder<TEntity> : IPipelineStageBuilder<TEntity, PipelineBranchStageBuilder<TEntity>>
{
    private readonly List<Func<PipelineEnvelope<TEntity>, CancellationToken, Task>> _stages = new();

    PipelineBranchStageBuilder<TEntity> IPipelineStageBuilder<TEntity, PipelineBranchStageBuilder<TEntity>>.AddStage(
        Func<PipelineEnvelope<TEntity>, CancellationToken, Task> stage)
        => AddStage(stage);

    public PipelineBranchStageBuilder<TEntity> AddStage(Func<PipelineEnvelope<TEntity>, CancellationToken, Task> stage)
    {
        if (stage is null) throw new ArgumentNullException(nameof(stage));
        _stages.Add(stage);
        return this;
    }

    internal IReadOnlyList<Func<PipelineEnvelope<TEntity>, CancellationToken, Task>> Build() => _stages;
}
