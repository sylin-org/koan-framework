namespace Koan.Data.Core.Lifecycle;

/// <summary>Raised when a before-lifecycle handler rejects a persistence operation.</summary>
public sealed class EntityLifecycleCancelledException : InvalidOperationException
{
    public EntityLifecycleCancelledException(EntityLifecycleOperation operation, string reason, string? code)
        : base(reason)
    {
        Operation = operation;
        ReasonCode = code;
    }

    public EntityLifecycleOperation Operation { get; }
    public string? ReasonCode { get; }
}
