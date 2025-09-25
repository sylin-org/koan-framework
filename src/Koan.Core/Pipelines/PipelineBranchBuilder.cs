using System;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Core.Pipelines;

/// <summary>
/// Configures success and failure paths for a branch stage.
/// </summary>
/// <typeparam name="TEntity">Pipeline entity type.</typeparam>
public sealed class PipelineBranchBuilder<TEntity>
{
    private readonly PipelineBranchStageBuilder<TEntity> _success = new();
    private readonly PipelineBranchStageBuilder<TEntity> _failure = new();

    internal PipelineBranchStageBuilder<TEntity> Success => _success;
    internal PipelineBranchStageBuilder<TEntity> Failure => _failure;

    /// <summary>
    /// Configures the success branch.
    /// </summary>
    public PipelineBranchBuilder<TEntity> OnSuccess(Action<PipelineBranchStageBuilder<TEntity>> configure)
    {
        if (configure is null) throw new ArgumentNullException(nameof(configure));
        configure(_success);
        return this;
    }

    /// <summary>
    /// Configures the failure branch.
    /// </summary>
    public PipelineBranchBuilder<TEntity> OnFailure(Action<PipelineBranchStageBuilder<TEntity>> configure)
    {
        if (configure is null) throw new ArgumentNullException(nameof(configure));
        configure(_failure);
        return this;
    }

    internal Func<PipelineEnvelope<TEntity>, CancellationToken, ValueTask> Build()
    {
        var success = _success.Build();
        var failure = _failure.Build();
        return async (envelope, ct) =>
        {
            var stages = envelope.IsFaulted ? failure : success;
            foreach (var stage in stages)
            {
                await stage(envelope, ct).ConfigureAwait(false);
                if (envelope.IsCompleted)
                {
                    break;
                }
            }
            envelope.Complete();
        };
    }
}
