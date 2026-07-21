using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace Koan.Tenancy.Web.Infrastructure;

/// <summary>
/// Serves the bundled operator-console UI from embedded resources (ARCH-0104) — the same manifest-resource pattern
/// as <c>Koan.Web.Admin</c>. Files under <c>wwwroot/</c> are embedded as <c>Koan.Tenancy.Web.wwwroot.*</c>; a null
/// or empty path resolves to <c>index.html</c>. Traversal (<c>..</c>) is refused.
/// </summary>
internal static class TenancyConsoleAssetProvider
{
    private const string ResourcePrefix = "Koan.Tenancy.Web.wwwroot.";
    private static readonly Assembly Assembly = typeof(TenancyConsoleAssetProvider).Assembly;

    public static bool TryGetAsset(string? path, out byte[] content, out string contentType)
    {
        content = Array.Empty<byte>();
        contentType = "";

        var normalized = Normalize(path);
        if (normalized is null) return false;

        var resourceName = ResourcePrefix + normalized.Replace('/', '.');
        using var stream = Assembly.GetManifestResourceStream(resourceName);
        if (stream is null) return false;

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        content = ms.ToArray();
        contentType = GetContentType(normalized);
        return true;
    }

    private static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "index.html";
        var trimmed = path.Replace('\\', '/');
        if (trimmed.StartsWith('/')) trimmed = trimmed[1..];
        if (trimmed.Contains("..", StringComparison.Ordinal)) return null; // no traversal
        return trimmed.Length == 0 ? "index.html" : trimmed;
    }

    private static string GetContentType(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".css" => "text/css; charset=utf-8",
            ".js" => "application/javascript; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            _ => "text/html; charset=utf-8",
        };
}
