using Microsoft.Extensions.FileSystemGlobbing;

namespace Koan.Context.Utilities;

/// <summary>
/// Parses .gitignore files and provides exclusion matching
/// </summary>
public class GitignoreParser
{
    private readonly List<string> _patterns = new();
    private readonly Matcher _matcher = new();

    /// <summary>
    /// Loads .gitignore patterns from a directory
    /// </summary>
    public static GitignoreParser LoadFromDirectory(string rootPath)
    {
        var parser = new GitignoreParser();
        var gitignorePath = Path.Combine(rootPath, ".gitignore");

        if (File.Exists(gitignorePath))
        {
            var lines = File.ReadAllLines(gitignorePath);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                // Skip comments and empty lines
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
                    continue;

                parser.AddPattern(trimmed);
            }
        }

        // Add common exclusions even without .gitignore
        parser.AddCommonPatterns();

        return parser;
    }

    private void AddPattern(string pattern)
    {
        _patterns.Add(pattern);
        _matcher.AddExclude(pattern);
    }

    private void AddCommonPatterns()
    {
        var commonPatterns = new[]
        {
            "bin/", "obj/", "node_modules/", ".git/", ".vs/", ".vscode/",
            "*.dll", "*.exe", "*.pdb", "*.cache", "*.user", "*.suo",
            ".DS_Store", "Thumbs.db", "desktop.ini",
            "*.log", "*.tmp", "*.temp",
            "dist/", "build/", "out/", "target/",
            "__pycache__/", "*.pyc", ".pytest_cache/",
            ".gradle/", ".idea/"
        };

        foreach (var pattern in commonPatterns)
        {
            if (!_patterns.Contains(pattern))
            {
                _patterns.Add(pattern);
                _matcher.AddExclude(pattern);
            }
        }
    }

    /// <summary>
    /// Checks if a relative path should be excluded
    /// </summary>
    public bool ShouldExclude(string relativePath)
    {
        // Normalize path separators
        var normalized = relativePath.Replace('\\', '/');

        // Use matcher for exclusion check
        var result = _matcher.Match(normalized);
        return !result.HasMatches;  // If no matches, it means it was excluded
    }
}
