namespace Koan.Rag.Abstractions;

/// <summary>
/// Result of an ingestion operation. Always returned — never bare Task.
/// Partial failures are surfaced per-file; successful files are committed.
/// </summary>
public sealed record RagIngestResult
{
    /// <summary>Number of files/entities successfully processed.</summary>
    public int FilesProcessed { get; init; }

    /// <summary>Total chunks created across all processed files.</summary>
    public int ChunksCreated { get; init; }

    /// <summary>Total entities extracted for the concept graph.</summary>
    public int EntitiesExtracted { get; init; }

    /// <summary>Per-file errors for files that failed processing.</summary>
    public IReadOnlyList<RagIngestError> Errors { get; init; } = [];

    /// <summary>Wall-clock time for the full ingestion operation.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>True if all files were processed without errors.</summary>
    public bool Success => Errors.Count == 0;
}

/// <summary>
/// Describes a single file/entity that failed during ingestion.
/// </summary>
public sealed record RagIngestError(
    string FileName,
    string Reason,
    Exception? Exception = null);
