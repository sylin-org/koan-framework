using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Koan.Storage.Abstractions;
using Koan.Storage.Keys;

namespace Koan.Storage;

/// <summary>
/// STOR-0011 §1: the data-axis isolation chokepoint for the blob path. Decorates the real
/// <see cref="IStorageService"/> and, for EVERY op, composes the leading axis particle onto the key (the prefix
/// for <see cref="ListObjects"/>) via <see cref="StorageKeyScoper"/> and runs the registered guards — so a
/// tenant's blobs are physically isolated and an unscoped op fails closed, no matter which surface reaches the
/// blob (<c>StorageEntity</c>, <c>MediaEntity</c>, <c>IMediaSource</c>/<c>MediaController</c>, presigned URLs, the
/// extension helpers, transfer, list). Off (no axis registered) ⇒ a pure pass-through (byte-identical).
/// </summary>
internal sealed class ScopedStorageService(IStorageService inner) : IStorageService
{
    private readonly IStorageService _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public Task<StorageObject> Put(string profile, string container, string key, Stream content, string? contentType, CancellationToken ct = default)
        => _inner.Put(profile, container, StorageKeyScoper.Scope(key), content, contentType, ct);

    public Task<Stream> Read(string profile, string container, string key, CancellationToken ct = default)
        => _inner.Read(profile, container, StorageKeyScoper.Scope(key), ct);

    public Task<(Stream Stream, long? Length)> ReadRange(string profile, string container, string key, long? from, long? to, CancellationToken ct = default)
        => _inner.ReadRange(profile, container, StorageKeyScoper.Scope(key), from, to, ct);

    public Task<bool> Delete(string profile, string container, string key, CancellationToken ct = default)
        => _inner.Delete(profile, container, StorageKeyScoper.Scope(key), ct);

    public Task<bool> Exists(string profile, string container, string key, CancellationToken ct = default)
        => _inner.Exists(profile, container, StorageKeyScoper.Scope(key), ct);

    public Task<ObjectStat?> Head(string profile, string container, string key, CancellationToken ct = default)
        => _inner.Head(profile, container, StorageKeyScoper.Scope(key), ct);

    // A tiering copy (same scope, different profile) composes the key ONCE. Cross-tenant transfer is forbidden by
    // the guard and not expressible (one key) — see STOR-0011 (the transfer signature limitation).
    public Task<StorageObject> TransferToProfile(string sourceProfile, string sourceContainer, string key, string targetProfile, string? targetContainer = null, bool deleteSource = false, CancellationToken ct = default)
        => _inner.TransferToProfile(sourceProfile, sourceContainer, StorageKeyScoper.Scope(key), targetProfile, targetContainer, deleteSource, ct);

    public Task<Uri> PresignRead(string profile, string container, string key, TimeSpan expiry, CancellationToken ct = default)
        => _inner.PresignRead(profile, container, StorageKeyScoper.Scope(key), expiry, ct);

    public Task<Uri> PresignWrite(string profile, string container, string key, TimeSpan expiry, string? contentType = null, CancellationToken ct = default)
        => _inner.PresignWrite(profile, container, StorageKeyScoper.Scope(key), expiry, contentType, ct);

    public IAsyncEnumerable<StorageObjectInfo> ListObjects(string profile, string container, string? prefix = null, CancellationToken ct = default)
    {
        // Scope the prefix so a tenant lists only its own blobs. Off ⇒ Scope("") returns "" ⇒ keep null (the
        // provider's "list all" semantics, byte-identical); scoped ⇒ "acme/" (or "acme/<prefix>").
        var scoped = StorageKeyScoper.Scope(prefix ?? "");
        return _inner.ListObjects(profile, container, string.IsNullOrEmpty(scoped) ? null : scoped, ct);
    }
}
