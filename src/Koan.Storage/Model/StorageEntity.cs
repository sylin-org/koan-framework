using Koan.Storage.Abstractions;
using Koan.Storage.Extensions;

namespace Koan.Storage.Model;

using Microsoft.Extensions.DependencyInjection;
using Koan.Core;
using Koan.Data.Core.Model;
using Koan.Storage;
using Koan.Storage.Keys;
using System.Text;
using Newtonsoft.Json;

// CRTP base for storage-backed entities with static creation helpers
public abstract class StorageEntity<TEntity> : Entity<TEntity>, IStorageObject
    where TEntity : class, IStorageObject
{
    // IStorageObject properties (minimal baseline, derived types may add more fields)
    public string Key { get; set; } = "";
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
        var profile = attr?.Profile ?? "";
        var container = overrideContainer ?? attr?.Container ?? "";
        return (profile, container);
    }

    private static IStorageService Storage()
    => (Koan.Core.Hosting.App.AppHost.Current?.GetService(typeof(IStorageService)) as IStorageService)
           ?? throw new InvalidOperationException("IStorageService not available. Ensure AppBootstrapper.InitializeModules() ran and AppHost.Current is set (greenfield boot).");

    // Carry the entity type through the type-erased storage boundary so Storage can compile the applicable
    // segmentation dimensions once and honor [HostScoped] subjects.
    private static IDisposable Scope() => StorageScope.For(typeof(TEntity));

    // Static creators
    public static async Task<TEntity> CreateTextFile(string name, string content, string? contentType = "text/plain; charset=utf-8", CancellationToken ct = default)
    {
        using var _scope = Scope();
        var (profile, container) = ResolveBinding();
        var obj = await Storage().CreateTextFile(name, content, contentType, profile, container, ct);
        return From(obj, name);
    }

    public static async Task<TEntity> Create<TDoc>(string name, TDoc value, JsonSerializerSettings? options = null, string contentType = "application/json; charset=utf-8", CancellationToken ct = default)
    {
        using var _scope = Scope();
        var (profile, container) = ResolveBinding();
        var obj = await Storage().CreateJson(name, value, options, profile, container, contentType, ct);
        return From(obj, name);
    }

    public static async Task<TEntity> Create(string name, ReadOnlyMemory<byte> bytes, string? contentType = "application/octet-stream", CancellationToken ct = default)
    {
        using var _scope = Scope();
        var (profile, container) = ResolveBinding();
        var obj = await Storage().Create(name, bytes, contentType, profile, container, ct);
        return From(obj, name);
    }

    // Convenience overload to ensure byte[] goes to binary path (not JSON generic)
    public static Task<TEntity> Create(string name, byte[] bytes, string? contentType = "application/octet-stream", CancellationToken ct = default)
        => Create(name, (ReadOnlyMemory<byte>)bytes, contentType, ct);

    public static async Task<TEntity> Onboard(string name, Stream content, string? contentType = null, CancellationToken ct = default)
    {
        using var _scope = Scope();
        var (profile, container) = ResolveBinding();
        var obj = await Storage().Onboard(name, content, contentType, profile, container, ct);
        return From(obj, name);
    }

    // Instance operations
    public async Task<string> ReadAllText(Encoding? encoding = null, CancellationToken ct = default)
    {
        using var _scope = Scope();
        var (profile, container) = InstanceBinding();
        return await Storage().ReadAllText(profile, container, Key, encoding, ct);
    }

    public async Task<byte[]> ReadAllBytes(CancellationToken ct = default)
    {
        using var _scope = Scope();
        var (profile, container) = InstanceBinding();
        return await Storage().ReadAllBytes(profile, container, Key, ct);
    }

    public async Task<string> ReadRangeAsString(long from, long to, Encoding? encoding = null, CancellationToken ct = default)
    {
        using var _scope = Scope();
        var (profile, container) = InstanceBinding();
        return await Storage().ReadRangeAsString(profile, container, Key, from, to, encoding, ct);
    }

    // Stream-based reads (DX helpers)
    public async Task<Stream> OpenRead(CancellationToken ct = default)
    {
        using var _scope = Scope();
        var (profile, container) = InstanceBinding();
        return await Storage().Read(profile, container, Key, ct);
    }

    public async Task<(Stream Stream, long? Length)> OpenReadRange(long? from = null, long? to = null, CancellationToken ct = default)
    {
        using var _scope = Scope();
        var (profile, container) = InstanceBinding();
        return await Storage().ReadRange(profile, container, Key, from, to, ct);
    }

    public async Task<ObjectStat?> Head(CancellationToken ct = default)
    {
        using var _scope = Scope();
        var (profile, container) = InstanceBinding();
        return await Storage().Head(profile, container, Key, ct);
    }

    public async Task<bool> Delete(CancellationToken ct = default)
    {
        using var _scope = Scope();
        var (profile, container) = InstanceBinding();
        return await Storage().Delete(profile, container, Key, ct);
    }

    public async Task<TTarget> CopyTo<TTarget>(CancellationToken ct = default)
        where TTarget : class, IStorageObject
    {
        // STOR-0011: a tiering copy composes with the SOURCE type's axes (same-tenant tiering is the supported case;
        // a cross-type transfer whose target carries different axes is an accepted limitation — the guard fires for
        // the source type, and the single TransferToProfile key cannot express a re-scoped target).
        using var _scope = Scope();
        // If this is a lightweight proxy (no provider known), defer to default profile resolution
        var (sourceProfile, sourceContainer) = string.IsNullOrWhiteSpace(Provider)
            ? ("", "")
            : InstanceBinding();
        var (targetProfile, targetContainer) = ResolveBindingFor<TTarget>();
        var obj = await Storage().TransferToProfile(sourceProfile, sourceContainer, Key, targetProfile, targetContainer, deleteSource: false, ct);
        return To<TTarget>(obj, Key);
    }

    public async Task<TTarget> MoveTo<TTarget>(CancellationToken ct = default)
        where TTarget : class, IStorageObject
    {
        using var _scope = Scope();
        var (sourceProfile, sourceContainer) = string.IsNullOrWhiteSpace(Provider)
            ? ("", "")
            : InstanceBinding();
        var (targetProfile, targetContainer) = ResolveBindingFor<TTarget>();
        var obj = await Storage().TransferToProfile(sourceProfile, sourceContainer, Key, targetProfile, targetContainer, deleteSource: true, ct);
        return To<TTarget>(obj, Key);
    }

    private static (string Profile, string Container) ResolveBindingFor<T>()
    {
        var t = typeof(T);
        var attr = t.GetCustomAttributes(typeof(StorageBindingAttribute), inherit: false).OfType<StorageBindingAttribute>().FirstOrDefault();
        var profile = attr?.Profile ?? "";
        var container = attr?.Container ?? "";
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
        using var _scope = Scope();
        var (profile, container) = ResolveBinding();
        return Storage().Read(profile, container, key, ct);
    }

    public static Task<(Stream Stream, long? Length)> OpenReadRange(string key, long? from = null, long? to = null, CancellationToken ct = default)
    {
        using var _scope = Scope();
        var (profile, container) = ResolveBinding();
        return Storage().ReadRange(profile, container, key, from, to, ct);
    }

    public static async Task<ObjectStat?> Head(string key, CancellationToken ct = default)
    {
        // Defensive guard: a null/empty key is a not-found, not an exception. Callers like
        // MediaContentController's [HttpGet("{**key}")] can receive empty segments from clients;
        // pushing them to the storage provider (which throws on null) would surface as 500s
        // rather than the natural 404.
        if (string.IsNullOrWhiteSpace(key)) return null;

        using var _scope = Scope();
        var (profile, container) = ResolveBinding();
        var stat = await Storage().Head(profile, container, key, ct);
        if (stat is null) return null;

        // Provider ContentType reliability varies: S3/Azure persist it via service metadata;
        // the local-filesystem provider doesn't carry a sidecar today and returns null. When
        // the provider can't supply ContentType, fall back to the persisted entity row — that's
        // the value the caller passed to Upload/Put, and it's authoritative regardless of how
        // the storage backend chose to (not) record it. The query is keyed on Key (the LOGICAL
        // key — the entity row stores the logical key, STOR-0011 §5; typically indexed) and only
        // runs when the fast path is empty; providers that DO persist mime skip this entirely.
        if (string.IsNullOrWhiteSpace(stat.ContentType))
        {
            var entity = (await Query(e => ((IStorageObject)e).Key == key, ct)).FirstOrDefault();
            if (entity is IStorageObject so && !string.IsNullOrWhiteSpace(so.ContentType))
            {
                stat = stat with { ContentType = so.ContentType };
            }
        }
        return stat;
    }

    // Map from StorageObject to TEntity (shallow copy of storage metadata).
    // STOR-0011 §5: the entity holds the LOGICAL key (the caller's name). The service-returned StorageObject.Key is
    // PHYSICAL (axis-composed); never derive .Key from it on the write-return path — pass the logical key explicitly.
    private static TEntity From(StorageObject obj, string? logicalKey = null)
    {
        var inst = Activator.CreateInstance<TEntity>();
        if (inst is StorageEntity<TEntity> se)
        {
            // Do NOT copy obj.Id - preserve entity's auto-generated GUID v7
            // The storage key is in obj.Key, not obj.Id
            se.Key = logicalKey ?? obj.Key;
            // STOR-0011 §5: StorageService sets StorageObject.Name = the (now physical, axis-composed) key; the
            // entity's Name must hold the LOGICAL name, not "acme/photo.jpg". Fall back to obj.Name only off-path.
            se.Name = logicalKey ?? obj.Name;
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

    private static TTarget To<TTarget>(StorageObject obj, string? logicalKey = null)
        where TTarget : class, IStorageObject
    {
        var inst = Activator.CreateInstance<TTarget>();
        if (inst is StorageEntity<TTarget> se)
        {
            // Do NOT copy obj.Id - preserve entity's auto-generated GUID v7
            // The storage key is in obj.Key, not obj.Id
            se.Key = logicalKey ?? obj.Key;
            se.Name = logicalKey ?? obj.Name;   // STOR-0011 §5: logical name, not the physical composed key
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
