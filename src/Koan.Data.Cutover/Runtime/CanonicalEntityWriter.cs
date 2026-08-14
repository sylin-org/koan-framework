using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Koan.Data.Core.Polymorphism;
using Newtonsoft.Json.Linq;

namespace Koan.Data.Cutover.Runtime;

internal static class CanonicalEntityWriter
{
    internal static byte[] Write(string rootIdentity, object entity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootIdentity);
        ArgumentNullException.ThrowIfNull(entity);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteString(writer, "koan-data-cutover-v1");
        WriteString(writer, rootIdentity);
        WriteString(writer, EntityTypeCatalog.TypeId(entity.GetType()));
        WriteToken(writer, EntityJsonSerialization.SerializeDocumentToken(entity));
        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteToken(BinaryWriter writer, JToken token)
    {
        switch (token)
        {
            case JObject value:
                writer.Write((byte)1);
                var properties = value.Properties().OrderBy(static property => property.Name, StringComparer.Ordinal).ToArray();
                writer.Write(properties.Length);
                foreach (var property in properties)
                {
                    WriteString(writer, property.Name);
                    WriteToken(writer, property.Value);
                }
                return;
            case JArray value:
                writer.Write((byte)2);
                writer.Write(value.Count);
                foreach (var item in value) WriteToken(writer, item);
                return;
            case JValue value:
                WriteValue(writer, value);
                return;
            default:
                throw new InvalidDataException($"Unsupported canonical Entity token '{token.Type}'.");
        }
    }

    private static void WriteValue(BinaryWriter writer, JValue value)
    {
        if (value.Type is JTokenType.Null or JTokenType.Undefined || value.Value is null)
        {
            writer.Write((byte)0);
            return;
        }

        switch (value.Value)
        {
            case bool boolean:
                writer.Write((byte)3);
                writer.Write(boolean);
                return;
            case byte[] bytes:
                writer.Write((byte)4);
                writer.Write(bytes.Length);
                writer.Write(bytes);
                return;
            case DateTime dateTime:
                writer.Write((byte)5);
                WriteString(writer, dateTime.ToString("O", CultureInfo.InvariantCulture));
                return;
            case DateTimeOffset dateTimeOffset:
                writer.Write((byte)6);
                WriteString(writer, dateTimeOffset.ToString("O", CultureInfo.InvariantCulture));
                return;
            case decimal number:
                writer.Write((byte)7);
                WriteString(writer, number.ToString("G29", CultureInfo.InvariantCulture));
                return;
            case double number:
                writer.Write((byte)8);
                WriteString(writer, number.ToString("R", CultureInfo.InvariantCulture));
                return;
            case float number:
                writer.Write((byte)9);
                WriteString(writer, number.ToString("R", CultureInfo.InvariantCulture));
                return;
            case Guid guid:
                writer.Write((byte)10);
                WriteString(writer, guid.ToString("D"));
                return;
            case TimeSpan duration:
                writer.Write((byte)11);
                WriteString(writer, duration.ToString("c", CultureInfo.InvariantCulture));
                return;
            case Uri uri:
                writer.Write((byte)12);
                WriteString(writer, uri.OriginalString);
                return;
            case string text:
                writer.Write((byte)13);
                WriteString(writer, text);
                return;
            case IFormattable formattable:
                writer.Write((byte)14);
                WriteString(writer, value.Type.ToString());
                WriteString(writer, formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty);
                return;
            default:
                writer.Write((byte)15);
                WriteString(writer, value.Type.ToString());
                WriteString(writer, Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? string.Empty);
                return;
        }
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
}

internal sealed class LogicalDigest : IDisposable
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private long _count;
    private bool _finished;
    private bool _disposed;

    internal long Count => _count;

    internal void Append(byte[] record)
    {
        ObjectDisposedException.ThrowIf(_finished || _disposed, this);
        Span<byte> length = stackalloc byte[sizeof(int)];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, record.Length);
        _hash.AppendData(length);
        _hash.AppendData(record);
        _count = checked(_count + 1L);
    }

    internal string Finish()
    {
        ObjectDisposedException.ThrowIf(_finished || _disposed, this);
        _finished = true;
        return Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _hash.Dispose();
    }
}
