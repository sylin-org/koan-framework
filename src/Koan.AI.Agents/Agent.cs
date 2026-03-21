namespace Koan.AI.Agents;

/// <summary>
/// Entry point for building entity-aware autonomous agents.
///
/// <code>
/// var result = await Agent.Create()
///     .System("You manage support tickets.")
///     .WithEntities&lt;SupportTicket&gt;(write: true)
///     .WithSearch&lt;KnowledgeBase&gt;()
///     .WithPlanning(PlanStrategy.ReAct)
///     .Run("Resolve all open P1 tickets using the knowledge base.");
/// </code>
/// </summary>
public static class Agent
{
    /// <summary>Create a new agent builder.</summary>
    public static AgentBuilder Create() => new();
}
