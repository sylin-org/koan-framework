using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace Koan.Web.Admin.Infrastructure;

internal static class KoanAdminUiAssetProvider
{
    private const string ResourcePrefix = "Koan.Web.Admin.wwwroot.";
    private static readonly Assembly Assembly = typeof(KoanAdminUiAssetProvider).Assembly;

    public static bool TryGetAsset(string? path, out string content, out string contentType)
    {
        Console.WriteLine($"[DEBUG] TryGetAsset: path='{path}'");
        var normalized = Normalize(path);
        Console.WriteLine($"[DEBUG] TryGetAsset: normalized='{normalized}'");
        if (normalized is null)
        {
            content = string.Empty;
            contentType = string.Empty;
            return false;
        }

        var resourceName = ResourcePrefix + normalized.Replace('/', '.');
        Console.WriteLine($"[DEBUG] TryGetAsset: resourceName='{resourceName}'");
        Console.WriteLine($"[DEBUG] TryGetAsset: Available resources:");
        foreach (var res in Assembly.GetManifestResourceNames())
        {
            Console.WriteLine($"[DEBUG]   - {res}");
        }

        using var stream = Assembly.GetManifestResourceStream(resourceName);
        Console.WriteLine($"[DEBUG] TryGetAsset: stream is {(stream == null ? "null" : "not null")}");
        if (stream is null)
        {
            content = string.Empty;
            contentType = string.Empty;
            return false;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: false);
        content = reader.ReadToEnd();
        contentType = GetContentType(normalized);
        return true;
    }

    private static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "index.html";
        }

        var trimmed = path.Replace('\\', '/');
        if (trimmed.StartsWith('/'))
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        return trimmed.Length == 0 ? "index.html" : trimmed;
    }

    private static string GetContentType(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".css" => "text/css; charset=utf-8",
            ".js" => "application/javascript; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            _ => "text/html; charset=utf-8"
        };
}
