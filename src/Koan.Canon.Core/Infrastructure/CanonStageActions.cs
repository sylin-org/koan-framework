using Koan.Data.Core.Model;

namespace Koan.Canon.Core.Infrastructure;

/// <summary>
/// Enhanced action types for Canon stage interceptors with comprehensive control options
/// </summary>
public static class CanonStageActions
{
    public static CanonStageAction Continue(object entity) => 
        new CanonStageAction(CanonStageActionType.Continue, entity);
    
    public static CanonStageAction Skip(object entity, string? reason = null) => 
        new CanonStageAction(CanonStageActionType.Skip, entity, reason);
    
    public static CanonStageAction Defer(object entity, TimeSpan delay, string? reason = null) => 
        new CanonStageAction(CanonStageActionType.Defer, entity, reason) { Delay = delay };
    
    public static CanonStageAction Retry(object entity, int maxAttempts, string? reason = null) => 
        new CanonStageAction(CanonStageActionType.Retry, entity, reason) { MaxAttempts = maxAttempts };
    
    public static CanonStageAction Park(object entity, string reasonCode, string? evidence = null) => 
        new CanonStageAction(CanonStageActionType.Park, entity, reasonCode) { Evidence = evidence };
    
    public static CanonStageAction Transform(object original, object transformed, string? reason = null) =>
        new CanonStageAction(CanonStageActionType.Transform, transformed, reason) { OriginalEntity = original };
}

/// <summary>
/// Canon stage action types for comprehensive pipeline control
/// </summary>
public enum CanonStageActionType
{
    /// <summary>Continue with normal stage processing</summary>
    Continue,
    /// <summary>Skip this stage but continue to next stage</summary>
    Skip, 
    /// <summary>Defer processing for specified time period</summary>
    Defer,
    /// <summary>Retry stage processing with attempt limit</summary>
    Retry,
    /// <summary>Park entity for manual resolution</summary>
    Park,
    /// <summary>Transform entity before stage processing</summary>
    Transform
}

/// <summary>
/// Represents the result of a Canon stage interceptor execution
/// </summary>
public class CanonStageAction
{
    public CanonStageActionType ActionType { get; }
    public object Entity { get; }
    public string? Reason { get; }
    public TimeSpan? Delay { get; set; }
    public int? MaxAttempts { get; set; }
    public string? Evidence { get; set; }
    public object? OriginalEntity { get; set; }
    
    /// <summary>Indicates if this action should stop the current stage processing</summary>
    public bool ShouldStop => ActionType != CanonStageActionType.Continue;
    
    /// <summary>Indicates if processing should continue to next stage</summary>
    public bool ShouldContinueToNextStage => ActionType is CanonStageActionType.Continue or CanonStageActionType.Skip or CanonStageActionType.Transform;
    
    public CanonStageAction(CanonStageActionType actionType, object entity, string? reason = null)
    {
        ActionType = actionType;
        Entity = entity;
        Reason = reason;
    }
}

