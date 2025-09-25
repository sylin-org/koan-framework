using System;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Core.Pipelines;

/// <summary>
/// Core fluent extensions shared by pipeline consumers.
/// </summary>
public static class PipelineFluentExtensions
{
    /// <summary>
    /// Converts an asynchronous stream into a pipeline builder.
    /// </summary>
    public static PipelineBuilder<TEntity> Pipeline<TEntity>(this IAsyncEnumerable<TEntity> source)
        => source is null ? throw new ArgumentNullException(nameof(source)) : new PipelineBuilder<TEntity>(source);

    /// <summary>
    /// Applies a diagnostic tap to the pipeline.
    /// </summary>
    public static PipelineBuilder<TEntity> Tap<TEntity>(this IAsyncEnumerable<TEntity> source, Func<PipelineEnvelope<TEntity>, CancellationToken, Task> tap)
        => source.Pipeline().Tap(tap);

    /// <summary>
    /// Applies a diagnostic tap to the pipeline.
    /// </summary>
    public static PipelineBuilder<TEntity> Tap<TEntity>(this PipelineBuilder<TEntity> builder, Func<PipelineEnvelope<TEntity>, CancellationToken, Task> tap)
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        if (tap is null) throw new ArgumentNullException(nameof(tap));
        return builder.AddStage(tap);
    }

    /// <summary>
    /// Applies a synchronous tap to the pipeline.
    /// </summary>
    public static PipelineBuilder<TEntity> Tap<TEntity>(this IAsyncEnumerable<TEntity> source, Action<PipelineEnvelope<TEntity>> tap)
        => source.Pipeline().Tap(tap);

    /// <summary>
    /// Applies a synchronous tap to the pipeline.
    /// </summary>
    public static PipelineBuilder<TEntity> Tap<TEntity>(this PipelineBuilder<TEntity> builder, Action<PipelineEnvelope<TEntity>> tap)
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        if (tap is null) throw new ArgumentNullException(nameof(tap));
        return builder.AddStage((envelope, _) =>
        {
            tap(envelope);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Applies a synchronous tap to nested stage builders (branches).
    /// </summary>
    public static TBuilder Tap<TEntity, TBuilder>(this IPipelineStageBuilder<TEntity, TBuilder> builder, Action<PipelineEnvelope<TEntity>> tap)
        where TBuilder : IPipelineStageBuilder<TEntity, TBuilder>
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        if (tap is null) throw new ArgumentNullException(nameof(tap));
        return builder.AddStage((envelope, _) =>
        {
            tap(envelope);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Mutates the envelope in-place.
    /// </summary>
    public static TBuilder Mutate<TEntity, TBuilder>(this IPipelineStageBuilder<TEntity, TBuilder> builder, Action<PipelineEnvelope<TEntity>> mutate)
        where TBuilder : IPipelineStageBuilder<TEntity, TBuilder>
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        if (mutate is null) throw new ArgumentNullException(nameof(mutate));
        return builder.AddStage((envelope, _) =>
        {
            mutate(envelope);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Registers a branch stage on the pipeline.
    /// </summary>
    public static PipelineBuilder<TEntity> Branch<TEntity>(this IAsyncEnumerable<TEntity> source, Action<PipelineBranchBuilder<TEntity>> configure)
        => source.Pipeline().Branch(configure);

    /// <summary>
    /// Registers a branch stage on the pipeline.
    /// </summary>
    public static PipelineBuilder<TEntity> Branch<TEntity>(this PipelineBuilder<TEntity> builder, Action<PipelineBranchBuilder<TEntity>> configure)
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        if (configure is null) throw new ArgumentNullException(nameof(configure));
        var branchBuilder = new PipelineBranchBuilder<TEntity>();
        configure(branchBuilder);
        builder.AddStage(branchBuilder.Build());
        builder.Seal();
        return builder;
    }

    /// <summary>
    /// Executes the pipeline.
    /// </summary>
    public static Task ExecuteAsync<TEntity>(this PipelineBuilder<TEntity> builder, CancellationToken cancellationToken = default)
        => builder is null ? throw new ArgumentNullException(nameof(builder)) : builder.ExecuteAsync(cancellationToken);
}
