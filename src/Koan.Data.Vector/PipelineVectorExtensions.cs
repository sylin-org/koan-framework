using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core.Pipelines;
using Koan.Data.Abstractions;
using Koan.Data.Core;

namespace Koan.Data.Vector;

/// <summary>
/// Vector-aware pipeline helpers layered on top of the core data pipeline extensions.
/// </summary>
public static class PipelineVectorExtensions
{
	/// <summary>
	/// Persists pipeline entities and associated embeddings when present.
	/// Falls back to the standard entity save when the envelope lacks vector features.
	/// </summary>
	public static PipelineBuilder<TEntity> SaveWithVectors<TEntity>(this PipelineBuilder<TEntity> builder)
		where TEntity : class, IEntity<string>
	{
		ArgumentNullException.ThrowIfNull(builder);
		return (PipelineBuilder<TEntity>)builder.AddStage(async (envelope, ct) =>
		{
			if (envelope.IsFaulted)
			{
				return;
			}

			try
			{
				if (TryGetEmbedding(envelope, out var embedding, out var metadata))
				{
					await VectorData<TEntity>.SaveWithVector(envelope.Entity, embedding, metadata, ct);
					envelope.Metadata["vector:affected"] = 1;
				}
				else
				{
					await envelope.Entity.Save(ct);
				}
			}
			catch (InvalidOperationException)
			{
				await envelope.Entity.Save(ct);
				envelope.Metadata["vector:affected"] = 0;
			}
			catch (Exception ex)
			{
				envelope.RecordError(ex);
			}
		});
	}

	/// <summary>
	/// Vector-aware save stage for pipeline branches.
	/// </summary>
	public static PipelineBranchStageBuilder<TEntity> SaveWithVectors<TEntity>(this PipelineBranchStageBuilder<TEntity> builder)
		where TEntity : class, IEntity<string>
	{
		ArgumentNullException.ThrowIfNull(builder);
		return builder.AddStage(async (envelope, ct) =>
		{
			if (envelope.IsFaulted)
			{
				return;
			}

			try
			{
				if (TryGetEmbedding(envelope, out var embedding, out var metadata))
				{
					await VectorData<TEntity>.SaveWithVector(envelope.Entity, embedding, metadata, ct);
					envelope.Metadata["vector:affected"] = 1;
				}
				else
				{
					await envelope.Entity.Save(ct);
				}
			}
			catch (InvalidOperationException)
			{
				await envelope.Entity.Save(ct);
				envelope.Metadata["vector:affected"] = 0;
			}
			catch (Exception ex)
			{
				envelope.RecordError(ex);
			}
		});
	}

	private static bool TryGetEmbedding<TEntity>(PipelineEnvelope<TEntity> envelope, out ReadOnlyMemory<float> embedding, out IReadOnlyDictionary<string, object>? metadata)
		where TEntity : class, IEntity<string>
	{
		metadata = null;
		embedding = default;
		if (!envelope.Features.TryGetValue(PipelineFeatureKeys.Embedding, out var embeddingObj) || embeddingObj is not float[] values)
		{
			return false;
		}

		metadata = envelope.Features.TryGetValue("vector:metadata", out var metadataObj)
			? metadataObj as IReadOnlyDictionary<string, object>
			: null;
		embedding = values;
		return true;
	}
}