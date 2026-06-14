namespace Koan.Rag.Abstractions;

/// <summary>
/// Captures the retrieval agent's decision-making process for debugging and evaluation.
/// </summary>
public sealed record RagRetrievalTrace
{
    /// <summary>Ordered list of tool invocations the agent performed.</summary>
    public IReadOnlyList<RagToolInvocation> Steps { get; init; } = [];

    /// <summary>Total retrieval rounds executed.</summary>
    public int RoundsExecuted { get; init; }

    /// <summary>Total chunks retrieved across all rounds before reranking.</summary>
    public int TotalChunksRetrieved { get; init; }

    /// <summary>Chunks that survived reranking and contributed to generation.</summary>
    public int ChunksUsedInGeneration { get; init; }

    /// <summary>Time spent in retrieval (excluding generation).</summary>
    public TimeSpan RetrievalLatency { get; init; }
}

/// <summary>
/// A single tool invocation within the retrieval trace.
/// </summary>
public sealed record RagToolInvocation(
    string ToolName,
    string? Input,
    int ResultCount,
    TimeSpan Latency);
