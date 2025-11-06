using Microsoft.Extensions.FileSystemGlobbing;

namespace Koan.Context.Utilities;

/// <summary>
/// Derives semantic categories from file paths
/// </summary>
public static class PathCategorizer
{
    private static readonly (string Category, string[] Patterns)[] Rules = new[]
    {
        ("decision", new[] { "**/decisions/**", "**/adr/**", "**/*-decision.md", "**/ADR/**" }),
        ("guide", new[] { "**/guides/**", "**/tutorials/**", "**/howto/**", "**/how-to/**" }),
        ("api-doc", new[] { "**/api/**", "**/reference/**", "**/api-reference/**" }),
        ("architecture", new[] { "**/architecture/**", "**/arch/**", "**/*-arch.md" }),
        ("test", new[] { "**/tests/**", "**/*Tests.cs", "**/*Test.cs", "**/*.test.*", "**/*.spec.*", "**/test/**" }),
        ("config", new[] { "**/*.json", "**/*.yaml", "**/*.yml", "**/*.toml", "**/*.xml", "**/*.ini", "**/*.conf" }),
        ("source", new[] { "**/src/**", "**/lib/**", "**/*.cs", "**/*.ts", "**/*.js", "**/*.py", "**/*.java", "**/*.go", "**/*.rs" }),
        ("documentation", new[] { "**/*.md", "**/docs/**", "**/doc/**" })
    };

    /// <summary>
    /// Derives category from a relative file path
    /// </summary>
    public static string? DeriveCategory(string relativePath)
    {
        // Normalize path separators
        var normalized = relativePath.Replace('\\', '/');

        foreach (var (category, patterns) in Rules)
        {
            foreach (var pattern in patterns)
            {
                if (IsMatch(normalized, pattern))
                    return category;
            }
        }

        return "other";
    }

    /// <summary>
    /// Extracts path segments (directory components) from a path
    /// </summary>
    public static string[] GetPathSegments(string relativePath)
    {
        return relativePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .SkipLast(1)  // Remove filename
            .ToArray();
    }

    /// <summary>
    /// Simple glob pattern matching
    /// </summary>
    private static bool IsMatch(string path, string pattern)
    {
        var matcher = new Matcher();
        matcher.AddInclude(pattern);
        return matcher.Match(path).HasMatches;
    }
}
