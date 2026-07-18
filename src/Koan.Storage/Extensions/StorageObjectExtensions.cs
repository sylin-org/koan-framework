using Koan.Storage.Abstractions;

namespace Koan.Storage.Extensions;

using Microsoft.Extensions.DependencyInjection;
using Koan.Core;
using Koan.Storage.Keys;
using System.Text;

public static class StorageObjectExtensions
{
    private static IStorageService Storage()
    => (Koan.Core.Hosting.App.AppHost.Current?.GetService(typeof(IStorageService)) as IStorageService)
           ?? throw new InvalidOperationException("IStorageService not available. Ensure AppBootstrapper.InitializeModules() ran and AppHost.Current is set (greenfield boot).");

    // Carry the instance's runtime type through the type-erased storage boundary. Without this, an
    // IStorageObject-typed reference would take the raw isolation path and lose subject applicability such as
    // [HostScoped].
    private static IDisposable Scope(IStorageObject obj) => StorageScope.For(obj.GetType());

    public static Task<string> ReadAllText(this IStorageObject obj, Encoding? encoding = null, CancellationToken ct = default)
    {
        using var _scope = Scope(obj);
        var (profile, container) = ResolveBindingFromInstance(obj);
        return Storage().ReadAllText(profile, container, obj.Key, encoding, ct);
    }

    public static Task<byte[]> ReadAllBytes(this IStorageObject obj, CancellationToken ct = default)
    {
        using var _scope = Scope(obj);
        var (profile, container) = ResolveBindingFromInstance(obj);
        return Storage().ReadAllBytes(profile, container, obj.Key, ct);
    }

    public static Task<ObjectStat?> Head(this IStorageObject obj, CancellationToken ct = default)
    {
        using var _scope = Scope(obj);
        var (profile, container) = ResolveBindingFromInstance(obj);
        return Storage().Head(profile, container, obj.Key, ct);
    }

    public static Task<bool> Delete(this IStorageObject obj, CancellationToken ct = default)
    {
        using var _scope = Scope(obj);
        var (profile, container) = ResolveBindingFromInstance(obj);
        return Storage().Delete(profile, container, obj.Key, ct);
    }

    public static async Task<TTarget> CopyTo<TTarget>(this IStorageObject obj, CancellationToken ct = default)
        where TTarget : class, IStorageObject
    {
        using var _scope = Scope(obj);   // compose the source key with the source type's axes (same-scope tiering)
        var targetType = typeof(TTarget);
        var binding = targetType.GetCustomAttributes(typeof(StorageBindingAttribute), false)
                                 .OfType<StorageBindingAttribute>()
                                 .FirstOrDefault();
        var profile = binding?.Profile ?? "";
        var container = binding?.Container ?? "";
        var result = await Storage().TransferToProfile(obj.Provider ?? "", obj.Container ?? "", obj.Key, profile, container, deleteSource: false, ct);
        return Hydrate<TTarget>(obj, result);
    }

    public static async Task<TTarget> MoveTo<TTarget>(this IStorageObject obj, CancellationToken ct = default)
        where TTarget : class, IStorageObject
    {
        using var _scope = Scope(obj);
        var targetType = typeof(TTarget);
        var binding = targetType.GetCustomAttributes(typeof(StorageBindingAttribute), false)
                                 .OfType<StorageBindingAttribute>()
                                 .FirstOrDefault();
        var profile = binding?.Profile ?? "";
        var container = binding?.Container ?? "";
        var result = await Storage().TransferToProfile(obj.Provider ?? "", obj.Container ?? "", obj.Key, profile, container, deleteSource: true, ct);
        return Hydrate<TTarget>(obj, result);
    }

    // STOR-0011 §5: the target entity holds the SOURCE's LOGICAL key/name (a tiering transfer keeps the logical
    // identity); the service-returned StorageObject's Key/Name are the PHYSICAL (axis-composed) key and must never
    // round-trip onto the entity (else a later typed read double-prefixes). The new entity keeps its own GUID v7
    // (do not copy result.Id). Physical-storage metadata (size/hash/provider/container/timestamps) comes from result.
    private static TTarget Hydrate<TTarget>(IStorageObject source, StorageObject result)
        where TTarget : class, IStorageObject
    {
        if (Activator.CreateInstance<TTarget>() is Model.StorageEntity<TTarget> se)
        {
            se.Key = source.Key;         // logical
            se.Name = source.Name;       // logical
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
        var attr = t.GetCustomAttributes(typeof(StorageBindingAttribute), false)
                     .OfType<StorageBindingAttribute>()
                     .FirstOrDefault();
        var profile = attr?.Profile ?? obj.Provider ?? "";
        var container = attr?.Container ?? obj.Container ?? "";
        return (profile, container);
    }
}
