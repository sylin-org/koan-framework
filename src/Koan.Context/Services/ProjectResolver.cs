using Koan.Context.Models;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Context.Services;

/// <summary>
/// Resolves project context from various sources (explicit ID, working directory, transport context)
/// </summary>
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

    public async Task<Project?> ResolveProjectAsync(
        string? libraryId,
        string? workingDirectory,
        HttpContext? httpContext = null,
        bool autoCreate = true,
        CancellationToken cancellationToken = default)
    {
        // Priority 1: Explicit libraryId (always wins)
        if (!string.IsNullOrWhiteSpace(libraryId))
        {
            _logger.LogDebug("Resolving project by explicit ID: {LibraryId}", libraryId);
            return await Project.Get(libraryId, cancellationToken);
        }

        // Priority 2: WorkingDirectory parameter (path-based lookup)
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            _logger.LogDebug("Resolving project by working directory: {Path}", workingDirectory);
            return await ResolveProjectByPathAsync(workingDirectory, autoCreate, cancellationToken);
        }

        // Priority 3: Transport context (HTTP headers from MCP client)
        if (httpContext != null)
        {
            // Check for working directory in HTTP headers
            // Claude Code and other MCP clients may send context via custom headers
            var headerKeys = new[] { "X-Working-Directory", "X-Claude-Working-Directory", "X-MCP-Working-Directory" };

            foreach (var headerKey in headerKeys)
            {
                if (httpContext.Request.Headers.TryGetValue(headerKey, out var headerValue) &&
                    !string.IsNullOrWhiteSpace(headerValue.ToString()))
                {
                    var contextPath = headerValue.ToString();
                    _logger.LogDebug("Resolving project by HTTP header {Header}: {Path}", headerKey, contextPath);
                    return await ResolveProjectByPathAsync(contextPath, autoCreate, cancellationToken);
                }
            }
        }

        _logger.LogWarning("No project context found - no libraryId, workingDirectory, or HTTP context headers provided");
        return null;
    }

    /// <summary>
    /// Resolves a project based on an arbitrary file path, optionally auto-creating one.
    /// </summary>
    public async Task<Project?> ResolveProjectByPathAsync(
        string pathContext,
        bool autoCreate = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pathContext))
        {
            throw new ArgumentException("Path context cannot be null or empty", nameof(pathContext));
        }

        var normalizedPath = ResolvePath(pathContext, _options.FollowSymbolicLinks);
        var gitRoot = FindGitRoot(normalizedPath);

        if (gitRoot != null)
        {
            var project = await ResolveByPathAsync(gitRoot, autoCreate, cancellationToken);
            if (project != null)
            {
                return project;
            }
        }

        // Fallback to directory itself when no git repo detected
        var fallbackRoot = gitRoot ?? Path.GetDirectoryName(normalizedPath);

        if (fallbackRoot == null)
        {
            throw new ArgumentException($"Invalid path context: {pathContext}", nameof(pathContext));
        }

        var resolved = await ResolveByPathAsync(fallbackRoot, autoCreate, cancellationToken);
        if (resolved != null)
        {
            return resolved;
        }

        if (!autoCreate || !_options.AutoCreateProjectOnQuery)
        {
            return null;
        }

        return await AutoCreateProjectAsync(fallbackRoot, cancellationToken);
    }

    private async Task<Project?> ResolveByPathAsync(
        string path,
        bool autoCreate,
        CancellationToken cancellationToken)
    {
        // Normalize and resolve path
        var normalizedPath = ResolvePath(path, _options.FollowSymbolicLinks);

        if (!Directory.Exists(normalizedPath))
        {
            _logger.LogWarning("Path does not exist: {Path}", normalizedPath);
            return null;
        }

        // Get all active projects
        var projects = await Project.Query(p => p.IsActive, cancellationToken);

        // Find all matching projects (exact + ancestors)
        var matches = FindAllMatchingProjects(normalizedPath, projects.ToList(), _options.MaxAncestorDepth);

        if (matches.Count > 0)
        {
            // Return most specific match
            var match = matches.First();
            _logger.LogDebug("Found project match: {ProjectName} at {Path}",
                match.Name, match.RootPath);
            return match;
        }

        // No match found - auto-create if enabled
        if (_options.AutoCreateProjectOnQuery && autoCreate)
        {
            _logger.LogInformation("Auto-creating project for path: {Path}", normalizedPath);
            return await AutoCreateProjectAsync(normalizedPath, cancellationToken);
        }

        return null;
    }

    private List<Project> FindAllMatchingProjects(
        string path,
        List<Project> allProjects,
        int maxDepth)
    {
        var matches = new List<Project>();
        var dir = new DirectoryInfo(path);
        var depth = 0;

        while (dir != null && depth <= maxDepth)
        {
            var match = allProjects.FirstOrDefault(p =>
                PathsEqual(p.RootPath, dir.FullName));

            if (match != null)
            {
                matches.Add(match);
            }

            dir = dir.Parent;
            depth++;
        }

        return matches
            .OrderBy(m => m.RootPath.Length)  // Most specific first
            .ToList();
    }

    private string? FindGitRoot(string path)
    {
        var current = new DirectoryInfo(path);

        while (current != null)
        {
            var gitFolder = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(gitFolder))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private async Task<Project> AutoCreateProjectAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var project = Project.CreateFromDirectory(path);
        project.Status = IndexingStatus.NotIndexed;
        await project.Save(cancellationToken);

        _logger.LogInformation("Created project {Name} ({ProjectId}) at {Path}",
            project.Name, project.Id, project.RootPath);

        return project;
    }

    private string ResolvePath(string path, bool followSymlinks)
    {
        var fullPath = Path.GetFullPath(path);

        if (!followSymlinks) return fullPath;

        // Resolve symlinks/junctions
        var info = new DirectoryInfo(fullPath);
        while (info.Attributes.HasFlag(FileAttributes.ReparsePoint) && info.LinkTarget != null)
        {
            info = new DirectoryInfo(info.LinkTarget);
        }

        return info.FullName;
    }

    private bool PathsEqual(string path1, string path2)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return Path.GetFullPath(path1).Equals(Path.GetFullPath(path2), comparison);
    }
}

/// <summary>
/// Configuration options for project resolution
/// </summary>
public class ProjectResolutionOptions
{
    public bool FollowSymbolicLinks { get; set; } = true;
    public bool MatchAncestorProjects { get; set; } = true;
    public int MaxAncestorDepth { get; set; } = 2;
    public bool AutoCreateProjectOnQuery { get; set; } = true;
    public bool AutoIndexInBackground { get; set; } = true;
    public bool AutoIndexRequireGitRepository { get; set; } = false;
    public int AutoIndexMaxSizeGB { get; set; } = 10;
    public string TopKMode { get; set; } = "per-project";
}
