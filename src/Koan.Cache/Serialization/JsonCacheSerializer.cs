using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Serialization;
using Koan.Core.Json;
using Koan.Data.Core.Polymorphism;
using Newtonsoft.Json;

namespace Koan.Cache.Serialization;

public sealed class JsonCacheSerializer : ICacheSerializer
{
    private static readonly JsonSerializerSettings EntityJsonSettings =
        EntityJsonSerialization.Apply(new JsonSerializerSettings(JsonDefaults.Settings));

    public string ContentType => CacheConstants.ContentTypes.Json;

    public bool CanHandle(Type type)
        => true;

    public ValueTask<CacheValue> SerializeAsync<T>(T value, CacheEntryOptions options, CancellationToken ct)
        => SerializeInternal(value, typeof(T), ct);

    public ValueTask<CacheValue> Serialize(object value, Type runtimeType, CacheEntryOptions options, CancellationToken ct)
        => SerializeInternal(value, runtimeType, ct);

    public ValueTask<T?> DeserializeAsync<T>(CacheValue value, CancellationToken ct)
        => DeserializeInternalAsync<T>(value, typeof(T), ct);

    public async ValueTask<object?> Deserialize(CacheValue value, Type returnType, CancellationToken ct)
    {
        var result = await DeserializeInternalAsync<object?>(value, returnType, ct);
        return result;
    }

    private static ValueTask<CacheValue> SerializeInternal(object? value, Type runtimeType, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (value is null)
        {
            return ValueTask.FromResult(CacheValue.FromJson("null", runtimeType));
        }

        var actualType = value.GetType();
        var json = JsonConvert.SerializeObject(value, actualType, EntityJsonSettings);
        return ValueTask.FromResult(CacheValue.FromJson(json, runtimeType));
    }

    private static ValueTask<T?> DeserializeInternalAsync<T>(CacheValue value, Type runtimeType, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (value.IsEmpty)
        {
            return ValueTask.FromResult(default(T));
        }

        var text = value.ToText();
        if (string.IsNullOrWhiteSpace(text))
        {
            return ValueTask.FromResult(default(T));
        }

        var result = JsonConvert.DeserializeObject(text, runtimeType, EntityJsonSettings);
        return ValueTask.FromResult((T?)result);
    }
}
