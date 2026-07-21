using System.ComponentModel;
using System.Runtime.CompilerServices;
using Koan.Data.Abstractions;

namespace Koan.Data.Core.Selection;

/// <summary>
/// Normalizes one Entity, a finite Entity sequence, or an asynchronous Entity stream into the one
/// lazy source shape consumed by module-owned capabilities.
/// </summary>
/// <remarks>
/// This is framework infrastructure, not an application flow API. It owns only cardinality,
/// ordinal, multiplicity, laziness, and cancellation. Capability callbacks, context capture,
/// batching, outcomes, routing, and policy remain with the consuming pillar.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class EntityCardinality
{
    /// <summary>Creates a lazy source containing exactly one Entity.</summary>
    public static IAsyncEnumerable<TEntity> One<TEntity>(
        TEntity entity,
        CancellationToken ct = default)
        where TEntity : class, IEntity
    {
        ArgumentNullException.ThrowIfNull(entity);
        return Many([entity], ct);
    }

    /// <summary>Creates a lazy, one-pass source over a finite Entity sequence.</summary>
    public static async IAsyncEnumerable<TEntity> Many<TEntity>(
        IEnumerable<TEntity> entities,
        [EnumeratorCancellation] CancellationToken ct = default)
        where TEntity : class, IEntity
    {
        ArgumentNullException.ThrowIfNull(entities);

        var ordinal = 0L;
        foreach (var entity in entities)
        {
            ct.ThrowIfCancellationRequested();
            if (entity is null)
            {
                throw new InvalidOperationException(
                    $"The Entity source yielded null at ordinal {ordinal}. " +
                    "Remove null entries before invoking an Entity capability.");
            }

            yield return entity;
            ordinal++;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>Preserves a lazy asynchronous Entity stream while applying terminal cancellation.</summary>
    public static async IAsyncEnumerable<TEntity> Stream<TEntity>(
        IAsyncEnumerable<TEntity> entities,
        [EnumeratorCancellation] CancellationToken ct = default)
        where TEntity : class, IEntity
    {
        ArgumentNullException.ThrowIfNull(entities);

        var ordinal = 0L;
        await foreach (var entity in entities.WithCancellation(ct).ConfigureAwait(false))
        {
            if (entity is null)
            {
                throw new InvalidOperationException(
                    $"The Entity stream yielded null at ordinal {ordinal}. " +
                    "Remove null entries before invoking an Entity capability.");
            }

            yield return entity;
            ordinal++;
        }
    }
}
