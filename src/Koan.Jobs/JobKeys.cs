using System.Security.Cryptography;
using System.Text;

namespace Koan.Jobs;

/// <summary>Row identities Jobs derives from business keys (JOBS-0010). A key of any length or alphabet becomes a short
/// fixed-length id every store can index and compare, so one key always addresses one row.</summary>
internal static class JobKeys
{
    /// <summary><paramref name="prefix"/> followed by the lowercase hex SHA-256 of <paramref name="key"/>.</summary>
    public static string Of(string prefix, string key)
        => prefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    /// <summary>A name-based GUID (version 8) from the SHA-256 of <paramref name="prefix"/> and <paramref name="key"/> —
    /// a fixed-width key every store indexes, SQL Server's computed-column indexes included.</summary>
    public static Guid GuidOf(string prefix, string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(prefix + key)).AsSpan(0, 16).ToArray();
        hash[7] = (byte)((hash[7] & 0x0F) | 0x80);   // version 8: custom, name-derived
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);   // RFC 4122 variant
        return new Guid(hash);
    }
}
