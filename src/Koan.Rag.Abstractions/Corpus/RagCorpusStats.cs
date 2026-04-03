namespace Koan.Rag.Abstractions;

/// <summary>
/// Health, metrics, and freshness indicators for a RAG corpus.
/// </summary>
public sealed record RagCorpusStats
{
    /// <summary>Total documents in the corpus.</summary>
    public int Documents { get; init; }

    /// <summary>Documents pending ingestion.</summary>
    public int PendingDocuments { get; init; }

    /// <summary>Documents that failed ingestion.</summary>
    public int FailedDocuments { get; init; }

    /// <summary>Total chunks across all documents.</summary>
    public int Chunks { get; init; }

    /// <summary>Entities in the concept graph.</summary>
    public int Entities { get; init; }

    /// <summary>Relationships in the concept graph.</summary>
    public int Relationships { get; init; }

    /// <summary>Number of Zen Garden compute nodes used during ingestion.</summary>
    public int ComputeNodes { get; init; }

    /// <summary>Total time spent on the most recent full ingestion.</summary>
    public TimeSpan? IngestDuration { get; init; }

    /// <summary>Average query latency over the recent window.</summary>
    public TimeSpan? AvgQueryLatency { get; init; }

    /// <summary>Corpus freshness score (0.0 = stale, 1.0 = fresh).</summary>
    public double FreshnessScore { get; init; }

    /// <summary>True if the framework recommends a reindex based on growth or staleness.</summary>
    public bool ReindexRecommended { get; init; }

    /// <summary>Human-readable reason for reindex recommendation.</summary>
    public string? ReindexReason { get; init; }

    /// <summary>Timestamp of the last full reindex.</summary>
    public DateTimeOffset? LastFullReindex { get; init; }

    /// <summary>Documents added since the last full reindex.</summary>
    public int DocumentsSinceLastReindex { get; init; }
}
