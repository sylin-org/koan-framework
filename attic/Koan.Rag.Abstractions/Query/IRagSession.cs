using Koan.Data.Abstractions;

namespace Koan.Rag.Abstractions;

/// <summary>
/// A stateful conversation session scoped to a RAG corpus.
/// Maintains conversation history across turns with configurable token budgets.
/// </summary>
/// <typeparam name="TEntity">The entity type of the parent corpus.</typeparam>
public interface IRagSession<TEntity> : IAsyncDisposable where TEntity : class, IEntity<string>
{
    /// <summary>Ask a question with conversation context from prior turns.</summary>
    Task<string> Ask(string query, CancellationToken ct = default);

    /// <summary>Ask with a focus modifier.</summary>
    Task<string> Ask(string query, string focus, CancellationToken ct = default);

    /// <summary>Ask and return a rich result.</summary>
    Task<RagQueryResult> AskResult(string query, CancellationToken ct = default);

    /// <summary>Tokens consumed by conversation history so far.</summary>
    int TokensUsed { get; }

    /// <summary>Remaining token budget before exhaustion strategy triggers.</summary>
    int TokensRemaining { get; }

    /// <summary>Number of conversation turns completed.</summary>
    int TurnCount { get; }
}
