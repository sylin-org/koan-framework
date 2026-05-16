namespace Koan.Rag.Abstractions;

/// <summary>
/// Tracks the processing state of a document within a RAG corpus.
/// </summary>
public enum RagIngestionStatus
{
    /// <summary>Document queued for ingestion.</summary>
    Pending = 0,

    /// <summary>Document is currently being processed (extraction, chunking, embedding).</summary>
    Processing = 1,

    /// <summary>Document successfully ingested into the corpus.</summary>
    Completed = 2,

    /// <summary>Ingestion failed; eligible for retry.</summary>
    Failed = 3,

    /// <summary>Ingestion permanently failed after exhausting retries.</summary>
    FailedPermanent = 4
}
