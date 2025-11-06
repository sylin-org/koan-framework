namespace Koan.Context.Services;

/// <summary>
/// Service for discovering indexable files in a project
/// </summary>
public interface IDocumentDiscoveryService
{
    /// <summary>
    /// Discovers files to be indexed in the project
    /// </summary>
    /// <param name="projectPath">Root path of the project</param>
    /// <param name="docsPath">Optional specific docs path to scan</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Async enumerable of discovered files</returns>
    IAsyncEnumerable<DiscoveredFile> DiscoverAsync(
        string projectPath,
        string? docsPath = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the git commit SHA for the project if available
    /// </summary>
    /// <param name="projectPath">Root path of the project</param>
    /// <returns>Commit SHA or null if not a git repository</returns>
    Task<string?> GetCommitShaAsync(string projectPath);
}

/// <summary>
/// Represents a discovered file ready for indexing
/// </summary>
public record DiscoveredFile(
    string AbsolutePath,
    string RelativePath,
    long SizeBytes,
    DateTime LastModified,
    FileType Type);

/// <summary>
/// Type of discovered file
/// </summary>
public enum FileType
{
    Markdown,
    Code,
    Readme,
    Changelog,
    Unknown
}
