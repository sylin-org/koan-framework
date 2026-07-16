using Koan.Data.Abstractions;

namespace Koan.Communication;

/// <summary>Handles typed business occurrences associated with <typeparamref name="TEntity"/>.</summary>
public interface IHandleEntityEvent<TEntity, TEvent> : IHandleEntityEvent
    where TEntity : class, IEntity
    where TEvent : class
{
    /// <summary>Determines whether this subscription handles the occurrence.</summary>
    bool Where(TEntity entity, EventOccurrence<TEvent> occurrence) => true;

    /// <summary>Handles one occurrence inside a Koan-owned service and context scope.</summary>
    Task Handle(TEntity entity, EventOccurrence<TEvent> occurrence, CancellationToken ct);
}
