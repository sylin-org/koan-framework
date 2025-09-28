using System;

namespace Koan.Data.Core.Events;

/// <summary>
/// Raised when an entity lifecycle hook cancels the ongoing operation.
/// </summary>
public sealed class EntityEventCancelledException : InvalidOperationException
{
    public EntityEventCancelledException(EntityEventOperation operation, string reason, string? code)
        : base(reason)
    {
        Operation = operation;
        ReasonCode = code;
    }

    public EntityEventOperation Operation { get; }

    public string? ReasonCode { get; }
}
