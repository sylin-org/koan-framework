namespace Koan.AI.Agents;

/// <summary>
/// A single step in the agent's reasoning loop.
/// </summary>
public sealed record AgentStep
{
    /// <summary>The agent's reasoning for this step (null for pure tool calls).</summary>
    public string? Reasoning { get; init; }

    /// <summary>Tool call made in this step (null for reasoning-only steps).</summary>
    public ToolCall? ToolCall { get; init; }

    /// <summary>Observation from tool execution (null if no tool was called).</summary>
    public string? Observation { get; init; }

    /// <summary>Tokens consumed in this step.</summary>
    public required int TokensUsed { get; init; }
}

/// <summary>
/// A tool invocation by the agent.
/// </summary>
public sealed record ToolCall(string Name, Dictionary<string, object?> Arguments);
