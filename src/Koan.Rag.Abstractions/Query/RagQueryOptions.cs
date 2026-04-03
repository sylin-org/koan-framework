namespace Koan.Rag.Abstractions;

/// <summary>
/// Full options for a RAG query. Tier 8 escape hatch — most developers
/// use <c>Ask(query)</c> or <c>Ask(query, focus)</c> instead.
/// </summary>
public sealed record RagQueryOptions
{
    /// <summary>
    /// Natural-language focus string that shapes retrieval and generation.
    /// Influences what the agent searches for, how results are reranked,
    /// and how the answer is framed.
    /// </summary>
    public string? Focus { get; init; }

    /// <summary>
    /// Structured tactical hints for the retrieval agent.
    /// </summary>
    public RetrievalHint? Hint { get; init; }

    /// <summary>
    /// Maximum number of search rounds the agent may perform.
    /// Default: 3.
    /// </summary>
    public int MaxSearchRounds { get; init; } = 3;

    /// <summary>
    /// Minimum confidence threshold for the answer. If the agent cannot
    /// meet this threshold, the result status is <see cref="RagQueryStatus.LowConfidence"/>.
    /// Default: 0.5.
    /// </summary>
    public double MinConfidence { get; init; } = 0.5;

    /// <summary>
    /// Pin a specific retrieval strategy. Use <see cref="RetrievalStrategy.Auto"/>
    /// for production; pin a specific strategy for deterministic evaluation.
    /// </summary>
    public RetrievalStrategy Strategy { get; init; } = RetrievalStrategy.Auto;

    /// <summary>
    /// Whether to include source citations in the result.
    /// Default: true.
    /// </summary>
    public bool IncludeCitations { get; init; } = true;
}
