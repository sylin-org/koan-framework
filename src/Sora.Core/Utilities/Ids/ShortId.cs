using System;
using System.Buffers.Text;

namespace Sora.Core.Utilities.Ids;

/// <summary>
/// ShortId: 22-char URL-safe base64 Guid (no padding). Provides round-trip helpers.
/// </summary>
public static class ShortId
{
    /// <summary>Creates a new 22-char base64url-encoded Guid string.</summary>
    public static string New() => From(Guid.NewGuid());

    /// <summary>Encodes the given Guid as a 22-char base64url string.</summary>
    public static string From(Guid value)
    {
        Span<byte> bytes = stackalloc byte[16];
        value.TryWriteBytes(bytes);
        // base64url without padding
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>Encodes a 16-byte GUID buffer to ShortId.</summary>
    public static string FromBytes(ReadOnlySpan<byte> guidBytes)
    {
        if (guidBytes.Length != 16) throw new ArgumentException("Expected 16 bytes", nameof(guidBytes));
        return Convert.ToBase64String(guidBytes.ToArray())
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>Decodes a 22/24-char base64/base64url Guid string.</summary>
    public static Guid ToGuid(string shortId)
    {
        if (TryToGuid(shortId, out var g)) return g;
        throw new FormatException("Invalid ShortId format.");
    }

    /// <summary>Tries to decode a ShortId to Guid.</summary>
    public static bool TryToGuid(string? shortId, out Guid guid)
    {
        guid = Guid.Empty;
        if (string.IsNullOrWhiteSpace(shortId)) return false;
        var s = shortId.Replace('-', '+').Replace('_', '/');
        // Restore padding if missing
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        try
        {
            var bytes = Convert.FromBase64String(s);
            if (bytes.Length != 16) return false;
            guid = new Guid(bytes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Decodes a ShortId to the 16-byte Guid buffer. Returns false if invalid.</summary>
    public static bool TryToBytes(string? shortId, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(shortId)) return false;
        var s = shortId.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        try
        {
            var b = Convert.FromBase64String(s);
            if (b.Length != 16) return false;
            bytes = b;
            return true;
        }
        catch { return false; }
    }
}
