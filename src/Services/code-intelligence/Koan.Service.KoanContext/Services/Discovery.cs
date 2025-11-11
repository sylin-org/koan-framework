using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Koan.Context.Utilities;
using Microsoft.Extensions.Logging;

namespace Koan.Context.Services;

/// <summary>
/// File discovery service
/// </summary>
/// <remarks>
/// Scans for documentation files while respecting common ignore patterns.
/// Supports README*, docs/**, adrs/**, *.md, CHANGELOG*, and optional src/** for code samples.
/// Security: Validates paths to prevent directory traversal attacks.
/// </remarks>
public class Discovery
{
    private readonly ILogger<Discovery> _logger;
    private readonly PathValidator _pathValidator;

    private static readonly string[] ExcludedDirectories =
    {
        "node_modules", "bin", "obj", ".git", ".vs", ".vscode",
        "dist", "build", "target", "coverage", ".next", ".nuxt",
        "packages", "vendor", "__pycache__", ".pytest_cache"
    };

    private static readonly string[] CodeFileExtensions =
    {
        ".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".java", ".go",
        ".rs", ".rb", ".php", ".c", ".cpp", ".h", ".hpp", ".swift",
        ".kt", ".scala", ".clj", ".fs", ".fsx", ".vb", ".sh", ".ps1"
    };

    private static readonly string[] IncludedPatterns =
    {
        "README*",
        "CHANGELOG*",
        "*.md",
        "docs/**/*.md",
        "adrs/**/*.md",
        "samples/**/*.md"
    };

