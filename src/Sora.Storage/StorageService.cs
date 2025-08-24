namespace Sora.Storage;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sora.Storage.Options;
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

        string? hashHex = null;
        long size = 0;
        if (content.CanSeek)
        {
            var originalPos = content.Position;
            try
            {
                content.Position = 0;
                using var sha = SHA256.Create();
                var buffer = new byte[64 * 1024];
                int read;
                while ((read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                {
                    sha.TransformBlock(buffer, 0, read, null, 0);
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                hashHex = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
                size = content.Length;
            }
            finally
            {
                content.Position = originalPos;
            }
        }

        await provider.WriteAsync(resolvedContainer, key, content, contentType, ct);
        return new StorageObject
        {
            Id = $"{provider.Name}:{resolvedContainer}:{key}",
            Key = key,
            Name = key,
            ContentType = contentType,
            Size = content.CanSeek ? size : 0,
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
        return await provider.ExistsAsync(resolvedContainer, key, ct).ConfigureAwait(false);
    }

    public async Task<ObjectStat?> HeadAsync(string profile, string container, string key, CancellationToken ct = default)
    {
        var (provider, resolvedContainer) = Resolve(profile, container);
        if (provider is IStatOperations stat)
            return await stat.HeadAsync(resolvedContainer, key, ct).ConfigureAwait(false);
        // Fallback: infer length via range 0-0 or full open (best-effort)
        try
        {
            using var s = await provider.OpenReadAsync(resolvedContainer, key, ct).ConfigureAwait(false);
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
            var ok = await ssc.CopyAsync(srcContainer, key, dstContainer, key, ct).ConfigureAwait(false);
            if (ok)
            {
                if (deleteSource && !(srcContainer == dstContainer))
                    await src.DeleteAsync(srcContainer, key, ct).ConfigureAwait(false);
                // Compose StorageObject with best-effort stat
                var stat = await HeadAsync(targetProfile, dstContainer, key, ct).ConfigureAwait(false);
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
        await using var read = await src.OpenReadAsync(srcContainer, key, ct).ConfigureAwait(false);
        var obj = await PutAsync(targetProfile, dstContainer, key, read, null, ct).ConfigureAwait(false);
        if (deleteSource)
            await src.DeleteAsync(srcContainer, key, ct).ConfigureAwait(false);
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

            throw new InvalidOperationException("No storage profile specified and no DefaultProfile configured. Configure 'Sora:Storage:DefaultProfile' or specify a profile explicitly.");
        }

        // 4) Disabled or NamedDefault without DefaultProfile set -> fail fast
        throw new InvalidOperationException("No storage profile specified and fallback is disabled. Set DefaultProfile or pass a profile name.");
    }

    private void ValidateConfiguration(StorageOptions opts)
    {
        // At least one profile must exist
        if (opts.Profiles is null || opts.Profiles.Count == 0)
        {
            throw new InvalidOperationException("Sora.Storage: No storage Profiles configured. Configure Sora:Storage:Profiles and at least one entry.");
        }

        // Each profile must reference a registered provider
        foreach (var (name, prof) in opts.Profiles)
        {
            if (string.IsNullOrWhiteSpace(prof.Provider))
                throw new InvalidOperationException($"Sora.Storage: Profile '{name}' has no Provider configured.");
            if (string.IsNullOrWhiteSpace(prof.Container))
                throw new InvalidOperationException($"Sora.Storage: Profile '{name}' has no Container configured.");
            if (!_providers.ContainsKey(prof.Provider))
                throw new InvalidOperationException($"Sora.Storage: Profile '{name}' references unknown provider '{prof.Provider}'. Ensure the provider is registered.");
        }

        // If DefaultProfile is set, it must exist
        if (!string.IsNullOrWhiteSpace(opts.DefaultProfile) && !opts.Profiles.ContainsKey(opts.DefaultProfile))
        {
            throw new InvalidOperationException($"Sora.Storage: DefaultProfile '{opts.DefaultProfile}' not found in Profiles.");
        }

        // If no DefaultProfile provided and fallback is SingleProfileOnly, enforce exactly one profile
        if (string.IsNullOrWhiteSpace(opts.DefaultProfile) && opts.FallbackMode == StorageFallbackMode.SingleProfileOnly)
        {
            if (opts.Profiles.Count == 1)
            {
                var only = opts.Profiles.First();
                _logger.LogInformation("Sora.Storage: ValidateOnStart enabled; only one profile configured ('{Profile}'). Implicit fallback will apply when profile isn't specified.", only.Key);
            }
            else
            {
                throw new InvalidOperationException("Sora.Storage: Multiple storage profiles configured but no DefaultProfile set. Either set 'Sora:Storage:DefaultProfile' or disable implicit fallback by passing a profile explicitly.");
            }
        }
    }
}
