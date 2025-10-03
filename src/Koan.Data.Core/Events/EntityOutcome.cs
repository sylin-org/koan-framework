using System;

namespace Koan.Data.Core.Events;

/// <summary>
/// Represents the lifecycle result for a single entity inside a batch.
/// </summary>
public sealed record EntityOutcome(object? Key, EntityEventOperation Operation, EntityEventResult Result)
{
    public bool Proceeded => !Result.IsCancelled;
}
