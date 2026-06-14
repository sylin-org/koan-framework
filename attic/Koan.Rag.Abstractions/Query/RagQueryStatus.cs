namespace Koan.Rag.Abstractions;

/// <summary>
/// Indicates the outcome status of a RAG query operation.
/// </summary>
public enum RagQueryStatus
{
    /// <summary>Query completed successfully with sufficient context.</summary>
    Success = 0,

    /// <summary>Query completed but confidence is below the minimum threshold.</summary>
    LowConfidence = 1,

    /// <summary>The corpus contains no documents. Call Ingest() first.</summary>
    EmptyCorpus = 2,

    /// <summary>Retrieval found no relevant chunks for the query.</summary>
    NoResults = 3,

    /// <summary>The query was cancelled via CancellationToken.</summary>
    Cancelled = 4,

    /// <summary>An error occurred during retrieval or generation.</summary>
    Error = 5
}
