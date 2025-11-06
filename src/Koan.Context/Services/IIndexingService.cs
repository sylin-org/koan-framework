namespace Koan.Context.Services;

/// <summary>
/// Orchestrates the document indexing pipeline
/// </summary>
public interface IIndexingService
{
    /// <summary>
    /// Indexes all documents for a project
    /// </summary>
    /// <param name="projectId">Project ID to index</param>
    /// <param name="progress">Optional progress reporter</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Indexing result with statistics</returns>
    Task<IndexingResult> IndexProjectAsync(
        string projectId,
        IProgress<IndexingProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of an indexing operation.
/// QA Issue #35: Changed from List of string to structured error type
/// </summary>
public record IndexingResult(
    int FilesProcessed,
    int ChunksCreated,
    int VectorsSaved,
    TimeSpan Duration,
    IReadOnlyList<IndexingError> Errors);

/// <summary>
/// Structured error information from indexing
/// QA Issue #35: Provides detailed error context for diagnostics
/// </summary>
public record IndexingError(
    string FilePath,
    string ErrorMessage,
    string ErrorType,
    string? StackTrace);

/// <summary>
/// Progress information during indexing
/// </summary>
public record IndexingProgress(
    int FilesProcessed,
    int FilesTotal,
    int ChunksCreated,
    int VectorsSaved,
    string? CurrentFile);
