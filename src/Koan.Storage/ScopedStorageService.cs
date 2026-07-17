using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Koan.Storage.Abstractions;
using Koan.Storage.Identity;

namespace Koan.Storage;

/// <summary>
/// Storage's physical-identity chokepoint. Every operation binds the shared hard-segmentation plan once and
/// realizes its dimensions as safe leading path particles. Typed surfaces carry their subject through
/// <see cref="Keys.StorageScope"/>; raw storage operations isolate by default; explicit host operations remain
/// available for infrastructure workflows. With no active dimensions this is a byte-identical pass-through.
/// </summary>
internal sealed class ScopedStorageService(IStorageService inner, StorageIdentityPlan identity) : IStorageService
{
    private readonly IStorageService _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly StorageIdentityPlan _identity = identity ?? throw new ArgumentNullException(nameof(identity));

    public async Task<StorageObject> Put(string profile, string container, string key, Stream content, string? contentType, CancellationToken ct = default)
    {
        var identity = _identity.Bind(key, "Storage put");
        return identity.Project(await _inner.Put(profile, container, identity.PhysicalKey, content, contentType, ct));
    }

    public Task<Stream> Read(string profile, string container, string key, CancellationToken ct = default)
        => _inner.Read(profile, container, _identity.Bind(key, "Storage read").PhysicalKey, ct);

    public Task<(Stream Stream, long? Length)> ReadRange(string profile, string container, string key, long? from, long? to, CancellationToken ct = default)
        => _inner.ReadRange(profile, container, _identity.Bind(key, "Storage range read").PhysicalKey, from, to, ct);

    public Task<bool> Delete(string profile, string container, string key, CancellationToken ct = default)
        => _inner.Delete(profile, container, _identity.Bind(key, "Storage delete").PhysicalKey, ct);

    public Task<bool> Exists(string profile, string container, string key, CancellationToken ct = default)
        => _inner.Exists(profile, container, _identity.Bind(key, "Storage exists").PhysicalKey, ct);

    public Task<ObjectStat?> Head(string profile, string container, string key, CancellationToken ct = default)
        => _inner.Head(profile, container, _identity.Bind(key, "Storage head").PhysicalKey, ct);

    // A tiering copy (same scope, different profile) composes the key ONCE. Cross-tenant transfer is forbidden by
    // the guard and not expressible (one key) — see STOR-0011 (the transfer signature limitation).
    public async Task<StorageObject> TransferToProfile(string sourceProfile, string sourceContainer, string key, string targetProfile, string? targetContainer = null, bool deleteSource = false, CancellationToken ct = default)
    {
        var identity = _identity.Bind(key, "Storage transfer");
        return identity.Project(await _inner.TransferToProfile(
            sourceProfile,
            sourceContainer,
            identity.PhysicalKey,
            targetProfile,
            targetContainer,
            deleteSource,
            ct));
    }

    public Task<Uri> PresignRead(string profile, string container, string key, TimeSpan expiry, CancellationToken ct = default)
        => _inner.PresignRead(profile, container, _identity.Bind(key, "Storage presign read").PhysicalKey, expiry, ct);

    public Task<Uri> PresignWrite(string profile, string container, string key, TimeSpan expiry, string? contentType = null, CancellationToken ct = default)
        => _inner.PresignWrite(profile, container, _identity.Bind(key, "Storage presign write").PhysicalKey, expiry, contentType, ct);

    public IAsyncEnumerable<StorageObjectInfo> ListObjects(string profile, string container, string? prefix = null, CancellationToken ct = default)
    {
        // An empty logical prefix becomes the segmentation root (for example "acme/") so list-all stays bounded.
        var identity = _identity.Bind(prefix ?? "", "Storage list");
        var physical = string.IsNullOrEmpty(identity.PhysicalKey) ? null : identity.PhysicalKey;
        return ProjectList(_inner.ListObjects(profile, container, physical, ct), identity, ct);
    }

    private static async IAsyncEnumerable<StorageObjectInfo> ProjectList(
        IAsyncEnumerable<StorageObjectInfo> source,
        StorageIdentityBinding identity,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var item in source.WithCancellation(ct).ConfigureAwait(false))
            yield return item with { Key = identity.ProjectKey(item.Key) };
    }
}
