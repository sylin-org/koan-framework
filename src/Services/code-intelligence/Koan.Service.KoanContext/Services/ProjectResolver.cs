using Koan.Context.Models;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Context.Services;

/// <summary>
/// Resolves project context from various sources (explicit ID, working directory, path context)
/// </summary>
/// <remarks>
/// Simplified resolver focusing on core resolution logic:
/// - Priority 1: Explicit project ID (exact match)
/// - Priority 2: Working directory or path context (git root detection)
/// - Priority 3: HTTP transport headers (X-Working-Directory)
/// Removed complexity: ancestor matching, symlink resolution, auto-indexing side effects
/// </remarks>
public class ProjectResolver
{
    private readonly ILogger<ProjectResolver> _logger;
    private readonly ProjectResolutionOptions _options;

    public ProjectResolver(
        ILogger<ProjectResolver> logger,
        IOptions<ProjectResolutionOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    /// <summary>
    /// Resolve project by ID, working directory, or HTTP context
    /// </summary>
    public virtual async Task<Project?> ResolveProjectAsync(
        string? libraryId,
        string? workingDirectory,
        HttpContext? httpContext = null,
        bool autoCreate = true,
        CancellationToken cancellationToken = default)
    {
        // Priority 1: Explicit libraryId (direct lookup)
        if (!string.IsNullOrWhiteSpace(libraryId))
        {
            _logger.LogDebug("Resolving project by ID: {LibraryId}", libraryId);
            return await Project.Get(libraryId, cancellationToken);
        }

        // Priority 2: Working directory (path-based lookup)
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            _logger.LogDebug("Resolving project by path: {Path}", workingDirectory);
            return await ResolveProjectByPathAsync(workingDirectory, autoCreate, cancellationToken);
        }

        // Priority 3: HTTP headers (MCP transport context)
        if (httpContext != null)
        {
            var headerKeys = new[] { "X-Working-Directory", "X-Claude-Working-Directory" };

            foreach (var headerKey in headerKeys)
            {
                if (httpContext.Request.Headers.TryGetValue(headerKey, out var headerValue) &&
                    !string.IsNullOrWhiteSpace(headerValue.ToString()))
                {
                    var contextPath = headerValue.ToString();
                    _logger.LogDebug("Resolving project from header {Header}: {Path}", headerKey, contextPath);
                    return await ResolveProjectByPathAsync(contextPath, autoCreate, cancellationToken);
                }
            }
        }

        _logger.LogWarning("No project context provided");
        return null;
    }

    /// <summary>
    /// Resolve project from file path (detects git root, matches existing projects)
    /// </summary>
    public virtual async Task<Project?> ResolveProjectByPathAsync(
        string pathContext,
        bool autoCreate = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pathContext))
        {
            throw new ArgumentException("Path cannot be empty", nameof(pathContext));
        }

        // Normalize path
        var normalizedPath = Path.GetFullPath(pathContext);

        // Detect git root (essential for identifying project boundaries)
        var gitRoot = FindGitRoot(normalizedPath);
        var projectRoot = gitRoot ?? Path.GetDirectoryName(normalizedPath);

        if (projectRoot == null)
        {
            throw new ArgumentException($"Invalid path: {pathContext}", nameof(pathContext));
        }

        // Find exact match in existing projects
        var projects = await Project.Query(p => true, cancellationToken);
        var match = projects.FirstOrDefault(p => PathsEqual(p.RootPath, projectRoot));

        if (match != null)
        {
            _logger.LogDebug("Found project: {Name} at {Path}", match.Name, match.RootPath);
            return match;
        }

        // Auto-create if enabled
        if (_options.AutoCreate && autoCreate)
        {
            _logger.LogInformation("Auto-creating project at {Path}", projectRoot);
            return await CreateProjectAsync(projectRoot, cancellationToken);
        }

        return null;
    }

    /// <summary>
    /// Find git repository root by walking up directory tree
    /// </summary>
    private string? FindGitRoot(string path)
    {
        var current = new DirectoryInfo(path);

        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    /// <summary>
    /// Create new project from directory path
    /// </summary>
    private async Task<Project> CreateProjectAsync(string path, CancellationToken cancellationToken)
    {
        var project = Project.CreateFromDirectory(path);
        project.Status = IndexingStatus.NotIndexed;
        await project.Save(cancellationToken);

        _logger.LogInformation("Created project {Name} ({Id}) at {Path}", project.Name, project.Id, project.RootPath);
        return project;
    }

    /// <summary>
    /// Compare paths for equality (case-insensitive on Windows/macOS)
    /// </summary>
    private bool PathsEqual(string path1, string path2)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return Path.GetFullPath(path1).Equals(Path.GetFullPath(path2), comparison);
    }
}

/// <summary>
/// Project resolution configuration (simplified from 10 options to 3)
/// </summary>
public class ProjectResolutionOptions
{
    /// <summary>Auto-create projects when resolving unknown paths</summary>
    public bool AutoCreate { get; set; } = true;

    /// <summary>Auto-index newly created projects in background</summary>
    public bool AutoIndex { get; set; } = true;

    /// <summary>Maximum project size for auto-indexing (GB)</summary>
    public int MaxSizeGB { get; set; } = 10;
}
