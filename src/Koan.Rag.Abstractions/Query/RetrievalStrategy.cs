namespace Koan.Rag.Abstractions;

/// <summary>
/// Controls retrieval strategy selection. <see cref="Auto"/> lets the agent decide;
/// other values pin a specific strategy for deterministic evaluation.
/// </summary>
public enum RetrievalStrategy
{
    /// <summary>Agent decides strategy per query (default, production mode).</summary>
    Auto = 0,

    /// <summary>Dense embedding similarity only. Deterministic for eval.</summary>
    SemanticOnly = 1,

    /// <summary>Sparse keyword (BM25/SPLADE) only. Deterministic for eval.</summary>
    KeywordOnly = 2,

    /// <summary>Dense + sparse hybrid without graph traversal. Deterministic for eval.</summary>
    HybridOnly = 3,

    /// <summary>Graph traversal prioritized, then vector search. Deterministic for eval.</summary>
    GraphFirst = 4
}
