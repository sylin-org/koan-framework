using Koan.Storage.Abstractions;

namespace Koan.Media.Core.Extensions;

using Microsoft.Extensions.DependencyInjection;
using Koan.Core;
using Koan.Media.Abstractions;
using Koan.Media.Abstractions.Model;
using Koan.Storage;

public static class MediaEntityExtensions
{
    private static IStorageService Storage()
    => Koan.Core.Hosting.App.AppHost.Current?.GetService(typeof(IStorageService)) as IStorageService
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
        var attr = t.GetCustomAttributes(typeof(Storage.Infrastructure.StorageBindingAttribute), inherit: false)
            .OfType<Storage.Infrastructure.StorageBindingAttribute>().FirstOrDefault();
        var profile = attr?.Profile ?? string.Empty;
        var container = instanceContainer ?? attr?.Container ?? string.Empty;
        return (profile, container);
    }
}
