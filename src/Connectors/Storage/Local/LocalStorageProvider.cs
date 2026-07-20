using Koan.Storage.Abstractions;
using Koan.Storage.Abstractions.Capabilities;

namespace Koan.Storage.Connector.Local;

using Koan.Core;
using Koan.Core.Capabilities;
using Koan.Storage.Connector.Local.Infrastructure;
using Microsoft.Extensions.Options;
using Koan.Storage;
using System.Security.Cryptography;
using System.Text;

[ProviderPriority(0)]
public sealed class LocalStorageProvider : IStorageProvider, IStatOperations, IServerSideCopy, IListOperations
{
    private static readonly char[] PortableInvalidKeyCharacters = ['<', '>', ':', '"', '|', '?', '*'];
    private readonly string _basePath;

    public LocalStorageProvider(IOptions<LocalStorageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Value.BasePath))
        {
            throw new InvalidOperationException(
                "Local storage requires Koan:Storage:Providers:Local:BasePath. Configure a directory path.");
        }

        _basePath = Path.GetFullPath(options.Value.BasePath);
        Directory.CreateDirectory(_basePath);
    }

    public string Name => LocalStorageConstants.ProviderName;
    public StorageProviderPlacement Placement => StorageProviderPlacement.Local;

    public void Describe(ICapabilities caps)
        => caps.Add(StorageCaps.SequentialRead)
            .Add(StorageCaps.Seek)
            .Add(StorageCaps.ServerSideCopy)
            .Add(StorageCaps.Stat)
            .Add(StorageCaps.List);

    public async Task Write(string container, string key, Stream content, string? contentType, CancellationToken ct = default)
    {
        var path = GetPath(container, key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp-" + Guid.CreateVersion7().ToString("N");
        try
        {
            await using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await content.CopyToAsync(fs, ct);
                await fs.FlushAsync(ct);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (IOException) { /* Preserve the primary write failure. */ }
            catch (UnauthorizedAccessException) { /* Preserve the primary write failure. */ }
        }
    }

    public Task<Stream> OpenRead(string container, string key, CancellationToken ct = default)
    {
        var path = GetPath(container, key);
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess);
        return Task.FromResult<Stream>(fs);
    }

    public async Task<(Stream Stream, long? Length)> OpenReadRange(string container, string key, long? from, long? to, CancellationToken ct = default)
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
        await CopyRange(fs, ms, sliceLen, ct);
        ms.Position = 0;
        fs.Dispose();
        return (ms, sliceLen);
    }

    public Task<bool> Delete(string container, string key, CancellationToken ct = default)
    {
        var path = GetPath(container, key);
        if (!File.Exists(path)) return Task.FromResult(false);
        File.Delete(path);
        return Task.FromResult(true);
    }

    public Task<bool> Exists(string container, string key, CancellationToken ct = default)
    {
        var path = GetPath(container, key);
        return Task.FromResult(File.Exists(path));
    }

    public Task<ObjectStat?> Head(string container, string key, CancellationToken ct = default)
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

    public Task<bool> Copy(string sourceContainer, string sourceKey, string targetContainer, string targetKey, CancellationToken ct = default)
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
        var safeKey = SanitizeKey(key);
        var path = Path.Combine(_basePath, container ?? "", Shard(safeKey), safeKey);
        var full = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(_basePath, full);
        if (Path.IsPathRooted(relative)
            || relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
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
            // Keep the logical key language stable across hosts, then honor any additional filesystem restriction.
            if (part.IndexOfAny(PortableInvalidKeyCharacters) >= 0
                || part.Any(char.IsControl)
                || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
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

    public async IAsyncEnumerable<StorageObjectInfo> ListObjects(string container, string? prefix = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var containerPath = GetContainerPath(container);

        if (!Directory.Exists(containerPath))
            yield break;

        // Physical files live under an opaque shard directory. Enumerate below each shard, reconstruct the
        // provider key relative to that shard, then apply the logical prefix. Prefixes therefore work for both
        // flat and nested keys without exposing or depending on the sharding scheme.
        foreach (var shardPath in Directory.EnumerateDirectories(containerPath))
        {
            foreach (var filePath in Directory.EnumerateFiles(shardPath, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();

                StorageObjectInfo? objectInfo = null;
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    if (!fileInfo.Exists || fileInfo.Name.Contains(".tmp-", StringComparison.Ordinal)) continue;

                    var relativePath = Path.GetRelativePath(shardPath, filePath);
                    var storageKey = relativePath.Replace(Path.DirectorySeparatorChar, '/');
                    if (!string.IsNullOrEmpty(prefix) &&
                        !storageKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    objectInfo = new StorageObjectInfo(
                        Key: storageKey,
                        Size: fileInfo.Length,
                        LastModified: fileInfo.LastWriteTimeUtc,
                        ContentType: null,
                        ETag: $"\"{fileInfo.LastWriteTimeUtc.Ticks:X}-{fileInfo.Length:X}\"");
                }
                catch (IOException)
                {
                    // A concurrently removed or moved object is simply absent from this listing snapshot.
                    continue;
                }

                if (objectInfo is not null) yield return objectInfo;
                await Task.Yield();
            }
        }
    }

    private string GetContainerPath(string? container)
    {
        var full = Path.GetFullPath(Path.Combine(_basePath, container ?? ""));
        var relative = Path.GetRelativePath(_basePath, full);
        if (Path.IsPathRooted(relative)
            || relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Path traversal detected.");
        }

        return full;
    }

    private static async Task CopyRange(Stream from, Stream to, long bytesToCopy, CancellationToken ct)
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

