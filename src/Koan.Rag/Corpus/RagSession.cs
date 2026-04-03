using Koan.Data.Abstractions;
using Koan.Rag.Abstractions;
using Koan.Rag.Retrieval;

namespace Koan.Rag;

/// <summary>
/// Stateful conversation session with conversation history and token budget management.
/// </summary>
internal sealed class RagSession<TEntity> : IRagSession<TEntity> where TEntity : class, IEntity<string>
{
    private readonly IRagCorpus<TEntity> _corpus;
    private readonly IRagRetrievalPipeline _retrievalPipeline;
    private readonly RagCorpusMetadata _metadata;
    private readonly RagSessionOptions _options;
    private readonly List<(string Role, string Content)> _history = [];
    private int _tokensUsed;
    private int _turnCount;

    internal RagSession(
        IRagCorpus<TEntity> corpus,
        IRagRetrievalPipeline retrievalPipeline,
        RagCorpusMetadata metadata,
        RagSessionOptions options)
    {
        _corpus = corpus;
        _retrievalPipeline = retrievalPipeline;
        _metadata = metadata;
        _options = options;
    }

    public int TokensUsed => _tokensUsed;
    public int TokensRemaining => Math.Max(0, _options.MaxTokenBudget - _tokensUsed);
    public int TurnCount => _turnCount;

    public async Task<string> Ask(string query, CancellationToken ct = default)
    {
        var result = await AskResult(query, ct);
        return result.Answer;
    }

    public async Task<string> Ask(string query, string focus, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var queryTokens = EstimateTokens(query);
        if (_tokensUsed + queryTokens > _options.MaxTokenBudget)
            await HandleBudgetExhaustion(ct);
        _history.Add(("user", query));
        _tokensUsed += queryTokens;
        _turnCount++;

        var contextualQuery = BuildContextualQuery(query);
        var result = await _retrievalPipeline.Execute<TEntity>(
            contextualQuery, new RagQueryOptions { Focus = focus }, _metadata, ct);

        _history.Add(("assistant", result.Answer));
        _tokensUsed += EstimateTokens(result.Answer);

        return result.Answer;
    }

    public async Task<RagQueryResult> AskResult(string query, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        // Check budget before mutating state
        var queryTokens = EstimateTokens(query);
        if (_tokensUsed + queryTokens > _options.MaxTokenBudget)
            await HandleBudgetExhaustion(ct);

        // Track the question in history
        _history.Add(("user", query));
        _tokensUsed += queryTokens;
        _turnCount++;

        // Build context-aware query with conversation history
        var contextualQuery = BuildContextualQuery(query);

        var result = await _retrievalPipeline.Execute<TEntity>(
            contextualQuery, new RagQueryOptions(), _metadata, ct);

        // Track the answer in history
        _history.Add(("assistant", result.Answer));
        _tokensUsed += EstimateTokens(result.Answer);

        return result;
    }

    public ValueTask DisposeAsync()
    {
        _history.Clear();
        _tokensUsed = 0;
        _turnCount = 0;
        return ValueTask.CompletedTask;
    }

    private string BuildContextualQuery(string currentQuery)
    {
        if (_history.Count <= 1)
            return currentQuery;

        // Include recent conversation context for the retrieval agent
        var context = string.Join("\n",
            _history.TakeLast(6) // Last 3 turns (user + assistant pairs)
                .Select(h => $"{h.Role}: {h.Content}"));

        return $"Conversation context:\n{context}\n\nCurrent question: {currentQuery}";
    }

    private Task HandleBudgetExhaustion(CancellationToken ct)
    {
        return _options.ExhaustionStrategy switch
        {
            SessionExhaustionStrategy.Throw =>
                throw new InvalidOperationException(
                    $"Session token budget exhausted ({_tokensUsed}/{_options.MaxTokenBudget} tokens). " +
                    "Dispose this session and create a new one."),

            SessionExhaustionStrategy.DropOldest => DropOldestTurns(),

            // AutoSummarize: for now, fall back to dropping oldest
            // Full summarization requires an LLM call which will be implemented
            // with the agentic retrieval pipeline
            _ => DropOldestTurns()
        };
    }

    private Task DropOldestTurns()
    {
        // Remove oldest turns until under budget
        while (_tokensUsed > _options.MaxTokenBudget && _history.Count > 0)
        {
            var removed = _history[0];
            _tokensUsed -= EstimateTokens(removed.Content);
            _history.RemoveAt(0);
        }

        return Task.CompletedTask;
    }

    private static int EstimateTokens(string text)
        => (text.Length + 3) / 4; // ~4 chars per token, matches EmbeddingMetadata convention
}
