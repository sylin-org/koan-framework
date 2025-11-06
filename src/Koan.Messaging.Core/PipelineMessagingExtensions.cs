using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core.Pipelines;

namespace Koan.Messaging;

/// <summary>
/// Messaging helpers for the semantic pipeline DSL.
/// </summary>
public static class PipelineMessagingExtensions
{
    public static PipelineBuilder<TEntity> Notify<TEntity>(this IAsyncEnumerable<TEntity> source, Func<TEntity, object?> messageFactory)
        => source.Pipeline().Notify(messageFactory);

    public static PipelineBuilder<TEntity> Notify<TEntity>(this PipelineBuilder<TEntity> builder, Func<TEntity, object?> messageFactory)
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        if (messageFactory is null) throw new ArgumentNullException(nameof(messageFactory));
        return builder.AddStage(async (envelope, ct) =>
        {
            if (envelope.IsFaulted) return;

            var message = messageFactory(envelope.Entity);
            if (message is null) return;

            try
            {
                await MessagingExtensions.Send(message, cancellationToken: ct);
                envelope.Features[PipelineFeatureKeys.Notification] = message;
            }
            catch (Exception ex)
            {
                envelope.RecordError(ex);
            }
        });
    }

    public static TBuilder Notify<TEntity, TBuilder>(this IPipelineStageBuilder<TEntity, TBuilder> builder, Func<PipelineEnvelope<TEntity>, object?> messageFactory)
        where TBuilder : IPipelineStageBuilder<TEntity, TBuilder>
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        if (messageFactory is null) throw new ArgumentNullException(nameof(messageFactory));
        return builder.AddStage(async (envelope, ct) =>
        {
            if (!envelope.IsFaulted) return;

            var message = messageFactory(envelope);
            if (message is null) return;

            try
            {
                await MessagingExtensions.Send(message, cancellationToken: ct);
                envelope.Features[PipelineFeatureKeys.Notification] = message;
            }
            catch (Exception ex)
            {
                envelope.RecordError(ex);
            }
        });
    }
}
