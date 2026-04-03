namespace Koan.Rag.Abstractions;

/// <summary>
/// Controls how the concept graph is constructed during corpus ingestion.
/// </summary>
public enum GraphStrategy
{
    /// <summary>
    /// Entity extraction + semantic linking. Entities are connected by embedding
    /// proximity of their descriptions. No explicit relationship extraction.
    /// Covers ~80% of cross-document relationship discovery at a fraction of the cost.
    /// </summary>
    Lightweight = 0,

    /// <summary>
    /// Entity extraction + explicit relationship extraction via LLM. Produces
    /// labeled edges ("requires", "is-a", "governed-by"). Higher cost, richer graph.
    /// </summary>
    Full = 1,

    /// <summary>
    /// No graph at ingest time. Entities and relationships are extracted on-demand
    /// at query time from retrieved chunks. Minimal ingestion cost.
    /// </summary>
    Lazy = 2
}
