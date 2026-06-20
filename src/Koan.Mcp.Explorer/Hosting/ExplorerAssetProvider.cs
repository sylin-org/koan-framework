using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace Koan.Mcp.Explorer.Hosting;

/// <summary>
/// Serves the embedded console SPA (index.html / explorer.css / app.js) from the assembly manifest. Embedded-only
/// (container-safe, no disk dependency), path-traversal-guarded, content-type mapped.
/// </summary>
internal static class ExplorerAssetProvider
{
    private const string ResourcePrefix = "Koan.Mcp.Explorer.wwwroot.";
    private static readonly Assembly Assembly = typeof(ExplorerAssetProvider).Assembly;

    public static bool TryGet(string? path, out string content, out string contentType)
    {
        var normalized = Normalize(path);
        if (normalized is null)
        {
            content = "";
            contentType = "";
            return false;
        }

        var resourceName = ResourcePrefix + normalized.Replace('/', '.');
        using var stream = Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            content = "";
            contentType = "";
            return false;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: false);
        content = reader.ReadToEnd();
        contentType = GetContentType(normalized);
        return true;
    }

    private static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "index.html";
        var trimmed = path.Replace('\\', '/');
        if (trimmed.StartsWith('/')) trimmed = trimmed[1..];
        if (trimmed.Contains("..", StringComparison.Ordinal)) return null; // path-traversal guard
        return trimmed.Length == 0 ? "index.html" : trimmed;
    }

    private static string GetContentType(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".css" => "text/css; charset=utf-8",
            ".js" => "application/javascript; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".svg" => "image/svg+xml",
            _ => "text/html; charset=utf-8"
        };
}
