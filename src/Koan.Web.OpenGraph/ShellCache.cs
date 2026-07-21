using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Web.OpenGraph;

/// <summary>
/// Reads the SPA shell from disk and caches it in memory, invalidating when the file changes. A
/// last-write-time stat per request is cheap and keeps the cache correct without a file watcher.
/// </summary>
internal sealed class ShellCache
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public async Task<string?> GetShellAsync(string? path, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        var lastWrite = File.GetLastWriteTimeUtc(path);
        if (_entries.TryGetValue(path, out var cached) && cached.LastWriteUtc == lastWrite)
        {
            return cached.Content;
        }

        var content = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        _entries[path] = new Entry(lastWrite, content);
        return content;
    }

    private readonly record struct Entry(DateTime LastWriteUtc, string Content);
}
