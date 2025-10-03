namespace Koan.Data.Core.Events;

/// <summary>
/// Mutable operation scope shared across lifecycle hooks for a single entity operation.
/// </summary>
public sealed class EntityEventOperationState
{
    private bool _requireAtomic;

    /// <summary>
    /// Marks the surrounding batch as requiring atomic execution.
    /// </summary>
    public void RequireAtomic() => _requireAtomic = true;

    /// <summary>
    /// Gets a value indicating whether atomic semantics are required.
    /// </summary>
    public bool IsAtomic => _requireAtomic;
}
