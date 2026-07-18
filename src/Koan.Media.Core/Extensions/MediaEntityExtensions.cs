using Koan.Storage.Abstractions;

namespace Koan.Media.Core.Extensions;

using Microsoft.Extensions.DependencyInjection;
using Koan.Core;
using Koan.Media;
using Koan.Media.Abstractions;
using Koan.Media.Abstractions.Model;
using Koan.Storage;

public static class MediaEntityExtensions
{
    private static IStorageService Storage()
    => Koan.Core.Hosting.App.AppHost.Current?.GetService(typeof(IStorageService)) as IStorageService
           ?? throw new InvalidOperationException("IStorageService not available");

    // Url helper - presigned when supported; falls back to a routed fetch URL (left to Web layer later)
    public static async Task<Uri> Url<TEntity>(this TEntity media, TimeSpan? ttl = null, CancellationToken ct = default)
        where TEntity : class, IMediaObject
    {
        // STOR-0011: declare the concrete type so the decorator composes this media's data-axis particle into the
        // presigned key (a presigned URL must address only the caller-scope's blob) and runs the guard.
        using var _scope = Koan.Storage.Keys.StorageScope.For(media.GetType());
        var (profile, container) = ResolveBinding<TEntity>(media.Container);
        if (ttl is { } t)
            return await Storage().PresignRead(profile, container!, media.Key, t, ct);
        // MVP: presign required; later add a web route fallback
        return await Storage().PresignRead(profile, container!, media.Key, TimeSpan.FromMinutes(15), ct);
    }

    // First-class static-like on model: TEntity.Url(id, ...)
    public static async Task<Uri> Url<TEntity>(string key, TimeSpan? ttl = null, CancellationToken ct = default)
        where TEntity : MediaEntity<TEntity>
    {
        var inst = MediaEntity<TEntity>.Get(key);
        return await inst.Url(ttl, ct);
    }

    private static (string Profile, string? Container) ResolveBinding<TEntity>(string? instanceContainer)
    {
        var t = typeof(TEntity);
        var attr = t.GetCustomAttributes(typeof(StorageBindingAttribute), inherit: false)
            .OfType<StorageBindingAttribute>().FirstOrDefault();
        var profile = attr?.Profile ?? "";
        var container = instanceContainer ?? attr?.Container ?? "";
        return (profile, container);
    }
}
