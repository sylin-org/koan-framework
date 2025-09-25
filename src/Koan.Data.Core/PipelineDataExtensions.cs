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
            return ValueTask.CompletedTask;
        });
    }

    public static TBuilder Save<TEntity, TBuilder, TKey>(this IPipelineStageBuilder<TEntity, TBuilder> builder)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
        where TBuilder : IPipelineStageBuilder<TEntity, TBuilder>
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        return builder.AddStage(async (envelope, ct) =>
        {
            if (envelope.IsFaulted)
            {
                return;
            }

            await envelope.Entity.Save<TEntity, TKey>(ct).ConfigureAwait(false);
        });
    }

    public static TBuilder Save<TEntity, TBuilder>(this IPipelineStageBuilder<TEntity, TBuilder> builder)
        where TEntity : class, IEntity<string>
        where TBuilder : IPipelineStageBuilder<TEntity, TBuilder>
        => builder.Save<TEntity, TBuilder, string>();

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
