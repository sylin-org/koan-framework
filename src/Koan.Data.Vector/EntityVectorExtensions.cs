using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Abstractions;
using Koan.Data.Vector.Abstractions;

namespace Koan.Data.Vector;

public static class EntityVectorExtensions
{
	public static Task SaveVectorAsync<TEntity>(
		this TEntity entity,
		float[] embedding,
		object? metadata = null,
		string? profileName = null,
		CancellationToken ct = default)
		where TEntity : class, IEntity<string>
	{
		System.ArgumentNullException.ThrowIfNull(entity);
		System.ArgumentNullException.ThrowIfNull(embedding);
		return VectorWorkflow<TEntity>.Save(entity, embedding, metadata, profileName, ct);
	}

	public static Task<VectorWorkflowSaveManyResult> SaveVectorsAsync<TEntity>(
		this IEnumerable<(TEntity Entity, float[] Embedding, object? Metadata)> items,
		string? profileName = null,
		CancellationToken ct = default)
		where TEntity : class, IEntity<string>
	{
		System.ArgumentNullException.ThrowIfNull(items);
		return VectorWorkflow<TEntity>.SaveMany(items, profileName, ct);
	}
}