    public Discovery(ILogger<Discovery> logger, PathValidator pathValidator)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pathValidator = pathValidator ?? throw new ArgumentNullException(nameof(pathValidator));
    }

    public async IAsyncEnumerable<DiscoveredFile> DiscoverAsync(
        string projectPath,
        string? docsPath = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_pathValidator.IsValidProjectPath(projectPath, out var validationError))
        {
            if (!string.IsNullOrWhiteSpace(validationError) &&
                validationError.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
            {
                throw new DirectoryNotFoundException(validationError);
            }

            throw new ArgumentException(validationError ?? "Invalid project path", nameof(projectPath));
        }

        var normalizedProjectPath = Path.GetFullPath(projectPath);

        // Validate and sanitize search path to prevent directory traversal
        var searchPath = ValidateAndResolveSearchPath(normalizedProjectPath, docsPath);

        if (!Directory.Exists(searchPath))
        {
            _logger.LogWarning("Search path does not exist: {SearchPath}", searchPath);
            throw new DirectoryNotFoundException($"Search path does not exist: {searchPath}");
        }

        _logger.LogInformation("Discovering files (code + markdown) in {SearchPath}", searchPath);

        // Discover markdown files
        await foreach (var file in DiscoverMarkdownFilesAsync(searchPath, normalizedProjectPath, cancellationToken))
        {
            yield return file;
        }

        // Discover code files
        await foreach (var file in DiscoverCodeFilesAsync(searchPath, normalizedProjectPath, cancellationToken))
        {
            yield return file;
        }
    }

    /// <summary>
    /// Validates and resolves the search path, preventing directory traversal attacks
    /// </summary>
    private string ValidateAndResolveSearchPath(string projectPath, string? docsPath)
    {
        if (string.IsNullOrWhiteSpace(docsPath))
        {
            return Path.GetFullPath(projectPath);
        }

        // Combine and normalize paths
        var combinedPath = Path.Combine(projectPath, docsPath);
        var normalizedCombined = Path.GetFullPath(combinedPath);
        var normalizedProject = Path.GetFullPath(projectPath);

        // Ensure the resolved path is within the project boundary
        if (!normalizedCombined.StartsWith(normalizedProject, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError(
                "Path traversal attempt detected: docsPath={DocsPath} escapes project boundary {ProjectPath}",
                docsPath,
                projectPath);

            throw new SecurityException(
                $"Invalid docsPath: '{docsPath}' resolves outside project boundary. " +
                $"Resolved to: '{normalizedCombined}', expected within: '{normalizedProject}'");
        }

        return normalizedCombined;
    }

    private async IAsyncEnumerable<DiscoveredFile> DiscoverMarkdownFilesAsync(
        string searchPath,
        string projectPath,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var searchOption = SearchOption.AllDirectories;
        var fileCount = 0;

        foreach (var file in Directory.EnumerateFiles(searchPath, "*.md", searchOption))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Skip excluded directories
            if (ShouldExclude(file, projectPath))
            {
                continue;
            }

            // Protected file access with error handling
            FileInfo? fileInfo;
            try
            {
                fileInfo = new FileInfo(file);

                // Security: Skip symbolic links to prevent traversal attacks
                if (fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    _logger.LogDebug("Skipping symbolic link: {FilePath}", file);
                    continue;
                }

                if (fileInfo.Attributes.HasFlag(FileAttributes.Hidden))
                {
                    _logger.LogDebug("Skipping hidden file: {FilePath}", file);
                    continue;
                }

                // Skip very large files (> 50MB) to prevent memory exhaustion
                if (fileInfo.Length > 50 * 1024 * 1024)
                {
                    _logger.LogWarning(
                        "Skipping large file ({SizeMB:F2} MB): {FilePath}",
                        fileInfo.Length / (1024.0 * 1024.0),
                        file);
                    continue;
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Access denied to file: {FilePath}", file);
                continue;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error accessing file: {FilePath}", file);
                continue;
            }

            var relativePath = Path.GetRelativePath(projectPath, file);
            var fileType = DetermineFileType(fileInfo.Name);

            yield return new DiscoveredFile(
                AbsolutePath: file,
                RelativePath: relativePath,
                SizeBytes: fileInfo.Length,
                LastModified: fileInfo.LastWriteTimeUtc,
                Type: fileType);

            fileCount++;
            await Task.Yield(); // Allow cooperative cancellation
        }

        _logger.LogInformation("Discovered {FileCount} markdown files", fileCount);
    }

    private async IAsyncEnumerable<DiscoveredFile> DiscoverCodeFilesAsync(
        string searchPath,
        string projectPath,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var searchOption = SearchOption.AllDirectories;
        var fileCount = 0;

        foreach (var file in Directory.EnumerateFiles(searchPath, "*.*", searchOption))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Check if file has a code extension
            var extension = Path.GetExtension(file);
            if (!CodeFileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            // Skip excluded directories
            if (ShouldExclude(file, projectPath))
            {
                continue;
            }

            // Protected file access with error handling
            FileInfo? fileInfo;
            try
            {
                fileInfo = new FileInfo(file);

                // Security: Skip symbolic links to prevent traversal attacks
                if (fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    _logger.LogDebug("Skipping symbolic link: {FilePath}", file);
                    continue;
                }

                if (fileInfo.Attributes.HasFlag(FileAttributes.Hidden))
                {
                    _logger.LogDebug("Skipping hidden file: {FilePath}", file);
                    continue;
                }

                // Skip very large files (> 10MB for code files) to prevent memory exhaustion
                if (fileInfo.Length > 10 * 1024 * 1024)
                {
                    _logger.LogWarning(
                        "Skipping large code file ({SizeMB:F2} MB): {FilePath}",
                        fileInfo.Length / (1024.0 * 1024.0),
                        file);
                    continue;
                }

                // Skip empty files
                if (fileInfo.Length == 0)
                {
                    continue;
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Access denied to file: {FilePath}", file);
                continue;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error accessing file: {FilePath}", file);
                continue;
            }

            var relativePath = Path.GetRelativePath(projectPath, file);

            yield return new DiscoveredFile(
                AbsolutePath: file,
                RelativePath: relativePath,
                SizeBytes: fileInfo.Length,
                LastModified: fileInfo.LastWriteTimeUtc,
                Type: FileType.Code);

            fileCount++;
            await Task.Yield(); // Allow cooperative cancellation
        }

        _logger.LogInformation("Discovered {FileCount} code files", fileCount);
    }

    private static bool ShouldExclude(string filePath, string projectPath)
    {
        var relativePath = Path.GetRelativePath(projectPath, filePath);
    var sanitized = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    var pathParts = sanitized.Split(new[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

    if (pathParts.Any(part => part.Length > 1 && part.StartsWith(".", StringComparison.Ordinal)))
        {
            return true;
        }

        return pathParts.Any(part => ExcludedDirectories.Contains(part, StringComparer.OrdinalIgnoreCase));
    }

    private static FileType DetermineFileType(string fileName)
    {
        var fileNameUpper = fileName.ToUpperInvariant();

        if (fileNameUpper.StartsWith("README"))
            return FileType.Readme;

        if (fileNameUpper.StartsWith("CHANGELOG"))
            return FileType.Changelog;

        if (fileNameUpper.EndsWith(".MD"))
            return FileType.Markdown;

        return FileType.Unknown;
    }

    public async Task<string?> GetCommitShaAsync(string projectPath)
    {
        var gitHeadPath = Path.Combine(projectPath, ".git", "HEAD");

        if (!File.Exists(gitHeadPath))
        {
            _logger.LogDebug("No .git/HEAD found at {GitPath}", gitHeadPath);
            return null;
        }

        try
        {
            var headContent = await File.ReadAllTextAsync(gitHeadPath);

            if (string.IsNullOrWhiteSpace(headContent))
            {
                _logger.LogWarning("Empty .git/HEAD file at {GitPath}", gitHeadPath);
                return null;
            }

            headContent = headContent.Trim();

            // HEAD file contains "ref: refs/heads/main" or direct SHA
            if (headContent.StartsWith("ref:", StringComparison.Ordinal))
            {
                // Bounds check: ensure there's content after "ref:"
                if (headContent.Length <= 5)
                {
                    _logger.LogWarning("Malformed .git/HEAD ref (too short): {Content}", headContent);
                    return null;
                }

                var refPath = headContent.Substring(5).Trim();

                if (string.IsNullOrWhiteSpace(refPath))
                {
                    _logger.LogWarning("Empty ref path in .git/HEAD: {Content}", headContent);
                    return null;
                }

                var refFile = Path.Combine(projectPath, ".git", refPath);

                if (File.Exists(refFile))
                {
                    var sha = await File.ReadAllTextAsync(refFile);
                    var trimmedSha = sha.Trim();

                    if (!string.IsNullOrWhiteSpace(trimmedSha))
                    {
                        _logger.LogDebug("Found git commit SHA: {Sha}", trimmedSha.Substring(0, Math.Min(8, trimmedSha.Length)));
                        return trimmedSha;
                    }
                }
                else
                {
                    _logger.LogWarning("Git ref file not found: {RefFile}", refFile);
                }
            }
            else
            {
                // Direct SHA in HEAD (detached HEAD state)
                if (!string.IsNullOrWhiteSpace(headContent))
                {
                    _logger.LogDebug("Found git commit SHA (detached HEAD): {Sha}", headContent.Substring(0, Math.Min(8, headContent.Length)));
                    return headContent;
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Access denied reading git commit SHA from {GitPath}", gitHeadPath);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "I/O error reading git commit SHA from {GitPath}", gitHeadPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error reading git commit SHA from {GitPath}", gitHeadPath);
        }

        return null;
    }
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
