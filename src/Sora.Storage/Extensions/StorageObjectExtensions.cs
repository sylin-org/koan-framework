using Sora.Storage.Abstractions;

namespace Sora.Storage.Extensions;

using Microsoft.Extensions.DependencyInjection;
using Sora.Core;
using System.Text;

public static class StorageObjectExtensions
{
    private static IStorageService Storage()
        => (SoraApp.Current?.GetService(typeof(IStorageService)) as IStorageService)
           ?? throw new InvalidOperationException("IStorageService not available. Ensure SoraInitialization.InitializeModules() ran and SoraApp.Current is set.");

    public static Task<string> ReadAllText(this IStorageObject obj, Encoding? encoding = null, CancellationToken ct = default)
    {
        var (profile, container) = ResolveBindingFromInstance(obj);
        return Storage().ReadAllText(profile, container, obj.Key, encoding, ct);
    }

    public static Task<byte[]> ReadAllBytes(this IStorageObject obj, CancellationToken ct = default)
    {
        var (profile, container) = ResolveBindingFromInstance(obj);
        return Storage().ReadAllBytes(profile, container, obj.Key, ct);
    }

    public static Task<ObjectStat?> Head(this IStorageObject obj, CancellationToken ct = default)
    {
        var (profile, container) = ResolveBindingFromInstance(obj);
        return Storage().HeadAsync(profile, container, obj.Key, ct);
    }

    public static Task<bool> Delete(this IStorageObject obj, CancellationToken ct = default)
    {
        var (profile, container) = ResolveBindingFromInstance(obj);
        return Storage().DeleteAsync(profile, container, obj.Key, ct);
    }

    public static async Task<TTarget> CopyTo<TTarget>(this IStorageObject obj, CancellationToken ct = default)
        where TTarget : class, IStorageObject
    {
        var targetType = typeof(TTarget);
        var binding = targetType.GetCustomAttributes(typeof(Infrastructure.StorageBindingAttribute), false)
                                 .OfType<Infrastructure.StorageBindingAttribute>()
                                 .FirstOrDefault();
        var profile = binding?.Profile ?? string.Empty;
        var container = binding?.Container ?? string.Empty;
        var result = await Storage().TransferToProfileAsync(obj.Provider ?? string.Empty, obj.Container ?? string.Empty, obj.Key, profile, container, deleteSource: false, ct).ConfigureAwait(false);

        // If TTarget derives from StorageEntity<TTarget>, hydrate metadata; else return a minimal proxy via StorageObject cast if possible
        if (Activator.CreateInstance<TTarget>() is Model.StorageEntity<TTarget> se)
        {
            se.Id = result.Id;
            se.Key = result.Key;
            se.Name = result.Name;
            se.ContentType = result.ContentType;
            se.Size = result.Size;
            se.ContentHash = result.ContentHash;
            se.CreatedAt = result.CreatedAt;
            se.UpdatedAt = result.UpdatedAt;
            se.Provider = result.Provider;
            se.Container = result.Container;
            se.Tags = result.Tags;
            return (TTarget)(object)se;
        }
        throw new InvalidOperationException($"Target type {typeof(TTarget).FullName} must derive from StorageEntity<TTarget> to receive storage metadata.");
    }

    public static async Task<TTarget> MoveTo<TTarget>(this IStorageObject obj, CancellationToken ct = default)
        where TTarget : class, IStorageObject
    {
        var targetType = typeof(TTarget);
        var binding = targetType.GetCustomAttributes(typeof(Infrastructure.StorageBindingAttribute), false)
                                 .OfType<Infrastructure.StorageBindingAttribute>()
                                 .FirstOrDefault();
        var profile = binding?.Profile ?? string.Empty;
        var container = binding?.Container ?? string.Empty;
        var result = await Storage().TransferToProfileAsync(obj.Provider ?? string.Empty, obj.Container ?? string.Empty, obj.Key, profile, container, deleteSource: true, ct).ConfigureAwait(false);

        if (Activator.CreateInstance<TTarget>() is Model.StorageEntity<TTarget> se)
        {
            se.Id = result.Id;
            se.Key = result.Key;
            se.Name = result.Name;
            se.ContentType = result.ContentType;
            se.Size = result.Size;
            se.ContentHash = result.ContentHash;
            se.CreatedAt = result.CreatedAt;
            se.UpdatedAt = result.UpdatedAt;
            se.Provider = result.Provider;
            se.Container = result.Container;
            se.Tags = result.Tags;
            return (TTarget)(object)se;
        }
        throw new InvalidOperationException($"Target type {typeof(TTarget).FullName} must derive from StorageEntity<TTarget> to receive storage metadata.");
    }
    private static (string Profile, string Container) ResolveBindingFromInstance(IStorageObject obj)
    {
        // If the runtime type has a StorageBindingAttribute, use it; otherwise fall back to obj.Provider/Container
        var t = obj.GetType();
        var attr = t.GetCustomAttributes(typeof(Infrastructure.StorageBindingAttribute), false)
                     .OfType<Infrastructure.StorageBindingAttribute>()
                     .FirstOrDefault();
        var profile = attr?.Profile ?? obj.Provider ?? string.Empty;
        var container = attr?.Container ?? obj.Container ?? string.Empty;
        return (profile, container);
    }
}
