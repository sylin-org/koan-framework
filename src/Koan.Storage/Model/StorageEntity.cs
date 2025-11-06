using Koan.Storage.Abstractions;
using Koan.Storage.Extensions;

namespace Koan.Storage.Model;

using Microsoft.Extensions.DependencyInjection;
using Koan.Core;
using Koan.Data.Core.Model;
using Koan.Storage.Infrastructure;
using System.Text;
using Newtonsoft.Json;

// CRTP base for storage-backed entities with static creation helpers
public abstract class StorageEntity<TEntity> : Entity<TEntity>, IStorageObject
    where TEntity : class, IStorageObject
{
    // IStorageObject properties (minimal baseline, derived types may add more fields)
    public string Key { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? ContentType { get; set; }
    public long Size { get; set; }
    public string? ContentHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? Provider { get; set; }
    public string? Container { get; set; }
    public IReadOnlyDictionary<string, string>? Tags { get; set; }

    // Resolve target profile/container from attribute or options default
    private static (string Profile, string Container) ResolveBinding(string? overrideContainer = null)
    {
        var t = typeof(TEntity);
        var attr = t.GetCustomAttributes(typeof(StorageBindingAttribute), inherit: false).OfType<StorageBindingAttribute>().FirstOrDefault();
        var profile = attr?.Profile ?? string.Empty;
        var container = overrideContainer ?? attr?.Container ?? string.Empty;
        return (profile, container);
    }

    private static IStorageService Storage()
    => (Koan.Core.Hosting.App.AppHost.Current?.GetService(typeof(IStorageService)) as IStorageService)
           ?? throw new InvalidOperationException("IStorageService not available. Ensure AppBootstrapper.InitializeModules() ran and AppHost.Current is set (greenfield boot).");

    // Static creators
    public static async Task<TEntity> CreateTextFile(string name, string content, string? contentType = "text/plain; charset=utf-8", CancellationToken ct = default)
    {
        var (profile, container) = ResolveBinding();
        var obj = await Storage().CreateTextFile(name, content, contentType, profile, container, ct);
        return From(obj);
    }

    public static async Task<TEntity> Create<TDoc>(string name, TDoc value, JsonSerializerSettings? options = null, string contentType = "application/json; charset=utf-8", CancellationToken ct = default)
    {
        var (profile, container) = ResolveBinding();
        var obj = await Storage().CreateJson(name, value, options, profile, container, contentType, ct);
        return From(obj);
    }

    public static async Task<TEntity> Create(string name, ReadOnlyMemory<byte> bytes, string? contentType = "application/octet-stream", CancellationToken ct = default)
    {
        var (profile, container) = ResolveBinding();
        var obj = await Storage().Create(name, bytes, contentType, profile, container, ct);
        return From(obj);
    }

    // Convenience overload to ensure byte[] goes to binary path (not JSON generic)
    public static Task<TEntity> Create(string name, byte[] bytes, string? contentType = "application/octet-stream", CancellationToken ct = default)
        => Create(name, (ReadOnlyMemory<byte>)bytes, contentType, ct);

    public static async Task<TEntity> Onboard(string name, Stream content, string? contentType = null, CancellationToken ct = default)
    {
        var (profile, container) = ResolveBinding();
        var obj = await Storage().Onboard(name, content, contentType, profile, container, ct);
        return From(obj);
    }

    // Instance operations
    public async Task<string> ReadAllText(Encoding? encoding = null, CancellationToken ct = default)
    {
        var (profile, container) = InstanceBinding();
        return await Storage().ReadAllText(profile, container, Key, encoding, ct);
    }

    public async Task<byte[]> ReadAllBytes(CancellationToken ct = default)
    {
        var (profile, container) = InstanceBinding();
        return await Storage().ReadAllBytes(profile, container, Key, ct);
    }

    public async Task<string> ReadRangeAsString(long from, long to, Encoding? encoding = null, CancellationToken ct = default)
    {
        var (profile, container) = InstanceBinding();
        return await Storage().ReadRangeAsString(profile, container, Key, from, to, encoding, ct);
    }

    // Stream-based reads (DX helpers)
    public async Task<Stream> OpenRead(CancellationToken ct = default)
    {
        var (profile, container) = InstanceBinding();
        return await Storage().ReadAsync(profile, container, Key, ct);
    }

    public async Task<(Stream Stream, long? Length)> OpenReadRange(long? from = null, long? to = null, CancellationToken ct = default)
    {
        var (profile, container) = InstanceBinding();
        return await Storage().ReadRangeAsync(profile, container, Key, from, to, ct);
    }

    public async Task<ObjectStat?> Head(CancellationToken ct = default)
    {
        var (profile, container) = InstanceBinding();
        return await Storage().HeadAsync(profile, container, Key, ct);
    }

    public async Task<bool> Delete(CancellationToken ct = default)
    {
        var (profile, container) = InstanceBinding();
        return await Storage().DeleteAsync(profile, container, Key, ct);
    }

    public async Task<TTarget> CopyTo<TTarget>(CancellationToken ct = default)
        where TTarget : class, IStorageObject
    {
        // If this is a lightweight proxy (no provider known), defer to default profile resolution
        var (sourceProfile, sourceContainer) = string.IsNullOrWhiteSpace(Provider)
            ? ("", "")
            : InstanceBinding();
        var (targetProfile, targetContainer) = ResolveBindingFor<TTarget>();
        var obj = await Storage().TransferToProfileAsync(sourceProfile, sourceContainer, Key, targetProfile, targetContainer, deleteSource: false, ct);
        return To<TTarget>(obj);
    }

    public async Task<TTarget> MoveTo<TTarget>(CancellationToken ct = default)
        where TTarget : class, IStorageObject
    {
        var (sourceProfile, sourceContainer) = string.IsNullOrWhiteSpace(Provider)
            ? ("", "")
            : InstanceBinding();
        var (targetProfile, targetContainer) = ResolveBindingFor<TTarget>();
        var obj = await Storage().TransferToProfileAsync(sourceProfile, sourceContainer, Key, targetProfile, targetContainer, deleteSource: true, ct);
        return To<TTarget>(obj);
    }

    private static (string Profile, string Container) ResolveBindingFor<T>()
    {
        var t = typeof(T);
        var attr = t.GetCustomAttributes(typeof(StorageBindingAttribute), inherit: false).OfType<StorageBindingAttribute>().FirstOrDefault();
        var profile = attr?.Profile ?? string.Empty;
        var container = attr?.Container ?? string.Empty;
        return (profile, container);
    }

    private (string Profile, string Container) InstanceBinding()
    {
        // Prefer the type-level binding; allow instance Container override when set
        var overrideContainer = string.IsNullOrWhiteSpace(Container) ? null : Container;
        return ResolveBinding(overrideContainer);
    }

    // Static key-first helpers (DX): operate by key using type-level binding
    public static Task<Stream> OpenRead(string key, CancellationToken ct = default)
    {
        var (profile, container) = ResolveBinding();
        return Storage().ReadAsync(profile, container, key, ct);
    }

    public static Task<(Stream Stream, long? Length)> OpenReadRange(string key, long? from = null, long? to = null, CancellationToken ct = default)
    {
        var (profile, container) = ResolveBinding();
        return Storage().ReadRangeAsync(profile, container, key, from, to, ct);
    }

    public static Task<ObjectStat?> Head(string key, CancellationToken ct = default)
    {
        var (profile, container) = ResolveBinding();
        return Storage().HeadAsync(profile, container, key, ct);
    }

    // Map from StorageObject to TEntity (shallow copy of storage metadata)
    private static TEntity From(StorageObject obj)
    {
        var inst = Activator.CreateInstance<TEntity>();
        if (inst is StorageEntity<TEntity> se)
        {
            // Do NOT copy obj.Id - preserve entity's auto-generated GUID v7
            // The storage key is in obj.Key, not obj.Id
            se.Key = obj.Key;
            se.Name = obj.Name;
            se.ContentType = obj.ContentType;
            se.Size = obj.Size;
            se.ContentHash = obj.ContentHash;
            se.CreatedAt = obj.CreatedAt;
            se.UpdatedAt = obj.UpdatedAt;
            se.Provider = obj.Provider;
            se.Container = obj.Container;
            se.Tags = obj.Tags;
        }
        return inst;
    }

    private static TTarget To<TTarget>(StorageObject obj)
        where TTarget : class, IStorageObject
    {
        var inst = Activator.CreateInstance<TTarget>();
        if (inst is StorageEntity<TTarget> se)
        {
            // Do NOT copy obj.Id - preserve entity's auto-generated GUID v7
            // The storage key is in obj.Key, not obj.Id
            se.Key = obj.Key;
            se.Name = obj.Name;
            se.ContentType = obj.ContentType;
            se.Size = obj.Size;
            se.ContentHash = obj.ContentHash;
            se.CreatedAt = obj.CreatedAt;
            se.UpdatedAt = obj.UpdatedAt;
            se.Provider = obj.Provider;
            se.Container = obj.Container;
            se.Tags = obj.Tags;
        }
        return inst;
    }

    // Lightweight proxy to enable: Model.Get(key).ReadAllText()
    public static TEntity Get(string key, string? name = null)
    {
        var inst = Activator.CreateInstance<TEntity>();
        if (inst is StorageEntity<TEntity> se)
        {
            se.Key = key;
            se.Name = name;
            // Use type binding for container; provider resolved at call-site by InstanceBinding()
            var (_, container) = ResolveBinding();
            se.Container = container;
        }
        return inst;
    }
}
