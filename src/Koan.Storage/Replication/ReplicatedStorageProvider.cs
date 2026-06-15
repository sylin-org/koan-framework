using System.Runtime.CompilerServices;
using Koan.Storage.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Koan.Storage.Replication;

/// <summary>
/// Composes two <see cref="IStorageProvider"/> instances (cache + durable) into a
/// replicated storage provider with write-through, pull-through, background sync,
/// and watermark-based eviction.
///
/// Provider-agnostic: works with any IStorageProvider pair (local+S3, local+local, etc.).
/// </summary>
public sealed class ReplicatedStorageProvider : IStorageProvider, IStatOperations, IListOperations, IDisposable
{
    private readonly IStorageProvider _cache;
    private readonly IStorageProvider _durable;
    private readonly string _container;
    private readonly LocalCacheOptions _cacheOptions;
    private readonly ILogger _logger;

    private readonly StorageManifest _manifest = new();
    private readonly SyncJournal _journal = new();

    private readonly CancellationTokenSource _cts = new();
    private readonly Task _syncTask;

    private readonly string _manifestPath;
    private readonly string _journalPath;

    private bool _disposed;
    private bool _manifestPopulated;

    private static readonly TimeSpan SyncInterval = TimeSpan.FromSeconds(5);

    public ReplicatedStorageProvider(
        IStorageProvider cache,
        IStorageProvider durable,
        string container,
        LocalCacheOptions? cacheOptions = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(durable);
        ArgumentException.ThrowIfNullOrWhiteSpace(container);

        _cache = cache;
        _durable = durable;
        _container = container;
        _cacheOptions = cacheOptions ?? new LocalCacheOptions();
        _logger = logger ?? NullLogger.Instance;

        _manifestPath = Path.Combine(".Koan", "storage-manifest", $"{container}.jsonl");
        _journalPath = Path.Combine(".Koan", "storage-sync", container, "journal.jsonl");

        // Load persisted state (fire-and-forget — manifest/journal populate before first sync)
        _ = InitializeAsync();

        _syncTask = Task.Run(() => BackgroundSyncLoop(_cts.Token));
    }

    public string Name => $"replicated:{_cache.Name}+{_durable.Name}";

    public StorageProviderCapabilities Capabilities => new(
        SupportsSequentialRead: true,
        SupportsSeek: _cache.Capabilities.SupportsSeek,
        SupportsPresignedRead: false,
        SupportsServerSideCopy: false);

    // ── Write flow ────────────────────────────────────────────────

    public async Task Write(string container, string key, Stream content, string? contentType, CancellationToken ct = default)
    {
        // Write to cache synchronously (from caller's perspective)
        await _cache.Write(container, key, content, contentType, ct);

        // Determine size for manifest
        long size = 0;
        if (content.CanSeek)
        {
            // NotSupportedException only: Length is an optional stream capability; a cache
            // Head below recovers the real size. Any other failure is a real I/O error.
            try { size = content.Length; } catch (NotSupportedException) { /* recovered via cache Head */ }
        }

        if (size == 0 && _cache is IStatOperations cacheStat)
        {
            var stat = await cacheStat.Head(container, key, ct);
            if (stat?.Length is long len) size = len;
        }

        // Update manifest: cached but not yet synced
        _manifest.Set(new ManifestEntry
        {
            Key = key,
            Size = size,
            Cached = true,
            Synced = false,
            LastAccess = DateTimeOffset.UtcNow
        });

        // Append to sync journal
        _journal.Append(new SyncJournalEntry
        {
            Op = SyncOperationType.Put,
            Container = container,
            Key = key,
            Timestamp = DateTimeOffset.UtcNow
        }, _journalPath);

        _logger.LogDebug("Replicated: wrote {Key} to cache, queued for sync", key);
    }

    // ── Read flow ─────────────────────────────────────────────────

    public async Task<Stream> OpenRead(string container, string key, CancellationToken ct = default)
    {
        // Try cache first
        try
        {
            var stream = await _cache.OpenRead(container, key, ct);

            // Update last access in manifest
            var existing = _manifest.Get(key);
            if (existing is not null)
                _manifest.Set(existing with { LastAccess = DateTimeOffset.UtcNow });

            return stream;
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            // Cache miss — fall through to durable
        }

        // Try durable
        Stream durableStream;
        try
        {
            durableStream = await _durable.OpenRead(container, key, ct);
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            throw new FileNotFoundException($"Object '{key}' not found in cache or durable storage.", key);
        }

        // Pull-through: copy from durable into cache
        var pulled = await PullThrough(container, key, durableStream, ct);

        _logger.LogDebug("Replicated: pull-through for {Key} from durable to cache", key);

        return pulled;
    }

