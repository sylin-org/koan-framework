namespace Koan.AI.Orchestration;

/// <summary>
/// Result of a completed chain execution.
/// </summary>
public sealed record ChainResult
{
    /// <summary>Final text output from the chain.</summary>
    public required string Text { get; init; }

    /// <summary>Citations from retrieval steps (null if no retrieval).</summary>
    public IReadOnlyList<Citation>? Citations { get; init; }

    /// <summary>Execution metrics.</summary>
    public required ChainMetrics Metrics { get; init; }
}

/// <summary>
/// A citation from a retrieval step in the chain.
/// </summary>
public sealed record Citation(string Source, string Excerpt, double Relevance);

/// <summary>
/// Execution metrics for a chain run.
/// </summary>
public sealed record ChainMetrics(int TotalTokens, TimeSpan Duration, int Steps);
