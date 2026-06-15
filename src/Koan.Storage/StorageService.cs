using Koan.Storage.Abstractions;

namespace Koan.Storage;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Storage.Options;
using Koan.Storage.Replication;
using System.Collections.Concurrent;
using System.Security.Cryptography;

public sealed class StorageService : IStorageService
{
    private readonly ILogger<StorageService> _logger;
    private readonly IReadOnlyDictionary<string, IStorageProvider> _providers;
    private readonly IOptionsMonitor<StorageOptions> _options;
    private readonly ConcurrentDictionary<string, ReplicatedStorageProvider> _replicatedProviders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Well-known provider name patterns considered "local".</summary>
    private static readonly string[] LocalProviderNames = ["local", "filesystem", "disk"];

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

    public async Task<StorageObject> Put(string profile, string container, string key, Stream content, string? contentType, CancellationToken ct = default)
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
        if (wasSeekable && size == 0 && provider is IStatOperations statOps)
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
        var (provider, resolvedContainer) = Resolve(profile, container);
        return provider.OpenRead(resolvedContainer, key, ct);
    }

    public Task<(Stream Stream, long? Length)> ReadRange(string profile, string container, string key, long? from, long? to, CancellationToken ct = default)
    {
        var (provider, resolvedContainer) = Resolve(profile, container);
        return provider.OpenReadRange(resolvedContainer, key, from, to, ct);
    }

    public Task<bool> Delete(string profile, string container, string key, CancellationToken ct = default)
    {
        var (provider, resolvedContainer) = Resolve(profile, container);
        return provider.Delete(resolvedContainer, key, ct);
    }

    public async Task<bool> Exists(string profile, string container, string key, CancellationToken ct = default)
    {
        var (provider, resolvedContainer) = Resolve(profile, container);
        return await provider.Exists(resolvedContainer, key, ct);
    }

    public async Task<ObjectStat?> Head(string profile, string container, string key, CancellationToken ct = default)
    {
        var (provider, resolvedContainer) = Resolve(profile, container);
        if (provider is IStatOperations stat)
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
        var (src, srcContainer) = Resolve(sourceProfile, sourceContainer);
        var (dst, dstContainer) = Resolve(targetProfile, targetContainer ?? "");

        // Attempt server-side copy when possible
        if (ReferenceEquals(src, dst) && (src is IServerSideCopy ssc))
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
        var (provider, resolvedContainer) = Resolve(profile, container);
        if (provider is IPresignOperations presign)
            return presign.PresignRead(resolvedContainer, key, expiry, ct);
        throw new NotSupportedException("Provider does not support presigned reads.");
    }

    public Task<Uri> PresignWrite(string profile, string container, string key, TimeSpan expiry, string? contentType = null, CancellationToken ct = default)
    {
        var (provider, resolvedContainer) = Resolve(profile, container);
        if (provider is IPresignOperations presign)
            return presign.PresignWrite(resolvedContainer, key, expiry, contentType, ct);
        throw new NotSupportedException("Provider does not support presigned writes.");
    }

    public IAsyncEnumerable<StorageObjectInfo> ListObjects(string profile, string container, string? prefix = null, CancellationToken ct = default)
    {
        var (provider, resolvedContainer) = Resolve(profile, container);
        if (provider is IListOperations listOps)
            return listOps.ListObjects(resolvedContainer, prefix, ct);
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

            var explicitProvider = ResolveProvider(profile, explicitProfile);
            var explicitContainer = string.IsNullOrWhiteSpace(container) ? explicitProfile.Container : container;
            return (explicitProvider, explicitContainer);
        }

        // 2) No profile provided: try DefaultProfile
        if (!string.IsNullOrWhiteSpace(opts.DefaultProfile))
        {
            if (!opts.Profiles.TryGetValue(opts.DefaultProfile, out var defaultProf))
                throw new InvalidOperationException($"Configured DefaultProfile '{opts.DefaultProfile}' not found in Profiles.");

            var defProvider = ResolveProvider(opts.DefaultProfile, defaultProf);
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
                var prov = ResolveProvider(kv.Key, prof);
                var cont = string.IsNullOrWhiteSpace(container) ? prof.Container : container;
                _logger.LogWarning("Storage profile not specified; using the only configured profile '{ProfileName}' (provider: {Provider})", kv.Key, prof.Provider);
                return (prov, cont);
            }

            throw new InvalidOperationException("No storage profile specified and no DefaultProfile configured. Configure 'Koan:Storage:DefaultProfile' or specify a profile explicitly.");
        }

        // 4) Disabled or NamedDefault without DefaultProfile set -> fail fast
        throw new InvalidOperationException("No storage profile specified and fallback is disabled. Set DefaultProfile or pass a profile name.");
    }

    /// <summary>
    /// Resolves the effective <see cref="IStorageProvider"/> for a profile, handling:
    /// - Explicit provider name → direct lookup
    /// - Absent provider + Mode → auto-detect from registered providers
    /// - Replicated mode → compose ReplicatedStorageProvider
    /// </summary>
    private IStorageProvider ResolveProvider(string profileName, StorageOptions.StorageProfile profile)
    {
        // Determine effective mode
        var mode = profile.Mode;

        // If provider is explicit, use legacy path (with replicated wrapping)
        if (!string.IsNullOrWhiteSpace(profile.Provider))
        {
            if (!_providers.TryGetValue(profile.Provider, out var named))
                throw new InvalidOperationException($"Storage profile '{profileName}' references unknown provider '{profile.Provider}'.");

            // Explicit Mode=Replicated with explicit provider: compose with auto-detected counterpart
            if (mode == StorageMode.Replicated)
            {
                return ComposeReplicated(profileName, profile, named);
            }

            return named;
        }

        // No explicit provider — auto-detect from registered providers
        var (localProvider, remoteProvider) = DetectProviders();

        return mode switch
        {
            StorageMode.Local => localProvider
                ?? throw new InvalidOperationException($"Storage profile '{profileName}' requires Mode=Local but no local provider is registered."),

            StorageMode.Remote => remoteProvider
                ?? throw new InvalidOperationException($"Storage profile '{profileName}' requires Mode=Remote but no remote provider is registered."),

            StorageMode.Replicated => ComposeReplicatedFromDetected(profileName, profile, localProvider, remoteProvider),

            // null = auto-detect
            null => AutoDetectProvider(profileName, profile, localProvider, remoteProvider),

            _ => throw new InvalidOperationException($"Storage profile '{profileName}' has unsupported mode '{mode}'.")
        };
    }

    /// <summary>
    /// Auto-detect the best provider arrangement from registered providers.
    /// </summary>
    private IStorageProvider AutoDetectProvider(
        string profileName,
        StorageOptions.StorageProfile profile,
        IStorageProvider? localProvider,
        IStorageProvider? remoteProvider)
    {
        // Both available → replicated
        if (localProvider is not null && remoteProvider is not null)
        {
            _logger.LogInformation(
                "Storage profile '{Profile}': auto-detected replicated mode (cache={Cache}, durable={Durable})",
                profileName, localProvider.Name, remoteProvider.Name);
            return ComposeReplicatedFromDetected(profileName, profile, localProvider, remoteProvider);
        }

        // Only local
        if (localProvider is not null)
        {
            _logger.LogDebug("Storage profile '{Profile}': auto-detected local mode ({Provider})", profileName, localProvider.Name);
            return localProvider;
        }

        // Only remote
        if (remoteProvider is not null)
        {
            _logger.LogDebug("Storage profile '{Profile}': auto-detected remote mode ({Provider})", profileName, remoteProvider.Name);
            return remoteProvider;
        }

        throw new InvalidOperationException(
            $"Storage profile '{profileName}' has no Provider configured and no providers are registered for auto-detection.");
    }

    /// <summary>
    /// Compose a <see cref="ReplicatedStorageProvider"/> when an explicit provider is set
    /// but Mode=Replicated. The explicit provider is used as durable; local is auto-detected.
    /// </summary>
    private IStorageProvider ComposeReplicated(
        string profileName,
        StorageOptions.StorageProfile profile,
        IStorageProvider explicitProvider)
    {
        var (localProvider, _) = DetectProviders();

        if (localProvider is null)
        {
            _logger.LogWarning(
                "Storage profile '{Profile}' is Mode=Replicated but no local provider found. Falling back to explicit provider only.",
                profileName);
            return explicitProvider;
        }

        // If the explicit provider IS the local one, look for a remote counterpart
        if (IsLocalProvider(explicitProvider))
        {
            var (_, remoteProvider) = DetectProviders();
            if (remoteProvider is null)
            {
                _logger.LogWarning(
                    "Storage profile '{Profile}' is Mode=Replicated but only a local provider is available. Using local only.",
                    profileName);
                return explicitProvider;
            }
            return GetOrCreateReplicated(profileName, profile, explicitProvider, remoteProvider);
        }

        return GetOrCreateReplicated(profileName, profile, localProvider, explicitProvider);
    }

    private IStorageProvider ComposeReplicatedFromDetected(
        string profileName,
        StorageOptions.StorageProfile profile,
        IStorageProvider? localProvider,
        IStorageProvider? remoteProvider)
    {
        if (localProvider is not null && remoteProvider is not null)
            return GetOrCreateReplicated(profileName, profile, localProvider, remoteProvider);

        if (localProvider is not null)
        {
            _logger.LogWarning("Storage profile '{Profile}': replicated mode requested but no remote provider available. Using local only.", profileName);
            return localProvider;
        }

        if (remoteProvider is not null)
        {
            _logger.LogWarning("Storage profile '{Profile}': replicated mode requested but no local provider available. Using remote only.", profileName);
            return remoteProvider;
        }

        throw new InvalidOperationException($"Storage profile '{profileName}' requires replicated mode but no providers are registered.");
    }

    private ReplicatedStorageProvider GetOrCreateReplicated(
        string profileName,
        StorageOptions.StorageProfile profile,
        IStorageProvider cache,
        IStorageProvider durable)
    {
        return _replicatedProviders.GetOrAdd(profileName, _ =>
        {
            _logger.LogInformation(
                "Storage profile '{Profile}': composing replicated provider (cache={Cache}, durable={Durable})",
                profileName, cache.Name, durable.Name);

            return new ReplicatedStorageProvider(
                cache: cache,
                durable: durable,
                container: profile.Container,
                cacheOptions: profile.LocalCache,
                logger: _logger);
        });
    }

    /// <summary>
    /// Detects a local and a remote provider from the registered set.
    /// </summary>
    private (IStorageProvider? Local, IStorageProvider? Remote) DetectProviders()
    {
        IStorageProvider? local = null;
        IStorageProvider? remote = null;

        foreach (var provider in _providers.Values)
        {
            if (IsLocalProvider(provider))
            {
                local ??= provider;
            }
            else
            {
                remote ??= provider;
            }
        }

        return (local, remote);
    }

    /// <summary>
    /// Heuristic: a provider is "local" if its name matches well-known local patterns.
    /// </summary>
    private static bool IsLocalProvider(IStorageProvider provider)
    {
        var name = provider.Name;
        foreach (var pattern in LocalProviderNames)
        {
            if (name.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private void ValidateConfiguration(StorageOptions opts)
    {
        // At least one profile must exist
        if (opts.Profiles is null || opts.Profiles.Count == 0)
        {
            throw new InvalidOperationException("Koan.Storage: No storage Profiles configured. Configure Koan:Storage:Profiles and at least one entry.");
        }

        // Each profile must reference a registered provider (or be null for auto-detect)
        foreach (var (name, prof) in opts.Profiles)
        {
            if (string.IsNullOrWhiteSpace(prof.Container))
                throw new InvalidOperationException($"Koan.Storage: Profile '{name}' has no Container configured.");

            // Provider is now nullable — skip provider validation when absent (auto-detect)
            if (!string.IsNullOrWhiteSpace(prof.Provider) && !_providers.ContainsKey(prof.Provider))
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
