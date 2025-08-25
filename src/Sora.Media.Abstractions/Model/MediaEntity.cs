using Sora.Storage.Abstractions;

namespace Sora.Media.Abstractions.Model;

using Sora.Data.Core.Model;
using Sora.Storage;

// CRTP model for media with first-class statics, layered over StorageEntity for placement/URLs
public abstract class MediaEntity<TEntity> : Sora.Storage.Model.StorageEntity<TEntity>, IMediaObject
    where TEntity : class, IMediaObject
{
    // Media graph and semantics
    public string? SourceMediaId { get; protected internal set; }
    public string? RelationshipType { get; protected internal set; }
    public string? DerivationKey { get; protected internal set; }

    // Common convenience pointers (optional for derived types)
    public string? ThumbnailMediaId { get; protected internal set; }

    // Static DX surface (MVP: Upload/Get/Url; Ensure/RunTask come from Core via extensions)
    // Upload/onboard a stream using Storage routing (profile/container resolved from StorageBindingAttribute)
    public static async Task<TEntity> Upload(Stream content, string name, string? contentType = null, IReadOnlyDictionary<string, string>? tags = null, CancellationToken ct = default)
    {
        // Reuse StorageEntity onboarding and hydrate media metadata
        var ent = await Onboard(name, content, contentType, ct).ConfigureAwait(false);
        // Allow setting tags on the instance for downstream routing/logic; provider sync is provider-specific and may be deferred
        if (ent is MediaEntity<TEntity> me && tags is not null)
        {
            me.Tags = tags;
        }
        return ent;
    }

    // Open read stream via storage service using the bound profile/container
    // NOTE: 'new' intentionally hides StorageEntity<TEntity>.OpenRead to allow media-specific binding semantics.
    public static new async Task<Stream> OpenRead(string key, CancellationToken ct = default)
    {
        var inst = Get(key);
        // Resolve binding from the model type; prefer instance Container override when present
        var attr = typeof(TEntity)
            .GetCustomAttributes(typeof(Sora.Storage.Infrastructure.StorageBindingAttribute), inherit: false)
            .OfType<Sora.Storage.Infrastructure.StorageBindingAttribute>()
            .FirstOrDefault();
        var profile = attr?.Profile ?? string.Empty;
        var container = inst.Container ?? attr?.Container ?? string.Empty;
        var svc = (Sora.Core.SoraApp.Current?.GetService(typeof(IStorageService)) as IStorageService)
            ?? throw new InvalidOperationException("IStorageService not available");
        return await svc.ReadAsync(profile, container, key, ct).ConfigureAwait(false);
    }
}