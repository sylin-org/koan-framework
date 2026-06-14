namespace Koan.Rag.Abstractions;

/// <summary>
/// Rich result from a RAG query, including the answer, source citations,
/// confidence, and operational metadata. Returned by <c>AskResult()</c>.
/// </summary>
public sealed record RagQueryResult
{
    /// <summary>The generated answer text.</summary>
    public required string Answer { get; init; }

    /// <summary>Outcome status of the query.</summary>
    public required RagQueryStatus Status { get; init; }

    /// <summary>
    /// Agent's confidence in the answer quality, from 0.0 (no confidence)
    /// to 1.0 (fully supported by retrieved context). Null if not assessed.
    /// </summary>
    public double? Confidence { get; init; }

    /// <summary>Source documents that contributed to the answer.</summary>
    public IReadOnlyList<RagSource> Sources { get; init; } = [];

    /// <summary>Total tokens consumed (retrieval + generation).</summary>
    public int? TokensUsed { get; init; }

    /// <summary>Wall-clock time for the full query pipeline.</summary>
    public TimeSpan? Latency { get; init; }

    /// <summary>
    /// Retrieval trace capturing the agent's tool calls, intermediate results,
    /// and reasoning. Useful for debugging and evaluation.
    /// </summary>
    public RagRetrievalTrace? Trace { get; init; }

    /// <summary>
    /// Human-readable status message. Populated on non-success statuses
    /// (e.g., "Corpus 'Policy' contains no documents. Call Ingest() first.").
    /// </summary>
    public string? Message { get; init; }
}