    public async Task<(Stream Stream, long? Length)> OpenReadRange(string container, string key, long? from, long? to, CancellationToken ct = default)
    {
        // Try cache first
        try
        {
            var result = await _cache.OpenReadRange(container, key, from, to, ct);

            var existing = _manifest.Get(key);
            if (existing is not null)
                _manifest.Set(existing with { LastAccess = DateTimeOffset.UtcNow });

            return result;
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            // fall through
        }

        // Fall back to durable
        return await _durable.OpenReadRange(container, key, from, to, ct);
    }

    // ── Delete flow ───────────────────────────────────────────────

    public async Task<bool> Delete(string container, string key, CancellationToken ct = default)
    {
        // Delete from cache (ignore if missing)
        try
        {
            await _cache.Delete(container, key, ct);
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            // Expected when file is not cached
        }

        // Remove from manifest
        _manifest.Remove(key);

        // Append delete to journal for background sync to durable
        _journal.Append(new SyncJournalEntry
        {
            Op = SyncOperationType.Delete,
            Container = container,
            Key = key,
            Timestamp = DateTimeOffset.UtcNow
        }, _journalPath);

        _logger.LogDebug("Replicated: deleted {Key} from cache, queued durable delete", key);
        return true;
    }

    // ── Exists ────────────────────────────────────────────────────

    public async Task<bool> Exists(string container, string key, CancellationToken ct = default)
    {
        // Check manifest first
        var entry = _manifest.Get(key);
        if (entry is not null && (entry.Cached || entry.Synced))
            return true;

        // Fall back to cache
        try
        {
            if (await _cache.Exists(container, key, ct))
                return true;
        }
        catch (Exception ex)
        {
            // Cache existence check failed — degrade to durable (the authoritative store).
            // Kept broad on purpose: any cache fault must fall through to durable, but no
            // longer silently so a flaky cache provider is diagnosable.
            _logger.LogDebug(ex, "Replicated: cache Exists check failed for {Key}, falling back to durable", key);
        }

        // Fall back to durable
        return await _durable.Exists(container, key, ct);
    }

    // ── Head ──────────────────────────────────────────────────────

    public async Task<ObjectStat?> Head(string container, string key, CancellationToken ct = default)
    {
        // Try cache first
        if (_cache is IStatOperations cacheStat)
        {
            try
            {
                var stat = await cacheStat.Head(container, key, ct);
                if (stat is not null)
                {
                    UpdateManifestFromStat(key, stat, cached: true);
                    return stat;
                }
            }
            catch (Exception ex)
            {
                // Cache Head failed — fall through to durable. Kept broad on purpose so any
                // cache fault still degrades to durable, but no longer silently.
                _logger.LogDebug(ex, "Replicated: cache Head failed for {Key}, falling back to durable", key);
            }
        }

        // Try durable
        if (_durable is IStatOperations durableStat)
        {
            var stat = await durableStat.Head(container, key, ct);
            if (stat is not null)
            {
                UpdateManifestFromStat(key, stat, cached: false);
                return stat;
            }
        }

        return null;
    }

    // ── ListObjects ───────────────────────────────────────────────

