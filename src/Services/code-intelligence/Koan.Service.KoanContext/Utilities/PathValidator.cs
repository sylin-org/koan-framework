using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using Koan.Service.KoanContext.Infrastructure;

namespace Koan.Context.Utilities;

/// <summary>
/// Validates file system paths to prevent path traversal attacks.
/// </summary>
public class PathValidator
{
    private static readonly string[] WindowsReservedNames =
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private readonly List<string> _allowedRoots;
    private readonly bool _restrictiveMode;
    private readonly int _maxPathLength;

    public PathValidator(IConfiguration configuration)
    {
        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        _maxPathLength = configuration
            .GetValue<int?>(Constants.Security.MaxPathLengthKey)
            .GetValueOrDefault(Constants.Security.MaxProjectPathLength);

        _allowedRoots = configuration
            .GetSection(Constants.Security.AllowedDirectoriesSection)
            .Get<List<string>>() ?? new List<string>();

        // Normalize allowed roots for comparison.
        _allowedRoots = _allowedRoots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(Path.GetFullPath)
            .Select(EnsureTrailingSeparator)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var restrictiveFlag = configuration
            .GetValue<bool>(Constants.Security.RestrictiveValidationFlag, defaultValue: false);

        _restrictiveMode = restrictiveFlag || _allowedRoots.Count > 0;

        if (_restrictiveMode)
        {
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(homeDir))
            {
                var normalizedHome = EnsureTrailingSeparator(Path.GetFullPath(homeDir));
                if (!_allowedRoots.Any(root => root.Equals(normalizedHome, StringComparison.OrdinalIgnoreCase)))
                {
                    _allowedRoots.Add(normalizedHome);
                }
            }
        }
    }

    /// <summary>
    /// Validates that a path is safe to use as a project root.
    /// </summary>
    public bool IsValidProjectPath(string? path, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            errorMessage = "Path cannot be null or empty";
            return false;
        }

        if (path.Contains('\0'))
        {
            errorMessage = "Path contains null byte (security violation)";
            return false;
        }

        if (path.Length > _maxPathLength)
        {
            errorMessage = $"Path exceeds the maximum length of {_maxPathLength} characters";
            return false;
        }

        if (!Path.IsPathFullyQualified(path))
        {
            errorMessage = $"Path must be absolute. Received: {path}";
            return false;
        }

        if (IsUncPath(path))
        {
            errorMessage = "UNC paths are not allowed for security reasons";
            return false;
        }

        if (ContainsParentTraversal(path))
        {
            errorMessage = "Path contains parent directory traversal sequence (..)";
            return false;
        }

        var normalizedPath = Path.GetFullPath(path);

        if (normalizedPath.Length > _maxPathLength)
        {
            errorMessage = $"Path exceeds the maximum length of {_maxPathLength} characters";
            return false;
        }

        if (!Directory.Exists(normalizedPath))
        {
            errorMessage = $"Path does not exist: {path}";
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            var leafName = Path.GetFileName(normalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrWhiteSpace(leafName) && IsWindowsReservedName(leafName))
            {
                errorMessage = $"Windows reserved name '{leafName}' is not allowed";
                return false;
            }
        }

        if (_restrictiveMode)
        {
            var candidate = EnsureTrailingSeparator(normalizedPath);
            var withinAllowedRoot = _allowedRoots.Any(root =>
                candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase));

            if (!withinAllowedRoot)
            {
                errorMessage = $"Path '{path}' is outside allowed directories. Allowed roots: {string.Join(", ", _allowedRoots)}";
                return false;
            }
        }

        if (_restrictiveMode && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var linkTarget = ResolveSymbolicLink(normalizedPath);
            if (!string.IsNullOrEmpty(linkTarget) && !string.Equals(linkTarget, normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                var normalizedTarget = EnsureTrailingSeparator(Path.GetFullPath(linkTarget));
                var isTargetAllowed = _allowedRoots.Any(root =>
                    normalizedTarget.StartsWith(root, StringComparison.OrdinalIgnoreCase));

                if (!isTargetAllowed)
                {
                    errorMessage = $"Symbolic link target '{linkTarget}' is outside allowed directories";
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsUncPath(string path)
        => path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal);

    private static bool ContainsParentTraversal(string path)
    {
        var sanitized = NormalizeSeparators(path);
        var segments = sanitized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment => segment == "..");
    }

    private static string NormalizeSeparators(string path)
        => path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    private static string EnsureTrailingSeparator(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        if (!path.EndsWith(Path.DirectorySeparatorChar) && !path.EndsWith(Path.AltDirectorySeparatorChar))
        {
            return path + Path.DirectorySeparatorChar;
        }

        return path;
    }

    private static bool IsWindowsReservedName(string name)
        => WindowsReservedNames.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves symbolic link to its target (Unix only).
    /// </summary>
    private static string? ResolveSymbolicLink(string path)
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
