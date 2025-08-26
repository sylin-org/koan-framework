using Sora.Storage.Abstractions;

namespace Sora.Storage.Local;

using Microsoft.Extensions.Options;
using Sora.Storage;
using System.Security.Cryptography;
using System.Text;

public sealed class LocalStorageProvider : IStorageProvider, IStatOperations, IServerSideCopy
{
    private readonly IOptionsMonitor<LocalStorageOptions> _options;

    public LocalStorageProvider(IOptionsMonitor<LocalStorageOptions> options)
    {
        _options = options;
    }

    public string Name => "local";

    public StorageProviderCapabilities Capabilities => new(true, true, false, true);

    public async Task WriteAsync(string container, string key, Stream content, string? contentType, CancellationToken ct = default)
    {
        var path = GetPath(container, key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await content.CopyToAsync(fs, ct);
        }
        if (File.Exists(path)) File.Delete(path);
        File.Move(temp, path);
    }

    public Task<Stream> OpenReadAsync(string container, string key, CancellationToken ct = default)
    {
        var path = GetPath(container, key);
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess);
        return Task.FromResult<Stream>(fs);
    }

    public async Task<(Stream Stream, long? Length)> OpenReadRangeAsync(string container, string key, long? from, long? to, CancellationToken ct = default)
    {
        var path = GetPath(container, key);
        var fi = new FileInfo(path);
        long length = fi.Length;
        long start = from ?? 0;
        long end = to.HasValue ? Math.Min(to.Value, length - 1) : length - 1;
        if (start < 0 || start > end || start >= length)
            throw new ArgumentOutOfRangeException(nameof(from));

        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess);
        fs.Seek(start, SeekOrigin.Begin);
        var sliceLen = end - start + 1;
        var ms = new MemoryStream((int)Math.Min(sliceLen, int.MaxValue));
        await CopyRangeAsync(fs, ms, sliceLen, ct);
        ms.Position = 0;
        fs.Dispose();
        return (ms, sliceLen);
    }

    public Task<bool> DeleteAsync(string container, string key, CancellationToken ct = default)
    {
        var path = GetPath(container, key);
        if (!File.Exists(path)) return Task.FromResult(false);
        File.Delete(path);
        return Task.FromResult(true);
    }

    public Task<bool> ExistsAsync(string container, string key, CancellationToken ct = default)
    {
        var path = GetPath(container, key);
        return Task.FromResult(File.Exists(path));
    }

    public Task<ObjectStat?> HeadAsync(string container, string key, CancellationToken ct = default)
    {
        var path = GetPath(container, key);
        if (!File.Exists(path)) return Task.FromResult<ObjectStat?>(null);
        var fi = new FileInfo(path);
        // Generate a lightweight, stable ETag from file LastWriteTimeUtc and Length (hex-encoded)
        // This avoids hashing content while still changing whenever the file is modified.
        var etag = $"\"{fi.LastWriteTimeUtc.Ticks:X}-{fi.Length:X}\"";
        var stat = new ObjectStat(fi.Length, null, fi.LastWriteTimeUtc, etag);
        return Task.FromResult<ObjectStat?>(stat);
    }

    public Task<bool> CopyAsync(string sourceContainer, string sourceKey, string targetContainer, string targetKey, CancellationToken ct = default)
    {
        var src = GetPath(sourceContainer, sourceKey);
        var dst = GetPath(targetContainer, targetKey);
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        if (string.Equals(src, dst, StringComparison.OrdinalIgnoreCase)) return Task.FromResult(true);
        File.Copy(src, dst, overwrite: true);
        return Task.FromResult(true);
    }

    private string GetPath(string container, string key)
    {
        var basePath = _options.CurrentValue.BasePath ?? throw new InvalidOperationException("Local storage BasePath not configured.");
        var safeKey = SanitizeKey(key);
        var path = Path.Combine(basePath, container ?? string.Empty, Shard(safeKey), safeKey);
        var full = Path.GetFullPath(path);
        var fullBase = Path.GetFullPath(basePath);
        if (!full.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Path traversal detected.");
        }
        return full;
    }

    private static string SanitizeKey(string key)
    {
        if (key is null) throw new ArgumentNullException(nameof(key));
        key = key.Replace('\\', '/');
        key = key.Trim();
        key = key.TrimStart('/');
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key cannot be empty.");

        var parts = key.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part == "." || part == "..")
                throw new InvalidOperationException("Path traversal or invalid segment detected.");
            // disallow path-breaking chars
            if (part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidOperationException("Invalid characters in key.");
        }
        return string.Join('/', parts);
    }

    private static string Shard(string key)
    {
        using var sha1 = SHA1.Create();
        var bytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(bytes.AsSpan(0, 2)).ToLowerInvariant();
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
}
