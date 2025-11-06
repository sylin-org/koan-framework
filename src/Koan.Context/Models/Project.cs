using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

namespace Koan.Context.Models;

/// <summary>
/// Represents a code project being tracked by Koan Context
/// </summary>
public class Project : Entity<Project, Guid>
{
    /// <summary>
    /// Display name for the project
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Absolute path to the project root directory
    /// </summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>
    /// Type of project (dotnet, node, python, etc.)
    /// </summary>
    public ProjectType ProjectType { get; set; } = ProjectType.Unknown;

    /// <summary>
    /// Git remote URL if project is in version control
    /// </summary>
    public string? GitRemote { get; set; }

    /// <summary>
    /// Last time the project was indexed
    /// </summary>
    public DateTime? LastIndexed { get; set; }

    /// <summary>
    /// Whether the project is actively being tracked
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// When this project was first registered
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last time project metadata was updated
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Number of documents/chunks indexed
    /// </summary>
    public int DocumentCount { get; set; }

    /// <summary>
    /// Total size of indexed content in bytes
    /// </summary>
    public long IndexedBytes { get; set; }

    /// <summary>
    /// Creates a new project instance
    /// </summary>
    /// <param name="name">Project name</param>
    /// <param name="rootPath">Absolute path to project root</param>
    /// <param name="projectType">Type of project</param>
    /// <returns>New Project entity with auto-generated GUID v7 ID</returns>
    public static Project Create(string name, string rootPath, ProjectType projectType = ProjectType.Unknown)
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
            ProjectType = projectType,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true
        };
    }

    /// <summary>
    /// Marks the project as indexed with current timestamp
    /// </summary>
    public void MarkIndexed(int documentCount, long indexedBytes)
    {
        LastIndexed = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
        DocumentCount = documentCount;
        IndexedBytes = indexedBytes;
    }
}

/// <summary>
/// Supported project types for automatic detection and tooling
/// </summary>
public enum ProjectType
{
    Unknown = 0,
    Dotnet,
    Node,
    Python,
    Java,
    Go,
    Rust,
    Ruby,
    Php,
    Generic
}
