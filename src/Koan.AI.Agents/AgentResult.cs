namespace Koan.AI.Agents;

/// <summary>
/// Result of a completed agent execution.
/// </summary>
public sealed record AgentResult
{
    /// <summary>Final text output from the agent.</summary>
    public required string Text { get; init; }

    /// <summary>Completion status.</summary>
    public required AgentStatus Status { get; init; }

    /// <summary>All reasoning and tool-call steps taken.</summary>
    public required IReadOnlyList<AgentStep> Steps { get; init; }

    /// <summary>Number of reasoning iterations performed.</summary>
    public required int Iterations { get; init; }

    /// <summary>Total tokens consumed across all iterations.</summary>
    public required int TotalTokens { get; init; }

    /// <summary>Total wall-clock duration.</summary>
    public required TimeSpan Duration { get; init; }
}

/// <summary>
/// Agent completion status.
/// </summary>
public enum AgentStatus
{
    /// <summary>Agent completed its goal successfully.</summary>
    Completed,

    /// <summary>Agent hit the maximum iteration limit.</summary>
    IterationLimitReached,

    /// <summary>Agent exhausted its token budget.</summary>
    BudgetExhausted
}
