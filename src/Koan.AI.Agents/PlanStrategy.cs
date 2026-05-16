namespace Koan.AI.Agents;

/// <summary>
/// Planning and reasoning strategy for agent execution.
/// </summary>
public enum PlanStrategy
{
    /// <summary>Reason-Act loop: think, act, observe, repeat.</summary>
    ReAct,

    /// <summary>Direct function calling without explicit reasoning steps.</summary>
    FunctionCalling,

    /// <summary>Create a plan first, then execute each step sequentially.</summary>
    PlanAndExecute
}
