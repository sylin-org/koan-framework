using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core.Pipelines;
using Koan.Data.Abstractions;

namespace Koan.Data.Vector;

/// <summary>
/// Vector store helpers for the semantic pipeline DSL.
/// </summary>
public static class PipelineVectorExtensions
{
    public static PipelineBuilder<TEntity> Store<TEntity>(this IAsyncEnumerable<TEntity> source, Func<TEntity, object?> metadataFactory)
        where TEntity : class, IEntity<string>
        => source.Pipeline().Store(metadataFactory);

    public static PipelineBuilder<TEntity> Store<TEntity>(this PipelineBuilder<TEntity> builder, Func<TEntity, object?> metadataFactory)
        where TEntity : class, IEntity<string>
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        if (metadataFactory is null) throw new ArgumentNullException(nameof(metadataFactory));
        return builder.AddStage(async (envelope, ct) =>
        {
            if (envelope.IsFaulted)
            {
                return;
            }

            if (!envelope.Features.TryGetValue(PipelineFeatureKeys.Embedding, out var embeddingObj) || embeddingObj is not float[] embedding)
            {
                envelope.RecordError(new InvalidOperationException("Vector store stage requires an embedding feature."));
                return;
            }

            var metadata = metadataFactory(envelope.Entity);
            try
            {
                var affected = await Vector<TEntity>.Save((envelope.Entity.Id!, embedding, metadata), ct).ConfigureAwait(false);
                envelope.Metadata["vector:affected"] = affected;
            }
            catch (Exception ex)
            {
                envelope.RecordError(ex);
            }
        });
    }

    public static TBuilder Store<TEntity, TBuilder>(this IPipelineStageBuilder<TEntity, TBuilder> builder, Func<PipelineEnvelope<TEntity>, object?> metadataFactory)
        where TEntity : class, IEntity<string>
        where TBuilder : IPipelineStageBuilder<TEntity, TBuilder>
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        if (metadataFactory is null) throw new ArgumentNullException(nameof(metadataFactory));
        return builder.AddStage(async (envelope, ct) =>
        {
            if (!envelope.Features.TryGetValue(PipelineFeatureKeys.Embedding, out var embeddingObj) || embeddingObj is not float[] embedding)
            {
                envelope.RecordError(new InvalidOperationException("Vector store stage requires an embedding feature."));
                return;
            }

            var metadata = metadataFactory(envelope);
            try
            {
                var affected = await Vector<TEntity>.Save((envelope.Entity.Id!, embedding, metadata), ct).ConfigureAwait(false);
                envelope.Metadata["vector:affected"] = affected;
            }
            catch (Exception ex)
            {
                envelope.RecordError(ex);
            }
        });
    }
}
