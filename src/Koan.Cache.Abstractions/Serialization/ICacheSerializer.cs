using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Primitives;

namespace Koan.Cache.Abstractions.Serialization;

public interface ICacheSerializer
{
    string ContentType { get; }

    bool CanHandle(Type type);

    ValueTask<CacheValue> SerializeAsync<T>(T value, CacheEntryOptions options, CancellationToken ct);

    ValueTask<CacheValue> SerializeAsync(object value, Type runtimeType, CacheEntryOptions options, CancellationToken ct);

    ValueTask<T?> DeserializeAsync<T>(CacheValue value, CancellationToken ct);

    ValueTask<object?> DeserializeAsync(CacheValue value, Type returnType, CancellationToken ct);
}
