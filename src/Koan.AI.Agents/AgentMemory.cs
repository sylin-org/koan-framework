namespace Koan.AI.Agents;

/// <summary>
/// Abstract base for agent memory strategies. Use static factory methods
/// to create instances.
///
/// <code>
/// Agent.Create()
///     .WithMemory(AgentMemory.Sliding(maxTurns: 20))
///     .Run("...");
/// </code>
/// </summary>
public abstract record AgentMemory
{
    /// <summary>Sliding window memory: keeps the last N turns.</summary>
    public static AgentMemory Sliding(int maxTurns = 20) => new SlidingAgentMemory(maxTurns);

    /// <summary>Entity-backed memory: persists agent state in an entity store.</summary>
    public static AgentMemory Entity<T>() => new EntityAgentMemory(typeof(T));

    /// <summary>Semantic memory: retrieves relevant past interactions via vector search.</summary>
    public static AgentMemory Semantic<T>() => new SemanticAgentMemory(typeof(T));
}

internal sealed record SlidingAgentMemory(int MaxTurns) : AgentMemory;
internal sealed record EntityAgentMemory(Type EntityType) : AgentMemory;
internal sealed record SemanticAgentMemory(Type EntityType) : AgentMemory;
