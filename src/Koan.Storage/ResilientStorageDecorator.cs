using Koan.Storage.Abstractions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Koan.Storage;

/// <summary>
/// Wraps any <see cref="IStorageProvider"/> with write-behind cache (WAL).
///
/// When the circuit is Open (primary unavailable), writes go to a local
/// write-ahead log (journal.jsonl + staging/). When the circuit closes
/// (primary recovers), the WAL is replayed in order.
///
/// Circuit state is managed externally — call <see cref="SetCircuitOpen"/>
/// and <see cref="SetCircuitClosed"/> from <c>GardenAwareEndpointManager</c>.
/// </summary>
public sealed class ResilientStorageDecorator : IStorageProvider, IStatOperations, IDisposable
{
    private readonly IStorageProvider _inner;
    private readonly ILogger _logger;
    private readonly string _walBasePath;
    private readonly long _maxWalSizeBytes;
    private readonly object _walLock = new();
    private readonly object _stateLock = new();

    private bool _circuitOpen;
    private bool _replaying;

    public ResilientStorageDecorator(
        IStorageProvider inner,
        ILogger logger,
        string walBasePath,
        long maxWalSizeBytes = 500 * 1024 * 1024)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(logger);

        _inner = inner;
        _logger = logger;
        _walBasePath = walBasePath;
        _maxWalSizeBytes = maxWalSizeBytes;
    }

    public string Name => _inner.Name;

    public StorageProviderCapabilities Capabilities => _inner.Capabilities;

    /// <summary>
    /// Signal that the primary is unavailable. Writes will go to WAL.
    /// </summary>
    public void SetCircuitOpen()
    {
        lock (_stateLock)
        {
            if (_circuitOpen) return;
            _circuitOpen = true;
            _logger.LogWarning("ResilientStorageDecorator: circuit opened for provider {Provider}", _inner.Name);
        }
    }

    /// <summary>
    /// Signal that the primary is available again. Triggers WAL replay.
    /// </summary>
    public void SetCircuitClosed()
    {
        lock (_stateLock)
        {
            if (!_circuitOpen) return;
            _circuitOpen = false;
            _logger.LogInformation("ResilientStorageDecorator: circuit closed for provider {Provider}", _inner.Name);
        }

        // Fire-and-forget WAL replay
        _ = Task.Run(() => ReplayWalAsync(CancellationToken.None));
    }

    public async Task WriteAsync(string container, string key, Stream content, string? contentType, CancellationToken ct = default)
    {
        if (!IsCircuitOpen())
        {
            try
            {
                await _inner.WriteAsync(container, key, content, contentType, ct);
                return;
            }
            catch (Exception ex) when (IsTransportFailure(ex))
            {
                _logger.LogWarning(ex, "ResilientStorageDecorator: write failed, falling back to WAL for {Container}/{Key}", container, key);
                SetCircuitOpen();
                // Reset stream if possible for WAL write
                if (content.CanSeek) content.Position = 0;
            }
        }

        await WriteToWalAsync(container, key, content, contentType, ct);
    }

    public async Task<Stream> OpenReadAsync(string container, string key, CancellationToken ct = default)
    {
        if (!IsCircuitOpen())
        {
            return await _inner.OpenReadAsync(container, key, ct);
        }

        // Try to read from WAL staging
        var stagingPath = GetStagingPath(container, key);
        if (File.Exists(stagingPath))
        {
            return new FileStream(stagingPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        throw new InvalidOperationException(
            $"Storage provider '{_inner.Name}' is unavailable and key '{key}' is not in the local WAL.");
    }

    public async Task<(Stream Stream, long? Length)> OpenReadRangeAsync(
        string container, string key, long? from, long? to, CancellationToken ct = default)
    {
        if (!IsCircuitOpen())
        {
            return await _inner.OpenReadRangeAsync(container, key, from, to, ct);
        }

        // Try WAL staging for range reads
        var stagingPath = GetStagingPath(container, key);
        if (File.Exists(stagingPath))
        {
            var fi = new FileInfo(stagingPath);
            long start = from ?? 0;
            long end = to.HasValue ? Math.Min(to.Value, fi.Length - 1) : fi.Length - 1;
            long length = end - start + 1;

            var fs = new FileStream(stagingPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            fs.Seek(start, SeekOrigin.Begin);
            var ms = new MemoryStream((int)Math.Min(length, int.MaxValue));
            await CopyRangeAsync(fs, ms, length, ct);
            fs.Dispose();
            ms.Position = 0;
            return (ms, length);
        }

        throw new InvalidOperationException(
            $"Storage provider '{_inner.Name}' is unavailable and key '{key}' is not in the local WAL.");
    }

    public async Task<bool> DeleteAsync(string container, string key, CancellationToken ct = default)
    {
        if (!IsCircuitOpen())
        {
            try
            {
                return await _inner.DeleteAsync(container, key, ct);
            }
            catch (Exception ex) when (IsTransportFailure(ex))
            {
                _logger.LogWarning(ex, "ResilientStorageDecorator: delete failed, recording in WAL for {Container}/{Key}", container, key);
                SetCircuitOpen();
            }
        }

        AppendJournalEntry(new WalEntry
        {
            Op = WalOp.Delete,
            Container = container,
            Key = key,
            Timestamp = DateTimeOffset.UtcNow
        });
        return true;
    }

    public async Task<bool> ExistsAsync(string container, string key, CancellationToken ct = default)
    {
        if (!IsCircuitOpen())
        {
            return await _inner.ExistsAsync(container, key, ct);
        }

        // Check WAL staging as best-effort
        var stagingPath = GetStagingPath(container, key);
        return File.Exists(stagingPath);
    }

    public async Task<ObjectStat?> HeadAsync(string container, string key, CancellationToken ct = default)
    {
        if (!IsCircuitOpen())
        {
            if (_inner is IStatOperations stat)
                return await stat.HeadAsync(container, key, ct);
            return null;
        }

        // Best-effort from WAL staging
        var stagingPath = GetStagingPath(container, key);
        if (File.Exists(stagingPath))
        {
            var fi = new FileInfo(stagingPath);
            return new ObjectStat(fi.Length, null, fi.LastWriteTimeUtc, null);
        }
        return null;
    }

    private bool IsCircuitOpen()
    {
        lock (_stateLock) return _circuitOpen;
    }

    private async Task WriteToWalAsync(string container, string key, Stream content, string? contentType, CancellationToken ct)
    {
        var walDir = GetWalDirectory(container);
        var stagingDir = Path.Combine(walDir, "staging");
        Directory.CreateDirectory(stagingDir);

        // Stage file content
        var stagingPath = GetStagingPath(container, key);
        var stagingFileDir = Path.GetDirectoryName(stagingPath);
        if (stagingFileDir is not null)
            Directory.CreateDirectory(stagingFileDir);

        var tempPath = stagingPath + ".tmp-" + Guid.CreateVersion7().ToString("N");
        await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous))
        {
            await content.CopyToAsync(fs, ct);
            await fs.FlushAsync(ct);
        }

        if (File.Exists(stagingPath)) File.Delete(stagingPath);
        File.Move(tempPath, stagingPath);

        // Append journal entry
        AppendJournalEntry(new WalEntry
        {
            Op = WalOp.Put,
            Container = container,
            Key = key,
            ContentType = contentType,
            StagingFile = GetStagingRelativePath(key),
            Timestamp = DateTimeOffset.UtcNow
        });

        EnforceWalSizeCap(walDir);

        _logger.LogDebug("ResilientStorageDecorator: WAL write staged for {Container}/{Key}", container, key);
    }

    private void AppendJournalEntry(WalEntry entry)
    {
        var walDir = GetWalDirectory(entry.Container);
        Directory.CreateDirectory(walDir);
        var journalPath = Path.Combine(walDir, "journal.jsonl");

        var line = JsonConvert.SerializeObject(entry, WalSerializerSettings.Instance);

        lock (_walLock)
        {
            File.AppendAllText(journalPath, line + Environment.NewLine);
        }
    }

    private async Task ReplayWalAsync(CancellationToken ct)
    {
        lock (_stateLock)
        {
            if (_replaying) return;
            _replaying = true;
        }

        try
        {
            // Find all WAL directories
            if (!Directory.Exists(_walBasePath)) return;

            var walDirs = Directory.GetDirectories(_walBasePath);
            foreach (var walDir in walDirs)
            {
                await ReplayWalDirectoryAsync(walDir, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ResilientStorageDecorator: WAL replay failed");
        }
        finally
        {
            lock (_stateLock) _replaying = false;
        }
    }

    private async Task ReplayWalDirectoryAsync(string walDir, CancellationToken ct)
    {
        var journalPath = Path.Combine(walDir, "journal.jsonl");
        if (!File.Exists(journalPath)) return;

        string[] lines;
        lock (_walLock)
        {
            lines = File.ReadAllLines(journalPath);
        }

        if (lines.Length == 0) return;

        _logger.LogInformation("ResilientStorageDecorator: replaying {Count} WAL entries from {Path}", lines.Length, walDir);

        var replayedCount = 0;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (IsCircuitOpen()) break; // circuit reopened during replay

            try
            {
                var entry = JsonConvert.DeserializeObject<WalEntry>(line, WalSerializerSettings.Instance);
                if (entry is null) continue;

                switch (entry.Op)
                {
                    case WalOp.Put:
                        await ReplayPutAsync(walDir, entry, ct);
                        break;
                    case WalOp.Delete:
                        await _inner.DeleteAsync(entry.Container, entry.Key, ct);
                        break;
                }
                replayedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ResilientStorageDecorator: WAL replay entry failed: {Line}", line);
                // On replay failure, open circuit again and stop
                SetCircuitOpen();
                return;
            }
        }

        // All entries replayed successfully — clean up
        _logger.LogInformation("ResilientStorageDecorator: replayed {Count}/{Total} WAL entries", replayedCount, lines.Length);

        lock (_walLock)
        {
            try
            {
                File.Delete(journalPath);
                var stagingDir = Path.Combine(walDir, "staging");
                if (Directory.Exists(stagingDir))
                    Directory.Delete(stagingDir, recursive: true);

                // Remove empty WAL directory
                if (Directory.GetFileSystemEntries(walDir).Length == 0)
                    Directory.Delete(walDir);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ResilientStorageDecorator: WAL cleanup failed for {Path}", walDir);
            }
        }
    }

    private async Task ReplayPutAsync(string walDir, WalEntry entry, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entry.StagingFile))
            return;

        var stagingPath = Path.Combine(walDir, "staging", entry.StagingFile);
        if (!File.Exists(stagingPath))
        {
            _logger.LogWarning("ResilientStorageDecorator: WAL staging file missing: {Path}", stagingPath);
            return;
        }

        await using var fs = new FileStream(stagingPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous);
        await _inner.WriteAsync(entry.Container, entry.Key, fs, entry.ContentType, ct);
    }

    private string GetWalDirectory(string container)
    {
        var safeName = $"{_inner.Name}-{container}".Replace(':', '-').Replace('/', '-').Replace('\\', '-');
        return Path.Combine(_walBasePath, safeName);
    }

    private string GetStagingPath(string container, string key)
    {
        var walDir = GetWalDirectory(container);
        return Path.Combine(walDir, "staging", GetStagingRelativePath(key));
    }

    private static string GetStagingRelativePath(string key)
    {
        // Sanitize key for file system usage
        return key.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }

    private void EnforceWalSizeCap(string walDir)
    {
        try
        {
            var stagingDir = Path.Combine(walDir, "staging");
            if (!Directory.Exists(stagingDir)) return;

            var files = new DirectoryInfo(stagingDir)
                .GetFiles("*", SearchOption.AllDirectories)
                .OrderBy(f => f.CreationTimeUtc)
                .ToList();

            long totalSize = files.Sum(f => f.Length);

            while (totalSize > _maxWalSizeBytes && files.Count > 0)
            {
                var oldest = files[0];
                totalSize -= oldest.Length;
                oldest.Delete();
                files.RemoveAt(0);
                _logger.LogDebug("ResilientStorageDecorator: evicted oldest WAL staging file: {Path}", oldest.FullName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ResilientStorageDecorator: WAL size cap enforcement failed");
        }
    }

    private static bool IsTransportFailure(Exception ex)
    {
        // HttpRequestException covers network failures, 503, timeouts
        if (ex is HttpRequestException) return true;
        if (ex is TaskCanceledException { InnerException: TimeoutException }) return true;

        // MinIO SDK wraps errors; check inner exceptions
        if (ex.InnerException is HttpRequestException) return true;
        if (ex.InnerException is TaskCanceledException { InnerException: TimeoutException }) return true;

        // Check for specific error messages that indicate unavailability
        var msg = ex.Message;
        if (msg.Contains("503", StringComparison.OrdinalIgnoreCase)) return true;
        if (msg.Contains("Service Unavailable", StringComparison.OrdinalIgnoreCase)) return true;
        if (msg.Contains("connection refused", StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    private static async Task CopyRangeAsync(Stream from, Stream to, long bytesToCopy, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        long remaining = bytesToCopy;
        int read;
        while (remaining > 0 && (read = await from.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct)) > 0)
        {
            await to.WriteAsync(buffer.AsMemory(0, read), ct);
            remaining -= read;
        }
    }

    public void Dispose()
    {
        if (_inner is IDisposable disposable)
            disposable.Dispose();
    }
}

internal enum WalOp
{
    Put = 0,
    Delete = 1
}

internal sealed class WalEntry
{
    public WalOp Op { get; set; }
    public string Container { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public string? StagingFile { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}

internal static class WalSerializerSettings
{
    public static readonly JsonSerializerSettings Instance = new()
    {
        Formatting = Formatting.None,
        NullValueHandling = NullValueHandling.Ignore,
        Converters = { new StringEnumConverter() }
    };
}