    public async IAsyncEnumerable<StorageObjectInfo> ListObjects(
        string container,
        string? prefix = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Populate manifest from durable on first call if empty
        if (!_manifestPopulated && _durable is IListOperations durableList)
        {
            await PopulateManifestFromDurable(durableList, container, ct);
            _manifestPopulated = true;
        }

        var entries = _manifest.All();
        foreach (var entry in entries)
        {
            if (prefix is not null && !entry.Key.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            yield return new StorageObjectInfo(
                Key: entry.Key,
                Size: entry.Size,
                LastModified: entry.LastAccess,
                ETag: entry.ETag);
        }
    }

    // ── Background sync ───────────────────────────────────────────

    private async Task BackgroundSyncLoop(CancellationToken ct)
    {
        // Small initial delay to let initialization complete
        try { await Task.Delay(TimeSpan.FromMilliseconds(500), ct); }
        catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await DrainAndSync(ct);
                await RunEvictionIfNeeded(ct);
                await PersistManifest(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Replicated: sync cycle failed, will retry next interval");
            }

            try { await Task.Delay(SyncInterval, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task DrainAndSync(CancellationToken ct)
    {
        var entries = _journal.Drain();
        if (entries.Count == 0)
            return;

        _logger.LogDebug("Replicated: draining {Count} journal entries", entries.Count);

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                switch (entry.Op)
                {
                    case SyncOperationType.Put:
                        await SyncPut(entry, ct);
                        break;

                    case SyncOperationType.Delete:
                        await SyncDelete(entry, ct);
                        break;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Re-queue remaining on shutdown
                RequeueEntries(entries, entries.ToList().IndexOf(entry));
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Replicated: sync failed for {Op} {Key}, will retry", entry.Op, entry.Key);
                // Re-queue this entry for next cycle
                _journal.Append(entry, _journalPath);
            }
        }

        // Truncate journal file after successful drain
        SyncJournal.Truncate(_journalPath);
    }

    private async Task SyncPut(SyncJournalEntry entry, CancellationToken ct)
    {
        // Read from cache
        Stream cacheStream;
        try
        {
            cacheStream = await _cache.OpenRead(entry.Container, entry.Key, ct);
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            _logger.LogWarning("Replicated: cache miss during sync for {Key} — file may have been evicted or deleted", entry.Key);
            return;
        }

        await using (cacheStream)
        {
            await _durable.Write(entry.Container, entry.Key, cacheStream, null, ct);
        }

        // Mark synced in manifest
        var manifest = _manifest.Get(entry.Key);
        if (manifest is not null)
            _manifest.Set(manifest with { Synced = true });

        _logger.LogDebug("Replicated: pushed {Key} to durable", entry.Key);
    }

    private async Task SyncDelete(SyncJournalEntry entry, CancellationToken ct)
    {
        try
        {
            await _durable.Delete(entry.Container, entry.Key, ct);
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            // Already gone from durable — acceptable
        }

        _logger.LogDebug("Replicated: deleted {Key} from durable", entry.Key);
    }

    // ── Eviction ──────────────────────────────────────────────────

    private async Task RunEvictionIfNeeded(CancellationToken ct)
    {
        if (string.Equals(_cacheOptions.Policy, "pinned", StringComparison.OrdinalIgnoreCase))
            return;

        var maxBytes = _cacheOptions.ParseMaxSizeBytes();
        if (maxBytes is null or <= 0)
            return; // Unlimited cache — no eviction

        var currentSize = _manifest.CachedSize();
        var highThreshold = (long)(maxBytes.Value * _cacheOptions.HighWatermark / 100.0);
        var lowThreshold = (long)(maxBytes.Value * _cacheOptions.LowWatermark / 100.0);

        if (currentSize <= highThreshold)
            return;

        _logger.LogInformation(
            "Replicated: cache size {Current} exceeds high watermark {High}, evicting to {Low}",
            currentSize, highThreshold, lowThreshold);

        // Get candidates: synced=true, sorted by lastAccess ascending
        var candidates = _manifest.EvictionCandidates(count: int.MaxValue);

        foreach (var candidate in candidates)
        {
            if (currentSize <= lowThreshold)
                break;

            ct.ThrowIfCancellationRequested();

            try
            {
                await _cache.Delete(_container, candidate.Key, ct);
            }
            catch (Exception ex) when (IsNotFound(ex))
            {
                // Already gone
            }

            currentSize -= candidate.Size;
            _manifest.Set(candidate with { Cached = false });

            _logger.LogDebug("Replicated: evicted {Key} ({Size} bytes)", candidate.Key, candidate.Size);
        }
    }

    // ── Initialization ────────────────────────────────────────────

    private async Task InitializeAsync()
    {
        try
        {
            await _manifest.Load(_manifestPath);
            await _journal.Load(_journalPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Replicated: failed to load persisted state, starting fresh");
        }
    }

    private async Task PopulateManifestFromDurable(IListOperations durableList, string container, CancellationToken ct)
    {
        try
        {
            await foreach (var obj in durableList.ListObjects(container, prefix: null, ct))
            {
                var existing = _manifest.Get(obj.Key);
                if (existing is null)
                {
                    _manifest.Set(new ManifestEntry
                    {
                        Key = obj.Key,
                        Size = obj.Size,
                        ETag = obj.ETag,
                        Cached = false,
                        Synced = true,
                        LastAccess = obj.LastModified
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Replicated: failed to populate manifest from durable, will retry later");
        }
    }

    private async Task PersistManifest(CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            await _manifest.Save(_manifestPath, ct);
        }
        catch (OperationCanceledException)
        {
            // Don't log on shutdown
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Replicated: failed to persist manifest");
        }
    }

    // ── Pull-through helper ───────────────────────────────────────

    private async Task<Stream> PullThrough(string container, string key, Stream durableStream, CancellationToken ct)
    {
        // Buffer into memory, write to cache, return seekable stream
        var ms = new MemoryStream();
        await durableStream.CopyToAsync(ms, ct);
        await durableStream.DisposeAsync();

        long size = ms.Length;
        ms.Position = 0;

        // Write to cache
        await _cache.Write(container, key, ms, null, ct);

        // Update manifest
        _manifest.Set(new ManifestEntry
        {
            Key = key,
            Size = size,
            Cached = true,
            Synced = true,
            LastAccess = DateTimeOffset.UtcNow
        });

        // Return a fresh stream from cache
        try
        {
            return await _cache.OpenRead(container, key, ct);
        }
        catch (Exception ex)
        {
            // Re-open of the just-written cache entry failed; return the buffered copy so
            // the caller still gets the data. Kept broad on purpose; logged so a cache that
            // fails immediately after Write is diagnosable.
            _logger.LogDebug(ex, "Replicated: re-open after pull-through Write failed for {Key}, returning buffered copy", key);
            ms.Position = 0;
            return ms;
        }
    }

    // ── Manifest helpers ──────────────────────────────────────────

    private void UpdateManifestFromStat(string key, ObjectStat stat, bool cached)
    {
        var existing = _manifest.Get(key);
        _manifest.Set(new ManifestEntry
        {
            Key = key,
            Size = stat.Length ?? existing?.Size ?? 0,
            ETag = stat.ETag ?? existing?.ETag,
            Cached = cached || (existing?.Cached ?? false),
            Synced = !cached || (existing?.Synced ?? false),
            LastAccess = DateTimeOffset.UtcNow
        });
    }

    private void RequeueEntries(IReadOnlyList<SyncJournalEntry> entries, int fromIndex)
    {
        for (int i = fromIndex; i < entries.Count; i++)
        {
            _journal.Append(entries[i], _journalPath);
        }
    }

    // ── Not-found detection ───────────────────────────────────────

    private static bool IsNotFound(Exception ex)
    {
        if (ex is FileNotFoundException) return true;
        if (ex is DirectoryNotFoundException) return true;

        // Common patterns from storage providers
        var msg = ex.Message;
        if (msg.Contains("not found", StringComparison.OrdinalIgnoreCase)) return true;
        if (msg.Contains("does not exist", StringComparison.OrdinalIgnoreCase)) return true;
        if (msg.Contains("404", StringComparison.Ordinal)) return true;
        if (msg.Contains("NoSuchKey", StringComparison.OrdinalIgnoreCase)) return true;

        if (ex.InnerException is not null)
            return IsNotFound(ex.InnerException);

        return false;
    }

    // ── Dispose ───────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();

        try
        {
            // Give the sync task a brief window to finish its current cycle
            _syncTask.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
            // Expected on cancellation
        }

        // Final persist (best-effort: Dispose must never throw)
        try { _manifest.Save(_manifestPath).GetAwaiter().GetResult(); }
        catch (Exception ex)
        {
            // Kept broad on purpose — Dispose must not throw — but logged so a lost final
            // manifest persist on shutdown is diagnosable.
            _logger.LogDebug(ex, "Replicated: final manifest persist on dispose failed (best-effort)");
        }

        _cts.Dispose();

        if (_cache is IDisposable cacheDisposable)
            cacheDisposable.Dispose();
        if (_durable is IDisposable durableDisposable)
            durableDisposable.Dispose();
    }
}
