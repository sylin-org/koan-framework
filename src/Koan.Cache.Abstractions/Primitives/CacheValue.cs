using System;
using System.Buffers;
using System.Text;

namespace Koan.Cache.Abstractions.Primitives;

public sealed record CacheValue
{
    private CacheValue(CacheContentKind kind, ReadOnlyMemory<byte> payload, string? text, Type? runtimeType)
    {
        ContentKind = kind;
        Payload = payload;
        Text = text;
        RuntimeType = runtimeType;
    }

    public CacheContentKind ContentKind { get; }

    public ReadOnlyMemory<byte> Payload { get; }

    public string? Text { get; }

    public Type? RuntimeType { get; }

    public bool IsEmpty => Payload.IsEmpty && string.IsNullOrEmpty(Text);

    public static CacheValue FromBytes(ReadOnlyMemory<byte> payload, Type? runtimeType = null)
        => new(CacheContentKind.Binary, payload, null, runtimeType);

    public static CacheValue FromString(string value, bool asBinary = false, Type? runtimeType = null)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return asBinary
            ? new CacheValue(CacheContentKind.Binary, Encoding.UTF8.GetBytes(value), value, runtimeType)
            : new CacheValue(CacheContentKind.String, ReadOnlyMemory<byte>.Empty, value, runtimeType);
    }

    public static CacheValue FromJson(string json, Type? runtimeType = null)
    {
        if (json is null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        return new CacheValue(CacheContentKind.Json, Encoding.UTF8.GetBytes(json), json, runtimeType);
    }

    public ReadOnlyMemory<byte> ToBytes()
        => ContentKind switch
        {
            CacheContentKind.Binary => Payload,
            CacheContentKind.String or CacheContentKind.Json => string.IsNullOrEmpty(Text) ? ReadOnlyMemory<byte>.Empty : Encoding.UTF8.GetBytes(Text!),
            _ => Payload
        };

    public string? ToText()
        => Text ?? (Payload.IsEmpty ? null : Encoding.UTF8.GetString(Payload.Span));
}
