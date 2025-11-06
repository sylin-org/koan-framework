namespace Koan.Context.Services;

/// <summary>
/// Service for chunking extracted content into indexable segments
/// </summary>
public interface IChunkingService
{
    /// <summary>
    /// Chunks an extracted document into semantic segments for indexing
    /// </summary>
    /// <param name="document">Extracted document to chunk</param>
    /// <param name="projectId">Project ID for metadata</param>
    /// <param name="commitSha">Optional git commit SHA</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Async enumerable of document chunks</returns>
    IAsyncEnumerable<ChunkedContent> ChunkAsync(
        ExtractedDocument document,
        string projectId,
        string? commitSha = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a chunked piece of content ready for embedding
/// </summary>
public record ChunkedContent(
    string ProjectId,
    string FilePath,
    string Text,
    int TokenCount,
    int StartOffset,
    int EndOffset,
    string? Title = null,
    string? Language = null);
