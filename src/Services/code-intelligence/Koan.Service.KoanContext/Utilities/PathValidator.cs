using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;

namespace Koan.Context.Utilities;

/// <summary>
/// Validates file system paths to prevent path traversal attacks
/// </summary>
public class PathValidator
{
    private readonly List<string> _allowedRoots;
    private readonly bool _restrictiveMode;

    public PathValidator(IConfiguration configuration)
    {
        // Load restrictive mode setting (default: false for local dev tools)
        _restrictiveMode = configuration
            .GetValue<bool>("Koan:Context:Security:EnableRestrictivePathValidation", defaultValue: false);

        // Load allowed directories from appsettings.json (only enforced if restrictive mode enabled)
        _allowedRoots = configuration
            .GetSection("Koan:Context:Security:AllowedDirectories")
            .Get<List<string>>() ?? new List<string>();

        // In restrictive mode, always allow user's home directory
        if (_restrictiveMode)
        {
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(homeDir) && !_allowedRoots.Contains(homeDir))
            {
                _allowedRoots.Add(homeDir);
            }
        }
    }

    /// <summary>
    /// Validates that a path is safe to use as a project root
    /// </summary>
    /// <param name="path">Path to validate</param>
    /// <param name="errorMessage">Error message if validation fails</param>
    /// <returns>True if path is valid, false otherwise</returns>
    public bool IsValidProjectPath(string? path, out string? errorMessage)
    {
        errorMessage = null;

        // Check 1: Path must not be null or empty
        if (string.IsNullOrWhiteSpace(path))
        {
            errorMessage = "Path cannot be null or empty";
            return false;
        }

        // Check 2: Path must be absolute (fully qualified)
        if (!Path.IsPathFullyQualified(path))
        {
            errorMessage = $"Path must be absolute. Received: {path}";
            return false;
        }

        // Check 3: Reject paths with null bytes (common attack vector)
        if (path.Contains('\0'))
        {
            errorMessage = "Path contains null byte (security violation)";
            return false;
        }

        // Check 4: Reject UNC paths (\\server\share)
        if (path.StartsWith(@"\\") || path.StartsWith("//"))
        {
            errorMessage = "UNC paths are not allowed for security reasons";
            return false;
        }

        // Check 5: Reject paths with path traversal sequences
        var normalizedPath = Path.GetFullPath(path); // Normalizes ../
        if (normalizedPath.Contains(".."))
        {
            errorMessage = "Path contains path traversal sequence (..)";
            return false;
        }

        // Check 6: Path must be within allowed roots (ONLY if restrictive mode enabled)
        // Restrictive mode is OFF by default for local development tools
        if (_restrictiveMode)
        {
            var isWithinAllowedRoot = _allowedRoots.Any(root =>
            {
                var normalizedRoot = Path.GetFullPath(root);
                return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
            });

            if (!isWithinAllowedRoot)
            {
                errorMessage = $"Path '{path}' is outside allowed directories. " +
                              $"Allowed roots: {string.Join(", ", _allowedRoots)}";
                return false;
            }
        }

        // Note: Directory existence is NOT checked here - validation happens at traversal time
        // Projects can be created for directories that will exist later

        // Check 7: (Unix only) Verify path is not a symbolic link outside allowed roots (only in restrictive mode)
        if (_restrictiveMode && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var linkTarget = ResolveSymbolicLink(path);
            if (linkTarget != null && linkTarget != path)
            {
                // Path is a symlink, verify target is within allowed roots
                var isTargetAllowed = _allowedRoots.Any(root =>
                {
                    var normalizedRoot = Path.GetFullPath(root);
                    return linkTarget.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
                });

                if (!isTargetAllowed)
                {
                    errorMessage = $"Symbolic link target '{linkTarget}' is outside allowed directories";
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Resolves symbolic link to its target (Unix only)
    /// </summary>
    private string? ResolveSymbolicLink(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            return info.LinkTarget ?? path;
        }
        catch
        {
            return path;
        }
    }
}
