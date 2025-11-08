using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

namespace Koan.Context.Models;

/// <summary>
/// Represents a code project being tracked by Koan Context
/// </summary>
public class Project : Entity<Project>
{
    /// <summary>
    /// Display name (auto-derived from folder name)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Absolute path to project root directory
    /// </summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>
    /// Optional subdirectory for documentation (e.g., "docs")
    /// </summary>
    public string? DocsPath { get; set; }

    /// <summary>
    /// Last successful indexing timestamp
    /// </summary>
    public DateTime? LastIndexed { get; set; }

    /// <summary>
    /// Current indexing status
    /// </summary>
    public IndexingStatus Status { get; set; } = IndexingStatus.NotIndexed;

    /// <summary>
    /// Total chunks indexed
    /// </summary>
    public int DocumentCount { get; set; }

    /// <summary>
    /// Total bytes of indexed content
    /// </summary>
    public long IndexedBytes { get; set; }

    /// <summary>
    /// Git commit SHA at time of indexing (provenance)
    /// </summary>
    public string? CommitSha { get; set; }

    /// <summary>
    /// Last error message if indexing failed
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Creates a new project instance
    /// </summary>
    /// <param name="name">Project name</param>
    /// <param name="rootPath">Absolute path to project root</param>
    /// <param name="docsPath">Optional subdirectory for documentation</param>
    /// <returns>New Project entity with auto-generated GUID v7 ID</returns>
    public static Project Create(string name, string rootPath, string? docsPath = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Project name cannot be empty", nameof(name));

        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Root path cannot be empty", nameof(rootPath));

        if (!Path.IsPathFullyQualified(rootPath))
            throw new ArgumentException("Root path must be an absolute path", nameof(rootPath));

        return new Project
        {
            Name = name,
            RootPath = rootPath,
            DocsPath = docsPath
        };
    }

    /// <summary>
    /// Creates a project from a directory path (auto-detect name from folder)
    /// </summary>
    public static Project CreateFromDirectory(string directoryPath, string? docsPath = null)
    {
        if (!Directory.Exists(directoryPath))
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");

        var folderName = Path.GetFileName(directoryPath);
        if (string.IsNullOrWhiteSpace(folderName))
            throw new ArgumentException("Could not determine folder name from path", nameof(directoryPath));

        return Create(folderName, directoryPath, docsPath);
    }

    /// <summary>
    /// Marks the project as indexed with current timestamp
    /// </summary>
    public void MarkIndexed(int documentCount, long indexedBytes)
    {
        LastIndexed = DateTime.UtcNow;
        DocumentCount = documentCount;
        IndexedBytes = indexedBytes;
        Status = IndexingStatus.Ready;
        LastError = null;
    }
}

/// <summary>
/// Project indexing status
/// </summary>
public enum IndexingStatus
{
    /// <summary>Project created but never indexed</summary>
    NotIndexed = 0,

    /// <summary>Initial indexing in progress (not queryable yet)</summary>
    Indexing = 1,

    /// <summary>Indexed and ready for queries</summary>
    Ready = 2,

    /// <summary>Last indexing failed</summary>
    Failed = 3
}
