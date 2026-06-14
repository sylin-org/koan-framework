namespace Koan.Rag.Abstractions;

/// <summary>
/// Progress report during a multi-file ingestion operation.
/// </summary>
public sealed record RagIngestProgress(
    int ProcessedFiles,
    int TotalFiles,
    int ProcessedChunks,
    string? CurrentFileName);
