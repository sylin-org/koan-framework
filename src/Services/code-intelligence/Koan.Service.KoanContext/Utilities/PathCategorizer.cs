namespace Koan.Context.Utilities;

/// <summary>
/// Provides helpers for working with project-relative paths during indexing.
/// </summary>
public static class PathMetadata
{
    public static string Normalize(string relativePath)
        => relativePath.Replace('\\', '/');

    public static string[] GetPathSegments(string relativePath)
    {
        return Normalize(relativePath)
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .SkipLast(1)
            .ToArray();
    }

    public static string GetDirectoryKey(string relativePath)
    {
        var segments = GetPathSegments(relativePath);
        return segments.Length == 0 ? string.Empty : string.Join('/', segments);
    }
}
