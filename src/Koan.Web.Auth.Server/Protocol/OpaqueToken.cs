using System.Security.Cryptography;

namespace Koan.Web.Auth.Server.Protocol;

/// <summary>
/// SEC-0006 D4/D7/D8 — high-entropy, opaque, URL-safe handles for authorization codes, consent-request ids
/// (<c>rid</c>), device codes, and browser-binding secrets. ≥128-bit by default, from a CSPRNG.
/// </summary>
internal static class OpaqueToken
{
    public static string New(int bytes = 32)
    {
        Span<byte> buffer = stackalloc byte[bytes];
        RandomNumberGenerator.Fill(buffer);
        return Base64Url(buffer);
    }

    public static string Base64Url(ReadOnlySpan<byte> data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
