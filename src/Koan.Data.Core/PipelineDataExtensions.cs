using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core.Pipelines;
using Koan.Data.Abstractions;

namespace Koan.Data.Core;

/// <summary>
/// Data-specific helpers for the semantic pipeline.
/// </summary>
public static class PipelineDataExtensions
{
    public static PipelineBuilder<TEntity> ForEach<TEntity>(this IAsyncEnumerable<TEntity> source, Func<TEntity, ValueTask> action)
        => source.Pipeline().ForEach(action);

    public static PipelineBuilder<TEntity> ForEach<TEntity>(this PipelineBuilder<TEntity> builder, Func<TEntity, ValueTask> action)
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        if (action is null) throw new ArgumentNullException(nameof(action));
        return builder.AddStage(async (envelope, _) =>
        {
            await action(envelope.Entity).ConfigureAwait(false);
        });
    }

    public static PipelineBuilder<TEntity> ForEach<TEntity>(this IAsyncEnumerable<TEntity> source, Action<TEntity> action)
        => source.Pipeline().ForEach(action);

    public static PipelineBuilder<TEntity> ForEach<TEntity>(this PipelineBuilder<TEntity> builder, Action<TEntity> action)
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        if (action is null) throw new ArgumentNullException(nameof(action));
        return builder.AddStage((envelope, _) =>
        {
            action(envelope.Entity);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Saves entities by unwrapping the envelope and calling the entity's standard Save method.
    /// Clean, semantic, no type pollution.
    /// </summary>
    public static PipelineBuilder<TEntity> Save<TEntity>(this PipelineBuilder<TEntity> builder)
        where TEntity : class, IEntity<string>
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        return (PipelineBuilder<TEntity>)builder.AddStage(async (envelope, ct) =>
        {
            if (envelope.IsFaulted) return;

            try
            {
                await envelope.Entity.Save(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                envelope.RecordError(ex);
            }
        });
    }

    /// <summary>
    /// Saves entities in branch stages by unwrapping and calling entity's Save method.
    /// </summary>
    public static PipelineBranchStageBuilder<TEntity> Save<TEntity>(this PipelineBranchStageBuilder<TEntity> builder)
        where TEntity : class, IEntity<string>
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        return builder.AddStage(async (envelope, ct) =>
        {
            if (envelope.IsFaulted) return;

            try
            {
                await envelope.Entity.Save(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                envelope.RecordError(ex);
            }
        });
    }

    /// <summary>
    /// Simple batching helper that yields materialised windows while honouring backpressure.
    /// </summary>
    public static async IAsyncEnumerable<IReadOnlyList<TEntity>> Batch<TEntity>(this IAsyncEnumerable<TEntity> source, int size, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));

        var buffer = new List<TEntity>(size);
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            buffer.Add(item);
            if (buffer.Count == size)
            {
                yield return buffer.ToArray();
                buffer.Clear();
            }
        }

        if (buffer.Count > 0)
        {
            yield return buffer.ToArray();
        }
    }
}
