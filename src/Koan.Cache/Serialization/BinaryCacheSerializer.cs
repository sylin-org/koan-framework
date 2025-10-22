using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Serialization;

namespace Koan.Cache.Serialization;

public sealed class BinaryCacheSerializer : ICacheSerializer
{
    public string ContentType => CacheConstants.ContentTypes.Binary;

    public bool CanHandle(Type type)
        => type == typeof(byte[]) || type == typeof(ReadOnlyMemory<byte>) || type == typeof(Memory<byte>) || type == typeof(Stream);

    public ValueTask<CacheValue> SerializeAsync<T>(T value, CacheEntryOptions options, CancellationToken ct)
        => SerializeAsync(value!, typeof(T), options, ct);

    public ValueTask<CacheValue> SerializeAsync(object value, Type runtimeType, CacheEntryOptions options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (value is byte[] buffer)
        {
            return ValueTask.FromResult(CacheValue.FromBytes(buffer, runtimeType));
        }

        if (value is ReadOnlyMemory<byte> rom)
        {
            return ValueTask.FromResult(CacheValue.FromBytes(rom, runtimeType));
        }

        if (value is Memory<byte> mem)
        {
            return ValueTask.FromResult(CacheValue.FromBytes(mem.ToArray(), runtimeType));
        }

        if (value is Stream stream)
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ValueTask.FromResult(CacheValue.FromBytes(ms.ToArray(), runtimeType));
        }

        throw new InvalidOperationException($"BinaryCacheSerializer cannot handle type {runtimeType}");
    }

    public ValueTask<T?> DeserializeAsync<T>(CacheValue value, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var result = (T?)Convert(value, typeof(T));
        return ValueTask.FromResult(result);
    }

    public ValueTask<object?> DeserializeAsync(CacheValue value, Type returnType, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var result = Convert(value, returnType);
        return ValueTask.FromResult(result);
    }

    private static object? Convert(CacheValue value, Type returnType)
    {
        if (value.IsEmpty)
        {
            return returnType switch
            {
                Type t when t == typeof(byte[]) => Array.Empty<byte>(),
                Type t when t == typeof(ReadOnlyMemory<byte>) => ReadOnlyMemory<byte>.Empty,
                Type t when t == typeof(Memory<byte>) => Memory<byte>.Empty,
                _ => null
            };
        }

        var payload = value.ToBytes().ToArray();
        if (returnType == typeof(byte[]))
        {
            return payload;
        }

        if (returnType == typeof(ReadOnlyMemory<byte>))
        {
            return new ReadOnlyMemory<byte>(payload);
        }

        if (returnType == typeof(Memory<byte>))
        {
            return new Memory<byte>(payload);
        }

        if (returnType == typeof(Stream))
        {
            return new MemoryStream(payload, writable: false);
        }

        return payload;
    }
}
