using System.Security.Cryptography;
using Koan.Storage.Abstractions;

namespace Koan.Media.Abstractions.Model;

using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Storage;

// CRTP model for media with first-class statics, layered over StorageEntity for placement/URLs
public abstract class MediaEntity<TEntity> : Koan.Storage.Model.StorageEntity<TEntity>, IMediaObject
    where TEntity : class, IMediaObject
{
    // Media graph and semantics
    public string? SourceMediaId { get; protected internal set; }
    public string? RelationshipType { get; protected internal set; }
    public string? DerivationKey { get; protected internal set; }

    // Common convenience pointers (optional for derived types)
    public string? ThumbnailMediaId { get; protected internal set; }

    // Static DX surface (Upload/Store/Get/OpenRead/Url; Ensure/RunTask come from Core via extensions).
    // Upload writes the stream under the caller-supplied name; Store derives a content-addressable
    // SHA-256 key from the bytes and dedupes against existing rows. Use Upload when the caller owns
    // the naming convention (e.g. {parentId}_thumb.jpg variants); use Store for opaque content with
    // built-in dedup.
    public static async Task<TEntity> Upload(Stream content, string name, string? contentType = null, IReadOnlyDictionary<string, string>? tags = null, CancellationToken ct = default)
    {
        // Reuse StorageEntity onboarding and hydrate media metadata
        var ent = await Onboard(name, content, contentType, ct);
        // Allow setting tags on the instance for downstream routing/logic; provider sync is provider-specific and may be deferred
        if (ent is MediaEntity<TEntity> me && tags is not null)
        {
            me.Tags = tags;
        }
        return ent;
    }

    /// <summary>
    /// Content-addressable upload with automatic dedup. Computes SHA-256 of <paramref name="bytes"/>;
    /// if an entity row already exists with that SHA as its storage key, returns it without writing.
    /// Otherwise uploads with the SHA as the storage key, persists the entity row, and returns it.
    /// Re-running with identical content is an idempotent no-op.
    /// </summary>
    /// <param name="bytes">Binary content to store.</param>
    /// <param name="name">
    /// Optional diagnostic name (e.g. an original filename). Stored on
    /// <see cref="Koan.Storage.Model.StorageEntity{TEntity}.Name"/>; never used as the storage key
    /// or in URLs. Leave null for purely opaque content-addressable storage.
    /// </param>
    /// <param name="contentType">
    /// MIME type. Stored on <see cref="Koan.Storage.Model.StorageEntity{TEntity}.ContentType"/> and
    /// served by the recipe-pipeline <c>MediaController</c> via the <c>Content-Type</c> header. When
    /// null, Koan infers from any extension in <paramref name="name"/>, falling back to
    /// <c>application/octet-stream</c>.
    /// </param>
    /// <param name="tags">Optional metadata bag (forwarded to <see cref="Upload"/>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The persisted (or pre-existing) media entity. Its
    /// <see cref="Koan.Storage.Model.StorageEntity{TEntity}.Key"/> is the 64-char lowercase
    /// SHA-256 hex of <paramref name="bytes"/>.
    /// </returns>
    /// <remarks>
    /// The storage key being the content hash also gives idempotency at the storage layer: an
    /// identical-content second call lands on the same on-disk file. Dedup at the entity layer
    /// (this method's preflight <c>Query</c>) prevents redundant row creation; storage-layer
    /// dedup prevents redundant byte writes when the entity-layer check is bypassed.
    /// </remarks>
    public static async Task<TEntity> Store(byte[] bytes, string? name = null, string? contentType = null, IReadOnlyDictionary<string, string>? tags = null, CancellationToken ct = default)
    {
        if (bytes is null) throw new ArgumentNullException(nameof(bytes));

        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        // Dedup: same content already stored?
        var existing = (await Query(e => e.Key == sha, ct)).FirstOrDefault();
        if (existing is not null) return existing;

        // Fresh upload: storage key = SHA-256; on-disk path becomes deterministic across runs.
        using var stream = new MemoryStream(bytes);
        var entity = await Upload(stream, sha, contentType, tags, ct);
        if (!string.IsNullOrEmpty(name) && entity is MediaEntity<TEntity> me)
        {
            me.Name = name;
        }
        await entity.Save(ct);
        return entity;
    }

    /// <summary>
    /// Stream overload of <see cref="Store(byte[], string?, string?, System.Collections.Generic.IReadOnlyDictionary{string, string}?, CancellationToken)"/>.
    /// Buffers the stream into memory to compute the SHA-256 digest; for very large payloads
    /// prefer reading the bytes once and calling the <c>byte[]</c> overload to avoid double buffering.
    /// </summary>
    public static async Task<TEntity> Store(Stream content, string? name = null, string? contentType = null, IReadOnlyDictionary<string, string>? tags = null, CancellationToken ct = default)
    {
        if (content is null) throw new ArgumentNullException(nameof(content));

        byte[] bytes;
        if (content is MemoryStream ms && ms.CanSeek)
        {
            bytes = ms.ToArray();
        }
        else
        {
            using var buf = new MemoryStream();
            await content.CopyToAsync(buf, ct);
            bytes = buf.ToArray();
        }
        return await Store(bytes, name, contentType, tags, ct);
    }

    // Open read stream via storage service using the bound profile/container
    // NOTE: 'new' intentionally hides StorageEntity<TEntity>.OpenRead to allow media-specific binding semantics.
    public static new async Task<Stream> OpenRead(string key, CancellationToken ct = default)
    {
        // STOR-0011: declare the entity type so the ScopedStorageService decorator applies this type's data-axis
        // isolation (the leading particle + guard) to this otherwise type-erased svc.Read — the override would
        // otherwise bypass the chokepoint (a CRITICAL cross-tenant leak on the media read surface).
        using var _scope = Koan.Storage.Keys.StorageScope.For(typeof(TEntity));
        var inst = Get(key);
        // Resolve binding from the model type; prefer instance Container override when present
        var attr = typeof(TEntity)
            .GetCustomAttributes(typeof(Koan.Storage.Infrastructure.StorageBindingAttribute), inherit: false)
            .OfType<Koan.Storage.Infrastructure.StorageBindingAttribute>()
            .FirstOrDefault();
        var profile = attr?.Profile ?? "";
        var container = inst.Container ?? attr?.Container ?? "";
        var svc = (Koan.Core.Hosting.App.AppHost.Current?.GetService(typeof(IStorageService)) as IStorageService)
                ?? throw new InvalidOperationException("IStorageService not available");
        return await svc.Read(profile, container, key, ct);
    }
}