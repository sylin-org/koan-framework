namespace Sora.Storage.Model;

using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Sora.Core;
using Sora.Data.Core.Model;
using Sora.Storage.Infrastructure;

// CRTP base for storage-backed entities with static creation helpers
public abstract class StorageEntity<TEntity> : Entity<TEntity>, IStorageObject
    where TEntity : class, IStorageObject
{
    // IStorageObject properties (minimal baseline, derived types may add more fields)
    public string Key { get; protected internal set; } = string.Empty;
    public string? Name { get; protected internal set; }
    public string? ContentType { get; protected internal set; }
    public long Size { get; protected internal set; }
    public string? ContentHash { get; protected internal set; }
    public DateTimeOffset CreatedAt { get; protected internal set; }
    public DateTimeOffset? UpdatedAt { get; protected internal set; }
    public string? Provider { get; protected internal set; }
    public string? Container { get; protected internal set; }
    public IReadOnlyDictionary<string, string>? Tags { get; protected internal set; }

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
        => (SoraApp.Current?.GetService(typeof(IStorageService)) as IStorageService)
           ?? throw new InvalidOperationException("IStorageService not available. Ensure SoraInitialization.InitializeModules() ran and SoraApp.Current is set.");

    // Static creators
    public static async Task<TEntity> CreateTextFile(string name, string content, string? contentType = "text/plain; charset=utf-8", CancellationToken ct = default)
    {
        var (profile, container) = ResolveBinding();
        var obj = await Storage().CreateTextFile(name, content, contentType, profile, container, ct).ConfigureAwait(false);
        return From(obj);
    }

    public static async Task<TEntity> Create<TDoc>(string name, TDoc value, JsonSerializerOptions? options = null, string contentType = "application/json; charset=utf-8", CancellationToken ct = default)
    {
        var (profile, container) = ResolveBinding();
        var obj = await Storage().CreateJson(name, value, options, profile, container, contentType, ct).ConfigureAwait(false);
        return From(obj);
    }

    public static async Task<TEntity> Create(string name, ReadOnlyMemory<byte> bytes, string? contentType = "application/octet-stream", CancellationToken ct = default)
    {
        var (profile, container) = ResolveBinding();
        var obj = await Storage().Create(name, bytes, contentType, profile, container, ct).ConfigureAwait(false);
        return From(obj);
    }

    public static async Task<TEntity> Onboard(string name, Stream content, string? contentType = null, CancellationToken ct = default)
    {
        var (profile, container) = ResolveBinding();
        var obj = await Storage().Onboard(name, content, contentType, profile, container, ct).ConfigureAwait(false);
        return From(obj);
    }

    // Instance operations
    public async Task<string> ReadAllText(Encoding? encoding = null, CancellationToken ct = default)
    {
        var (profile, container) = InstanceBinding();
        return await Storage().ReadAllText(profile, container, Key, encoding, ct).ConfigureAwait(false);
    }

    public async Task<byte[]> ReadAllBytes(CancellationToken ct = default)
    {
        var (profile, container) = InstanceBinding();
        return await Storage().ReadAllBytes(profile, container, Key, ct).ConfigureAwait(false);
    }

    public async Task<string> ReadRangeAsString(long from, long to, Encoding? encoding = null, CancellationToken ct = default)
    {
        var (profile, container) = InstanceBinding();
        return await Storage().ReadRangeAsString(profile, container, Key, from, to, encoding, ct).ConfigureAwait(false);
    }

    public async Task<ObjectStat?> Head(CancellationToken ct = default)
    {
        var (profile, container) = InstanceBinding();
        return await Storage().HeadAsync(profile, container, Key, ct).ConfigureAwait(false);
    }

    public async Task<bool> Delete(CancellationToken ct = default)
    {
        var (profile, container) = InstanceBinding();
        return await Storage().DeleteAsync(profile, container, Key, ct).ConfigureAwait(false);
    }

    public async Task<TTarget> CopyTo<TTarget>(CancellationToken ct = default)
        where TTarget : class, IStorageObject
    {
    var (sourceProfile, sourceContainer) = InstanceBinding();
    var (targetProfile, targetContainer) = ResolveBindingFor<TTarget>();
    var obj = await Storage().TransferToProfileAsync(sourceProfile, sourceContainer, Key, targetProfile, targetContainer, deleteSource: false, ct).ConfigureAwait(false);
        return To<TTarget>(obj);
    }

    public async Task<TTarget> MoveTo<TTarget>(CancellationToken ct = default)
        where TTarget : class, IStorageObject
    {
    var (sourceProfile, sourceContainer) = InstanceBinding();
    var (targetProfile, targetContainer) = ResolveBindingFor<TTarget>();
    var obj = await Storage().TransferToProfileAsync(sourceProfile, sourceContainer, Key, targetProfile, targetContainer, deleteSource: true, ct).ConfigureAwait(false);
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

    // Map from StorageObject to TEntity (shallow copy of storage metadata)
    private static TEntity From(StorageObject obj)
    {
        var inst = Activator.CreateInstance<TEntity>();
        if (inst is StorageEntity<TEntity> se)
        {
            se.Id = obj.Id;
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
            se.Id = obj.Id;
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
