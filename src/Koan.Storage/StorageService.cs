using Koan.Storage.Abstractions;
using Koan.Storage.Abstractions.Capabilities;
using Koan.Storage.Routing;

namespace Koan.Storage;

using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

internal sealed class StorageService : IStorageService
{
    private readonly ILogger<StorageService> _logger;
    private readonly StorageRoutingPlan _routing;

    public StorageService(
        ILogger<StorageService> logger,
        StorageRoutingPlan routing)
    {
        _logger = logger;
        _routing = routing;
    }

    public async Task<StorageObject> Put(string profile, string container, string key, Stream content, string? contentType, CancellationToken ct = default)
    {
        var (provider, capabilities, resolvedContainer) = Resolve(profile, container);

        // Compute hash and size while preserving original stream semantics.
        // - If seekable: hash by reading to end and then reset position; write original stream to provider.
        // - If non-seekable: buffer to memory while hashing, then write the buffer to provider.
        string? hashHex = null;
        long size = 0;
        bool wasSeekable = content.CanSeek;

        if (content.CanSeek)
        {
            long originalPos = 0;
            // NotSupportedException only: a stream may expose CanSeek=true yet not back Position
            // (capability probe). Any other failure is a real I/O error and must surface.
            try { originalPos = content.Position; } catch (NotSupportedException) { originalPos = 0; }

            using (var sha = SHA256.Create())
            {
                var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(81920);
                try
                {
                    int read;
                    while ((read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                    {
                        sha.TransformBlock(buffer, 0, read, null, 0);
                    }
                    sha.TransformFinalBlock([], 0, 0);
                    hashHex = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
                }
                finally
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
                }
            }

            // Reset to original position for provider write
            if (content.CanSeek)
                content.Seek(originalPos, SeekOrigin.Begin);

            // Determine size if available. NotSupportedException only: length is a stream
            // capability that may be absent; a best-effort stat below recovers the real size.
            try { size = content.Length - originalPos; } catch (NotSupportedException) { size = 0; }

            await provider.Write(resolvedContainer, key, content, contentType, ct);
        }
        else
        {
            using var sha = SHA256.Create();
            using var ms = new MemoryStream();
            var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(81920);
            try
            {
                int read;
                while ((read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                {
                    sha.TransformBlock(buffer, 0, read, null, 0);
                    await ms.WriteAsync(buffer.AsMemory(0, read), ct);
                }
                sha.TransformFinalBlock([], 0, 0);
                hashHex = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
            }

            ms.Position = 0;
            // Intentionally keep reported size as 0 for non-seekable uploads (contract: size unknown)
            // Tests assert this behavior. We still write the full buffered content.
            size = 0;
            await provider.Write(resolvedContainer, key, ms, contentType, ct);
        }

        // If seekable and size could not be determined, attempt best-effort stat.
        // For non-seekable uploads we intentionally leave size as 0.
        if (wasSeekable && size == 0 && capabilities.Has(StorageCaps.Stat) && provider is IStatOperations statOps)
        {
            var stat = await statOps.Head(resolvedContainer, key, ct);
            if (stat?.Length is long len) size = len;
        }

        return new StorageObject
        {
            Id = $"{provider.Name}:{resolvedContainer}:{key}",
            Key = key,
            Name = key,
            ContentType = contentType,
            Size = size,
            ContentHash = hashHex,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = null,
            Provider = provider.Name,
            Container = resolvedContainer,
            Tags = null
        };
    }

    public Task<Stream> Read(string profile, string container, string key, CancellationToken ct = default)
    {
        var (provider, _, resolvedContainer) = Resolve(profile, container);
        return provider.OpenRead(resolvedContainer, key, ct);
    }

    public Task<(Stream Stream, long? Length)> ReadRange(string profile, string container, string key, long? from, long? to, CancellationToken ct = default)
    {
        var (provider, _, resolvedContainer) = Resolve(profile, container);
        return provider.OpenReadRange(resolvedContainer, key, from, to, ct);
    }

    public Task<bool> Delete(string profile, string container, string key, CancellationToken ct = default)
    {
        var (provider, _, resolvedContainer) = Resolve(profile, container);
        return provider.Delete(resolvedContainer, key, ct);
    }

    public async Task<bool> Exists(string profile, string container, string key, CancellationToken ct = default)
    {
        var (provider, _, resolvedContainer) = Resolve(profile, container);
        return await provider.Exists(resolvedContainer, key, ct);
    }

    public async Task<ObjectStat?> Head(string profile, string container, string key, CancellationToken ct = default)
    {
        var (provider, capabilities, resolvedContainer) = Resolve(profile, container);
        if (capabilities.Has(StorageCaps.Stat) && provider is IStatOperations stat)
            return await stat.Head(resolvedContainer, key, ct);
        // Fallback: infer length via range 0-0 or full open (best-effort)
        try
        {
            using var s = await provider.OpenRead(resolvedContainer, key, ct);
            long? len = null;
            // NotSupportedException only: length is an optional stream capability — leave it null.
            try { len = s.CanSeek ? s.Length : null; } catch (NotSupportedException) { }
            return new ObjectStat(len, null, null, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Best-effort stat fallback: the object could not be opened to infer length.
            // Treat as un-stat-able (return null) rather than failing the caller, but no
            // longer silently — record so a misbehaving provider is diagnosable.
            _logger.LogDebug(ex, "Storage: best-effort Head fallback could not stat '{Container}/{Key}' on provider '{Provider}'; returning null", resolvedContainer, key, provider.Name);
            return null;
        }
    }

    public async Task<StorageObject> TransferToProfile(string sourceProfile, string sourceContainer, string key, string targetProfile, string? targetContainer = null, bool deleteSource = false, CancellationToken ct = default)
    {
        // Resolve source and target providers/containers
        var (src, sourceCapabilities, srcContainer) = Resolve(sourceProfile, sourceContainer);
        var (dst, _, dstContainer) = Resolve(targetProfile, targetContainer ?? "");

        // Attempt server-side copy when possible
        if (ReferenceEquals(src, dst)
            && sourceCapabilities.Has(StorageCaps.ServerSideCopy)
            && src is IServerSideCopy ssc)
        {
            var ok = await ssc.Copy(srcContainer, key, dstContainer, key, ct);
            if (ok)
            {
                if (deleteSource && !(srcContainer == dstContainer))
                    await src.Delete(srcContainer, key, ct);
                // Compose StorageObject with best-effort stat
                var stat = await Head(targetProfile, dstContainer, key, ct);
                return new StorageObject
                {
                    Id = $"{dst.Name}:{dstContainer}:{key}",
                    Key = key,
                    Name = key,
                    ContentType = stat?.ContentType,
                    Size = stat?.Length ?? 0,
                    ContentHash = null,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = null,
                    Provider = dst.Name,
                    Container = dstContainer,
                    Tags = null
                };
            }
        }

        // Stream copy fallback
        await using var read = await src.OpenRead(srcContainer, key, ct);
        var obj = await Put(targetProfile, dstContainer, key, read, null, ct);
        if (deleteSource)
            await src.Delete(srcContainer, key, ct);
        return obj;
    }

    public Task<Uri> PresignRead(string profile, string container, string key, TimeSpan expiry, CancellationToken ct = default)
    {
        var (provider, capabilities, resolvedContainer) = Resolve(profile, container);
        capabilities.Require(StorageCaps.PresignedRead);
        return ((IPresignOperations)provider).PresignRead(resolvedContainer, key, expiry, ct);
    }

    public Task<Uri> PresignWrite(string profile, string container, string key, TimeSpan expiry, string? contentType = null, CancellationToken ct = default)
    {
        var (provider, capabilities, resolvedContainer) = Resolve(profile, container);
        capabilities.Require(StorageCaps.PresignedWrite);
        return ((IPresignOperations)provider).PresignWrite(resolvedContainer, key, expiry, contentType, ct);
    }

    public IAsyncEnumerable<StorageObjectInfo> ListObjects(string profile, string container, string? prefix = null, CancellationToken ct = default)
    {
        var (provider, capabilities, resolvedContainer) = Resolve(profile, container);
        capabilities.Require(StorageCaps.List);
        return ((IListOperations)provider).ListObjects(resolvedContainer, prefix, ct);
    }

    private (IStorageProvider Provider, Koan.Core.Capabilities.CapabilitySet Capabilities, string Container) Resolve(
        string profile,
        string container)
    {
        var resolved = _routing.Resolve(profile, container);
        return (resolved.Route.Provider, resolved.Route.Capabilities, resolved.Container);
    }
}
