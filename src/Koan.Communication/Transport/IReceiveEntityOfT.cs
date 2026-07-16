using Koan.Data.Abstractions;

namespace Koan.Communication;

/// <summary>Receives isolated snapshots of <typeparamref name="TEntity"/> through Entity Transport.</summary>
/// <typeparam name="TEntity">The exact Entity contract handled by this receiver group.</typeparam>
public interface IReceiveEntity<TEntity> : IReceiveEntity
    where TEntity : class, IEntity
{
    /// <summary>Determines whether this receiver group accepts the snapshot.</summary>
    bool Where(TEntity entity) => true;

    /// <summary>Handles one accepted snapshot inside a Koan-owned service and context scope.</summary>
    Task Receive(TEntity entity, CancellationToken ct);
}
