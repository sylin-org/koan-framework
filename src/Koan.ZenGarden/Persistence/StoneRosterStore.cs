using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Koan.ZenGarden.Persistence;

/// <summary>
/// File-based Stone roster store with merge-on-write and atomic rename.
/// </summary>
internal sealed class StoneRosterStore : IStoneRosterStore
{
    private readonly string _filePath;
    private readonly TimeSpan _ttl;
    private readonly ILogger<StoneRosterStore> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public StoneRosterStore(string filePath, TimeSpan ttl, ILogger<StoneRosterStore> logger)
    {
        _filePath = filePath;
        _ttl = ttl;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CachedMossStone>> Load(CancellationToken ct = default)
    {
        try
        {
            MigrateLegacyFilename();

            if (!File.Exists(_filePath))
            {
                return [];
            }

            var json = await File.ReadAllTextAsync(_filePath, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            var entries = JsonSerializer.Deserialize<List<CachedMossStone>>(json, SerializerOptions);
            if (entries is null || entries.Count == 0)
            {
                return [];
            }

            var valid = FilterExpired(entries);
            _logger.LogDebug(
                "Loaded {Count} stones from persisted roster ({Expired} expired, path={Path})",
                valid.Count, entries.Count - valid.Count, _filePath);
            return valid;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to load persisted stone roster from {Path}", _filePath);
            return [];
        }
    }

    public async Task Persist(IEnumerable<CachedMossStone> stones, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var incoming = stones.ToList();

            // Merge with existing entries from disk (sibling containers may have written)
            var existing = await ReadFileQuietly(ct).ConfigureAwait(false);

            var merged = new Dictionary<string, CachedMossStone>(StringComparer.OrdinalIgnoreCase);
            foreach (var stone in existing)
            {
                merged[stone.CacheKey] = stone;
            }

            foreach (var stone in incoming)
            {
                if (!merged.TryGetValue(stone.CacheKey, out var prev) ||
                    stone.LastSeenUtc >= prev.LastSeenUtc)
                {
                    merged[stone.CacheKey] = stone;
                }
            }

            var toWrite = FilterExpired(merged.Values);

            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(toWrite, SerializerOptions);
            var tmpPath = _filePath + ".tmp";
            await File.WriteAllTextAsync(tmpPath, json, ct).ConfigureAwait(false);
            File.Move(tmpPath, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to persist stone roster to {Path}", _filePath);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void MigrateLegacyFilename()
    {
        if (File.Exists(_filePath))
            return;

        var dir = Path.GetDirectoryName(_filePath);
        if (string.IsNullOrEmpty(dir))
            return;

        var legacyPath = Path.Combine(dir, Constants.Persistence.LegacyRosterFileName);
        if (!File.Exists(legacyPath))
            return;

        try
        {
            File.Move(legacyPath, _filePath);
            _logger.LogInformation(
                "Migrated legacy roster file: {Legacy} → {Current}",
                legacyPath, _filePath);
        }
        catch (Exception ex)
        {
            // Race-safe: on shared volumes, multiple containers may attempt the rename
            // simultaneously. The first succeeds; others get FileNotFoundException.
            // This is expected and non-fatal — the winning container's rename stands.
            _logger.LogDebug(ex,
                "Could not migrate legacy roster file {Legacy}", legacyPath);
        }
    }

    private IReadOnlyList<CachedMossStone> FilterExpired(IEnumerable<CachedMossStone> stones)
    {
        var cutoff = DateTimeOffset.UtcNow - _ttl;
        return stones
            .Where(s => s.LastSeenUtc > cutoff)
            .ToArray();
    }

    private async Task<List<CachedMossStone>> ReadFileQuietly(CancellationToken ct)
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return [];
            }

            var json = await File.ReadAllTextAsync(_filePath, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<CachedMossStone>>(json, SerializerOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
