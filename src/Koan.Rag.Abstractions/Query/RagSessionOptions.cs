namespace Koan.Rag.Abstractions;

/// <summary>
/// Configuration for a stateful RAG conversation session.
/// </summary>
public sealed record RagSessionOptions
{
    /// <summary>
    /// Maximum token budget for conversation history.
    /// When exceeded, the session auto-summarizes older turns.
    /// Default: 16,000.
    /// </summary>
    public int MaxTokenBudget { get; init; } = 16_000;

    /// <summary>
    /// Strategy for handling token budget exhaustion.
    /// Default: <see cref="SessionExhaustionStrategy.AutoSummarize"/>.
    /// </summary>
    public SessionExhaustionStrategy ExhaustionStrategy { get; init; } =
        SessionExhaustionStrategy.AutoSummarize;
}

/// <summary>
/// Strategy for handling session token budget exhaustion.
/// </summary>
public enum SessionExhaustionStrategy
{
    /// <summary>Summarize older turns to free budget (default).</summary>
    AutoSummarize = 0,

    /// <summary>Drop oldest turns silently.</summary>
    DropOldest = 1,

    /// <summary>Throw when budget is exceeded.</summary>
    Throw = 2
}
