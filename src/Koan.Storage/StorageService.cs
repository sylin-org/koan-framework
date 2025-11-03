using Koan.Storage.Abstractions;

namespace Koan.Storage;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Storage.Options;
using System.Security.Cryptography;

public sealed class StorageService : IStorageService
{
    private readonly ILogger<StorageService> _logger;
    private readonly IReadOnlyDictionary<string, IStorageProvider> _providers;
    private readonly IOptionsMonitor<StorageOptions> _options;

    public StorageService(
        ILogger<StorageService> logger,
        IEnumerable<IStorageProvider> providers,
        IOptionsMonitor<StorageOptions> options)
    {
        _logger = logger;
        _providers = providers.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        _options = options;

        // Optional startup validation: fail-fast on obvious misconfigurations
        var opts = _options.CurrentValue;
        if (opts.ValidateOnStart)
        {
            ValidateConfiguration(opts);
        }
    }

    public async Task<StorageObject> PutAsync(string profile, string container, string key, Stream content, string? contentType, CancellationToken ct = default)
    {
        var (provider, resolvedContainer) = Resolve(profile, container);

        // Compute hash and size while preserving original stream semantics.
        // - If seekable: hash by reading to end and then reset position; write original stream to provider.
        // - If non-seekable: buffer to memory while hashing, then write the buffer to provider.
        string? hashHex = null;
        long size = 0;
        bool wasSeekable = content.CanSeek;

        if (content.CanSeek)
        {
            long originalPos = 0;
            try { originalPos = content.Position; } catch { originalPos = 0; }

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
                    sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
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

            // Determine size if available
            try { size = content.Length - originalPos; } catch { size = 0; }

            await provider.WriteAsync(resolvedContainer, key, content, contentType, ct);
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
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
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
            await provider.WriteAsync(resolvedContainer, key, ms, contentType, ct);
        }

        // If seekable and size could not be determined, attempt best-effort stat.
        // For non-seekable uploads we intentionally leave size as 0.
        if (wasSeekable && size == 0 && provider is IStatOperations statOps)
        {
            var stat = await statOps.HeadAsync(resolvedContainer, key, ct);
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

    public Task<Stream> ReadAsync(string profile, string container, string key, CancellationToken ct = default)
    {
        var (provider, resolvedContainer) = Resolve(profile, container);
        return provider.OpenReadAsync(resolvedContainer, key, ct);
    }

    public Task<(Stream Stream, long? Length)> ReadRangeAsync(string profile, string container, string key, long? from, long? to, CancellationToken ct = default)
    {
        var (provider, resolvedContainer) = Resolve(profile, container);
        return provider.OpenReadRangeAsync(resolvedContainer, key, from, to, ct);
    }

    public Task<bool> DeleteAsync(string profile, string container, string key, CancellationToken ct = default)
    {
        var (provider, resolvedContainer) = Resolve(profile, container);
        return provider.DeleteAsync(resolvedContainer, key, ct);
    }

    public async Task<bool> ExistsAsync(string profile, string container, string key, CancellationToken ct = default)
    {
        var (provider, resolvedContainer) = Resolve(profile, container);
        return await provider.ExistsAsync(resolvedContainer, key, ct);
    }

    public async Task<ObjectStat?> HeadAsync(string profile, string container, string key, CancellationToken ct = default)
    {
        var (provider, resolvedContainer) = Resolve(profile, container);
        if (provider is IStatOperations stat)
            return await stat.HeadAsync(resolvedContainer, key, ct);
        // Fallback: infer length via range 0-0 or full open (best-effort)
        try
        {
            using var s = await provider.OpenReadAsync(resolvedContainer, key, ct);
            long? len = null;
            try { len = s.CanSeek ? s.Length : null; } catch { }
            return new ObjectStat(len, null, null, null);
        }
        catch { return null; }
    }

    public async Task<StorageObject> TransferToProfileAsync(string sourceProfile, string sourceContainer, string key, string targetProfile, string? targetContainer = null, bool deleteSource = false, CancellationToken ct = default)
    {
        // Resolve source and target providers/containers
        var (src, srcContainer) = Resolve(sourceProfile, sourceContainer);
        var (dst, dstContainer) = Resolve(targetProfile, targetContainer ?? string.Empty);

        // Attempt server-side copy when possible
        if (ReferenceEquals(src, dst) && (src is IServerSideCopy ssc))
        {
            var ok = await ssc.CopyAsync(srcContainer, key, dstContainer, key, ct);
            if (ok)
            {
                if (deleteSource && !(srcContainer == dstContainer))
                    await src.DeleteAsync(srcContainer, key, ct);
                // Compose StorageObject with best-effort stat
                var stat = await HeadAsync(targetProfile, dstContainer, key, ct);
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
        await using var read = await src.OpenReadAsync(srcContainer, key, ct);
        var obj = await PutAsync(targetProfile, dstContainer, key, read, null, ct);
        if (deleteSource)
            await src.DeleteAsync(srcContainer, key, ct);
        return obj;
    }

    public Task<Uri> PresignReadAsync(string profile, string container, string key, TimeSpan expiry, CancellationToken ct = default)
    {
        var (provider, resolvedContainer) = Resolve(profile, container);
        if (provider is IPresignOperations presign)
            return presign.PresignReadAsync(resolvedContainer, key, expiry, ct);
        throw new NotSupportedException("Provider does not support presigned reads.");
    }

    public Task<Uri> PresignWriteAsync(string profile, string container, string key, TimeSpan expiry, string? contentType = null, CancellationToken ct = default)
    {
        var (provider, resolvedContainer) = Resolve(profile, container);
        if (provider is IPresignOperations presign)
            return presign.PresignWriteAsync(resolvedContainer, key, expiry, contentType, ct);
        throw new NotSupportedException("Provider does not support presigned writes.");
    }

    public IAsyncEnumerable<StorageObjectInfo> ListObjectsAsync(string profile, string container, string? prefix = null, CancellationToken ct = default)
    {
        var (provider, resolvedContainer) = Resolve(profile, container);
        if (provider is IListOperations listOps)
            return listOps.ListObjectsAsync(resolvedContainer, prefix, ct);
        throw new NotSupportedException("Provider does not support object listing.");
    }

    private (IStorageProvider Provider, string Container) Resolve(string profile, string container)
    {
        var opts = _options.CurrentValue;

        // 1) If profile explicitly provided, resolve or fail clearly
        if (!string.IsNullOrWhiteSpace(profile))
        {
            if (!opts.Profiles.TryGetValue(profile, out var explicitProfile))
                throw new InvalidOperationException($"Unknown storage profile '{profile}'.");

            var explicitProvider = _providers[explicitProfile.Provider];
            var explicitContainer = string.IsNullOrWhiteSpace(container) ? explicitProfile.Container : container;
            return (explicitProvider, explicitContainer);
        }

        // 2) No profile provided: try DefaultProfile
        if (!string.IsNullOrWhiteSpace(opts.DefaultProfile))
        {
            if (!opts.Profiles.TryGetValue(opts.DefaultProfile, out var defaultProf))
                throw new InvalidOperationException($"Configured DefaultProfile '{opts.DefaultProfile}' not found in Profiles.");

            var defProvider = _providers[defaultProf.Provider];
            var defContainer = string.IsNullOrWhiteSpace(container) ? defaultProf.Container : container;
            return (defProvider, defContainer);
        }

        // 3) Fallback mode: SingleProfileOnly -> if exactly one profile defined, use it
        if (opts.FallbackMode == StorageFallbackMode.SingleProfileOnly)
        {
            if (opts.Profiles.Count == 1)
            {
                var kv = opts.Profiles.First();
                var prof = kv.Value;
                var prov = _providers[prof.Provider];
                var cont = string.IsNullOrWhiteSpace(container) ? prof.Container : container;
                _logger.LogWarning("Storage profile not specified; using the only configured profile '{ProfileName}' (provider: {Provider})", kv.Key, prof.Provider);
                return (prov, cont);
            }

            throw new InvalidOperationException("No storage profile specified and no DefaultProfile configured. Configure 'Koan:Storage:DefaultProfile' or specify a profile explicitly.");
        }

        // 4) Disabled or NamedDefault without DefaultProfile set -> fail fast
        throw new InvalidOperationException("No storage profile specified and fallback is disabled. Set DefaultProfile or pass a profile name.");
    }

    private void ValidateConfiguration(StorageOptions opts)
    {
        // At least one profile must exist
        if (opts.Profiles is null || opts.Profiles.Count == 0)
        {
            throw new InvalidOperationException("Koan.Storage: No storage Profiles configured. Configure Koan:Storage:Profiles and at least one entry.");
        }

        // Each profile must reference a registered provider
        foreach (var (name, prof) in opts.Profiles)
        {
            if (string.IsNullOrWhiteSpace(prof.Provider))
                throw new InvalidOperationException($"Koan.Storage: Profile '{name}' has no Provider configured.");
            if (string.IsNullOrWhiteSpace(prof.Container))
                throw new InvalidOperationException($"Koan.Storage: Profile '{name}' has no Container configured.");
            if (!_providers.ContainsKey(prof.Provider))
                throw new InvalidOperationException($"Koan.Storage: Profile '{name}' references unknown provider '{prof.Provider}'. Ensure the provider is registered.");
        }

        // If DefaultProfile is set, it must exist
        if (!string.IsNullOrWhiteSpace(opts.DefaultProfile) && !opts.Profiles.ContainsKey(opts.DefaultProfile))
        {
            throw new InvalidOperationException($"Koan.Storage: DefaultProfile '{opts.DefaultProfile}' not found in Profiles.");
        }

        // If no DefaultProfile provided and fallback is SingleProfileOnly, enforce exactly one profile
        if (string.IsNullOrWhiteSpace(opts.DefaultProfile) && opts.FallbackMode == StorageFallbackMode.SingleProfileOnly)
        {
            if (opts.Profiles.Count == 1)
            {
                var only = opts.Profiles.First();
                _logger.LogInformation("Koan.Storage: ValidateOnStart enabled; only one profile configured ('{Profile}'). Implicit fallback will apply when profile isn't specified.", only.Key);
            }
            else
            {
                throw new InvalidOperationException("Koan.Storage: Multiple storage profiles configured but no DefaultProfile set. Either set 'Koan:Storage:DefaultProfile' or disable implicit fallback by passing a profile explicitly.");
            }
        }
    }
}
