using System.Security.Cryptography;
using System.Text;

namespace Koan.Web.Auth.Connector.Atproto.Storage;

/// <summary>Row ids composed from protocol values, so a store that compares ids without case never merges two of them.</summary>
internal static class StorageKeys
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The SHA-256 of <paramref name="kind"/> and <paramref name="value"/>, encoded strictly: a value that is not
    /// well-formed UTF-16 is refused rather than hashed as the replacement character.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not well-formed UTF-16.</exception>
    public static string Compose(string kind, string value)
    {
        try { return Convert.ToHexString(SHA256.HashData(StrictUtf8.GetBytes(kind + '\u001f' + value))); }
        catch (EncoderFallbackException invalid)
        {
            throw new ArgumentException("An AT Protocol value composed into a row id is not well-formed UTF-16.", nameof(value), invalid);
        }
    }
}
