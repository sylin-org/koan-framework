using System;
using System.Collections.Generic;

namespace Koan.Data.Core.Events;

/// <summary>
/// Raised when an entity batch operation is cancelled due to lifecycle guardrails.
/// </summary>
public sealed class EntityEventBatchCancelledException : InvalidOperationException
{
    public EntityEventBatchCancelledException(EntityEventOperation operation, IReadOnlyList<EntityOutcome> outcomes)
        : base($"{operation} batch cancelled by lifecycle hook.")
    {
        Operation = operation;
        Outcomes = outcomes;
    }

    public EntityEventOperation Operation { get; }

    public IReadOnlyList<EntityOutcome> Outcomes { get; }
}
