using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Serialization;

namespace Koan.Cache.Serialization;

public sealed class StringCacheSerializer : ICacheSerializer
{
    public string ContentType => CacheConstants.ContentTypes.String;

    public bool CanHandle(Type type)
        => type == typeof(string);

    public ValueTask<CacheValue> SerializeAsync<T>(T value, CacheEntryOptions options, CancellationToken ct)
        => SerializeAsync(value!, typeof(T), options, ct);

    public ValueTask<CacheValue> SerializeAsync(object value, Type runtimeType, CacheEntryOptions options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (value is not string str)
        {
            throw new InvalidOperationException($"StringCacheSerializer can only handle string types. Provided type: {runtimeType}");
        }

        return ValueTask.FromResult(CacheValue.FromString(str, runtimeType: runtimeType));
    }

    public ValueTask<T?> DeserializeAsync<T>(CacheValue value, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (value.IsEmpty)
        {
            return ValueTask.FromResult(default(T));
        }

        var text = value.ToText();
        return ValueTask.FromResult((T?)(object?)text);
    }

    public ValueTask<object?> DeserializeAsync(CacheValue value, Type returnType, CancellationToken ct)
        => DeserializeAsync<object?>(value, ct);
}
