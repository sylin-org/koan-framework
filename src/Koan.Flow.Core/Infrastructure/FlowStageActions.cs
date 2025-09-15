using Koan.Data.Core.Model;

namespace Koan.Flow.Core.Infrastructure;

/// <summary>
/// Enhanced action types for Flow stage interceptors with comprehensive control options
/// </summary>
public static class FlowStageActions
{
    public static FlowStageAction Continue(object entity) => 
        new FlowStageAction(FlowStageActionType.Continue, entity);
    
    public static FlowStageAction Skip(object entity, string? reason = null) => 
        new FlowStageAction(FlowStageActionType.Skip, entity, reason);
    
    public static FlowStageAction Defer(object entity, TimeSpan delay, string? reason = null) => 
        new FlowStageAction(FlowStageActionType.Defer, entity, reason) { Delay = delay };
    
    public static FlowStageAction Retry(object entity, int maxAttempts, string? reason = null) => 
        new FlowStageAction(FlowStageActionType.Retry, entity, reason) { MaxAttempts = maxAttempts };
    
    public static FlowStageAction Park(object entity, string reasonCode, string? evidence = null) => 
        new FlowStageAction(FlowStageActionType.Park, entity, reasonCode) { Evidence = evidence };
    
    public static FlowStageAction Transform(object original, object transformed, string? reason = null) =>
        new FlowStageAction(FlowStageActionType.Transform, transformed, reason) { OriginalEntity = original };
}

/// <summary>
/// Flow stage action types for comprehensive pipeline control
/// </summary>
public enum FlowStageActionType
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
/// Represents the result of a Flow stage interceptor execution
/// </summary>
public class FlowStageAction
{
    public FlowStageActionType ActionType { get; }
    public object Entity { get; }
    public string? Reason { get; }
    public TimeSpan? Delay { get; set; }
    public int? MaxAttempts { get; set; }
    public string? Evidence { get; set; }
    public object? OriginalEntity { get; set; }
    
    /// <summary>Indicates if this action should stop the current stage processing</summary>
    public bool ShouldStop => ActionType != FlowStageActionType.Continue;
    
    /// <summary>Indicates if processing should continue to next stage</summary>
    public bool ShouldContinueToNextStage => ActionType is FlowStageActionType.Continue or FlowStageActionType.Skip or FlowStageActionType.Transform;
    
    public FlowStageAction(FlowStageActionType actionType, object entity, string? reason = null)
    {
        ActionType = actionType;
        Entity = entity;
        Reason = reason;
    }
}