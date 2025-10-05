namespace Koan.Canon.Core.Interceptors;

/// <summary>
/// Action types for Canon intake interceptors
/// </summary>
public enum CanonIntakeActionType
{
    Continue,
    Drop,
    Park,
    Transform
}

/// <summary>
/// Represents the result of a Canon intake interceptor execution
/// </summary>
public class CanonIntakeAction
{
    public CanonIntakeActionType Action { get; }
    public object Entity { get; }
    public string? Reason { get; }
    public object? OriginalEntity { get; set; }

    /// <summary>Indicates if this action should stop the intake processing</summary>
    public bool ShouldStop => Action != CanonIntakeActionType.Continue;

    public CanonIntakeAction(CanonIntakeActionType action, object entity, string? reason = null)
    {
        Action = action;
        Entity = entity;
        Reason = reason;
    }
}

/// <summary>
/// Helper methods for creating Canon intake actions
/// </summary>
public static class CanonIntakeActions
{
    /// <summary>
    /// Continue normal processing with the given entity.
    /// </summary>
    public static CanonIntakeAction Continue(object entity) => new(CanonIntakeActionType.Continue, entity);

    /// <summary>
    /// Drop the entity entirely - skip all processing.
    /// </summary>
    public static CanonIntakeAction Drop(object entity, string? reason = null) => new(CanonIntakeActionType.Drop, entity, reason);

    /// <summary>
    /// Park the entity with the specified status for later processing.
    /// </summary>
    public static CanonIntakeAction Park(object entity, string reasonCode, string? evidence = null) =>
        new(CanonIntakeActionType.Park, entity, reasonCode) { OriginalEntity = evidence };

    /// <summary>
    /// Transform the entity before processing.
    /// </summary>
    public static CanonIntakeAction Transform(object original, object transformed, string? reason = null) =>
        new(CanonIntakeActionType.Transform, transformed, reason) { OriginalEntity = original };
}
