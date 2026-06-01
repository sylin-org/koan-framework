using System.Collections.Concurrent;
using Koan.Media.Web.Routing;

namespace Koan.Media.Core.Tests.Support;

/// <summary>
/// Test <see cref="IMediaSource"/> that simulates the storage-backed
/// derivation surface introduced by MEDIA-0007. Sources and derivations
/// live in the same in-memory namespace, keyed by
/// <c>{sourceId}:{recipeFingerprint}</c>, and the source itself implements
/// the new <see cref="IMediaSource.OpenDerivationAsync"/>,
/// <see cref="IMediaSource.TryStoreDerivationAsync"/> and
/// <see cref="IMediaSource.SweepOrphanedDerivationsAsync"/> methods. Lets
/// the cache-as-storage specs verify the contract end-to-end without
/// pulling in Koan.Storage.
/// </summary>
public sealed class StorageBackedMediaSource : IMediaSource
{
    // Source rows: sourceId -> (bytes, contentHash, contentType).
    private readonly ConcurrentDictionary<string, SourceRow> _sources = new(StringComparer.OrdinalIgnoreCase);

    // Derivation rows: derivedKey ({sourceId}:{fingerprint}) -> entry.
    private readonly ConcurrentDictionary<string, DerivationRow> _derivations = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Number of times <see cref="OpenDerivationAsync"/> returned a non-null hit.</summary>
    public int DerivationHitCount;

    /// <summary>Number of times <see cref="TryStoreDerivationAsync"/> persisted a row.</summary>
    public int DerivationWriteCount;

    /// <summary>Optional faulting hook applied to <see cref="TryStoreDerivationAsync"/>.</summary>
    public Func<string, string, Exception?>? WriteFault { get; set; }

    public async Task<string> AddSourceAsync(string id, Stream content, string contentType = "image/jpeg", CancellationToken ct = default)
    {
        var bytes = await Fixtures.Snapshot(content, ct).ConfigureAwait(false);
        await using var ms = new MemoryStream(bytes);
        var hash = await Fixtures.Sha256Hex(ms, ct).ConfigureAwait(false);
        _sources[id] = new SourceRow(bytes, hash, contentType);
        return hash;
    }

    public void DeleteSource(string id) => _sources.TryRemove(id, out _);

    public bool ContainsSource(string id) => _sources.ContainsKey(id);

    public int DerivationCount => _derivations.Count;

    /// <summary>Enumerate stored derivations for assertion convenience.</summary>
    public IEnumerable<KeyValuePair<string, DerivationRow>> AllDerivations()
        => _derivations.ToArray();

    public Task<MediaSourceHandle?> OpenAsync(string id, CancellationToken ct = default)
    {
        if (!_sources.TryGetValue(id, out var row))
            return Task.FromResult<MediaSourceHandle?>(null);
        return Task.FromResult<MediaSourceHandle?>(new MediaSourceHandle(
            Id: id,
            Bytes: new MemoryStream(row.Bytes, writable: false),
            ContentHashHex: row.ContentHashHex,
            LastModified: null));
    }

    public Task<MediaDerivationHandle?> OpenDerivationAsync(string sourceId, string recipeFingerprint, CancellationToken ct = default)
    {
        var key = DerivedKey(sourceId, recipeFingerprint);
        if (!_derivations.TryGetValue(key, out var row))
            return Task.FromResult<MediaDerivationHandle?>(null);
        Interlocked.Increment(ref DerivationHitCount);
        return Task.FromResult<MediaDerivationHandle?>(new MediaDerivationHandle(
            Bytes: new MemoryStream(row.Bytes, writable: false),
            ContentType: row.ContentType));
    }

    public Task TryStoreDerivationAsync(
        string sourceId,
        string recipeFingerprint,
        MediaOutput output,
        string? recipeName,
        string? recipeVersion,
        CancellationToken ct = default)
    {
        if (WriteFault is not null)
        {
            var ex = WriteFault(sourceId, recipeFingerprint);
            if (ex is not null) throw ex;
        }

        var key = DerivedKey(sourceId, recipeFingerprint);
        _derivations[key] = new DerivationRow(
            Bytes: output.Bytes,
            ContentType: output.ContentType,
            SourceMediaId: sourceId,
            DerivationKey: recipeFingerprint,
            RelationshipType: recipeName ?? "derivation",
            RecipeVersion: recipeVersion ?? "1",
            CreatedAt: DateTimeOffset.UtcNow);
        Interlocked.Increment(ref DerivationWriteCount);
        return Task.CompletedTask;
    }

    public Task<MediaDerivationSweepResult> SweepOrphanedDerivationsAsync(CancellationToken ct = default)
    {
        var examined = 0;
        var deleted = 0;
        foreach (var entry in _derivations.ToArray())
        {
            ct.ThrowIfCancellationRequested();
            examined++;
            if (entry.Value.SourceMediaId is { Length: > 0 } sid && !_sources.ContainsKey(sid))
            {
                if (_derivations.TryRemove(entry.Key, out _)) deleted++;
            }
        }
        return Task.FromResult(new MediaDerivationSweepResult(examined, deleted));
    }

    public static string DerivedKey(string sourceId, string fingerprint) => $"{sourceId}:{fingerprint}";

    public sealed record SourceRow(byte[] Bytes, string ContentHashHex, string ContentType);

    public sealed record DerivationRow(
        byte[] Bytes,
        string ContentType,
        string SourceMediaId,
        string DerivationKey,
        string RelationshipType,
        string RecipeVersion,
        DateTimeOffset CreatedAt);
}
