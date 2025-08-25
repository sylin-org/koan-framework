namespace Sora.Media.Core;

using Microsoft.Extensions.DependencyInjection;
using Sora.Core;
using Sora.Media.Abstractions;
using Sora.Media.Abstractions.Model;
using Sora.Storage;

public static class MediaEntityExtensions
{
    private static IStorageService Storage()
        => (SoraApp.Current?.GetService(typeof(IStorageService)) as IStorageService)
           ?? throw new InvalidOperationException("IStorageService not available");

    // Url helper — presigned when supported; falls back to a routed fetch URL (left to Web layer later)
    public static async Task<Uri> Url<TEntity>(this TEntity media, TimeSpan? ttl = null, CancellationToken ct = default)
        where TEntity : class, IMediaObject
    {
        var (profile, container) = ResolveBinding<TEntity>(media.Container);
        if (ttl is { } t)
            return await Storage().PresignReadAsync(profile, container!, media.Key, t, ct).ConfigureAwait(false);
        // MVP: presign required; later add a web route fallback
        return await Storage().PresignReadAsync(profile, container!, media.Key, TimeSpan.FromMinutes(15), ct).ConfigureAwait(false);
    }

    // First-class static-like on model: TEntity.Url(id, ...)
    public static async Task<Uri> Url<TEntity>(string key, TimeSpan? ttl = null, CancellationToken ct = default)
        where TEntity : MediaEntity<TEntity>
    {
        var inst = MediaEntity<TEntity>.Get(key);
        return await inst.Url(ttl, ct).ConfigureAwait(false);
    }

    private static (string Profile, string? Container) ResolveBinding<TEntity>(string? instanceContainer)
    {
        var t = typeof(TEntity);
        var attr = t.GetCustomAttributes(typeof(Sora.Storage.Infrastructure.StorageBindingAttribute), inherit: false)
            .OfType<Sora.Storage.Infrastructure.StorageBindingAttribute>().FirstOrDefault();
        var profile = attr?.Profile ?? string.Empty;
        var container = instanceContainer ?? attr?.Container ?? string.Empty;
        return (profile, container);
    }
}
