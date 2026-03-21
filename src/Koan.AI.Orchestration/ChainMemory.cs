namespace Koan.AI.Orchestration;

/// <summary>
/// Abstract base for chain memory strategies. Use static factory methods
/// to create instances.
///
/// <code>
/// Chain.Create()
///     .WithMemory(ChainMemory.Sliding(maxTurns: 10))
///     .Chat("...")
///     .Run();
/// </code>
/// </summary>
public abstract record ChainMemory
{
    /// <summary>Sliding window memory: keeps the last N turns.</summary>
    public static ChainMemory Sliding(int maxTurns = 10) => new SlidingMemory(maxTurns);

    /// <summary>Summary-based memory: compresses history into a running summary.</summary>
    public static ChainMemory Summary() => new SummaryMemory();

    /// <summary>Entity-backed memory: persists conversation state in an entity store.</summary>
    public static ChainMemory Entity<T>() => new EntityMemory(typeof(T));
}

internal sealed record SlidingMemory(int MaxTurns) : ChainMemory;
internal sealed record SummaryMemory : ChainMemory;
internal sealed record EntityMemory(Type EntityType) : ChainMemory;
