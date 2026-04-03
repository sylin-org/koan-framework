using Koan.Rag.Abstractions;

namespace Koan.Rag.Infrastructure;

/// <summary>
/// Global RAG configuration options. Convention defaults apply when not configured.
/// Bound from <c>Koan:Rag</c> configuration section.
/// </summary>
public sealed class RagOptions
{
    /// <summary>Default chunking strategy.</summary>
    public string ChunkStrategy { get; set; } = "SemanticWithContext";

    /// <summary>Token count for child chunks (precision matching).</summary>
    public int ChildChunkTokens { get; set; } = 300;

    /// <summary>Token count for parent chunks (context return).</summary>
    public int ParentChunkTokens { get; set; } = 1200;

    /// <summary>Whether to generate contextual prefixes per chunk.</summary>
    public bool ContextualPrefix { get; set; } = true;

    /// <summary>Hybrid search alpha (0.0 = keyword only, 1.0 = semantic only).</summary>
    public double HybridAlpha { get; set; } = 0.6;

    /// <summary>Whether cross-encoder reranking is enabled.</summary>
    public bool RerankEnabled { get; set; } = true;

    /// <summary>Top-N results after reranking.</summary>
    public int RerankTopN { get; set; } = 10;

    /// <summary>Default graph construction strategy.</summary>
    public GraphStrategy GraphStrategy { get; set; } = GraphStrategy.Lightweight;

    /// <summary>Cosine similarity threshold for entity resolution.</summary>
    public double EntityResolutionThreshold { get; set; } = 0.92;

    /// <summary>Maximum retrieval rounds for the agent.</summary>
    public int MaxSearchRounds { get; set; } = 3;

    /// <summary>Minimum confidence for answer acceptance.</summary>
    public double MinConfidence { get; set; } = 0.5;

    /// <summary>Whether to include citations in results.</summary>
    public bool CitationsEnabled { get; set; } = true;
}
