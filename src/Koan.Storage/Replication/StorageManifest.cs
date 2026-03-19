using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Koan.Storage.Replication;

/// <summary>
/// Thread-safe in-memory manifest of known storage objects across both tiers.
/// Backed by a JSONL file at <c>.Koan/storage-manifest/{container}.jsonl</c>.
/// </summary>
public sealed class StorageManifest
{
    private readonly ConcurrentDictionary<string, ManifestEntry> _entries = new(StringComparer.Ordinal);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    /// <summary>Returns the manifest entry for the given key, or null if unknown.</summary>
    public ManifestEntry? Get(string key)
    {
        return _entries.TryGetValue(key, out var entry) ? entry : null;
    }

    /// <summary>
    /// Adds or replaces a manifest entry. Returns the new entry for convenience.
    /// </summary>
    public ManifestEntry Set(ManifestEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entries[entry.Key] = entry;
        return entry;
    }

    /// <summary>Removes the entry for the given key. Returns true if it existed.</summary>
    public bool Remove(string key)
    {
        return _entries.TryRemove(key, out _);
    }

    /// <summary>Returns a snapshot of all manifest entries.</summary>
    public IReadOnlyList<ManifestEntry> All()
    {
        return _entries.Values.ToList();
    }

    /// <summary>
    /// Total size of all entries marked as cached (present in local cache).
    /// </summary>
    public long CachedSize()
    {
        long total = 0;
        foreach (var entry in _entries.Values)
        {
            if (entry.Cached)
                total += entry.Size;
        }
        return total;
    }

    /// <summary>
    /// Returns up to <paramref name="count"/> entries eligible for eviction:
    /// synced=true, cached=true, sorted by LastAccess ascending (oldest first).
    /// </summary>
    public IReadOnlyList<ManifestEntry> EvictionCandidates(int count)
    {
        return _entries.Values
            .Where(e => e.Synced && e.Cached)
            .OrderBy(e => e.LastAccess)
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// Loads manifest entries from a JSONL file. Merges into current state
    /// (later lines for the same key overwrite earlier ones).
    /// </summary>
    public async Task LoadAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path))
            return;

        using var reader = new StreamReader(path);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var entry = JsonSerializer.Deserialize<ManifestEntry>(line, JsonOptions);
                if (entry is not null)
                    _entries[entry.Key] = entry;
            }
            catch (JsonException)
            {
                // Skip corrupted lines — append-only format tolerates partial writes
            }
        }
    }

    /// <summary>
    /// Persists the full manifest to a JSONL file (compacted — one line per entry).
    /// Writes atomically via temp file + rename.
    /// </summary>
    public async Task SaveAsync(string path, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tempPath = path + ".tmp-" + Guid.CreateVersion7().ToString("N");

        await using (var writer = new StreamWriter(tempPath))
        {
            foreach (var entry in _entries.Values)
            {
                var json = JsonSerializer.Serialize(entry, JsonOptions);
                await writer.WriteLineAsync(json.AsMemory(), ct);
            }
        }

        // Atomic swap
        if (File.Exists(path))
            File.Delete(path);
        File.Move(tempPath, path);
    }
}
