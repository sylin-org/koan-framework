using System.Security.Cryptography;
using System.Text;

namespace Koan.Web.Auth.Server.Protocol;

/// <summary>
/// SEC-0006 D4 — PKCE (RFC 7636) verification. S256 is the only accepted method; <c>plain</c> is rejected.
/// </summary>
internal static class Pkce
{
    public const string MethodS256 = "S256";

    /// <summary>A syntactically valid code_challenge is present and uses S256.</summary>
    public static bool IsValidChallenge(string? challenge, string? method)
        => !string.IsNullOrWhiteSpace(challenge)
           && string.Equals(method, MethodS256, StringComparison.Ordinal);

    /// <summary>
    /// Verify a code_verifier against the stored S256 challenge in constant time. RFC 7636: the verifier is
    /// 43–128 unreserved ASCII chars; <c>challenge = BASE64URL(SHA256(ASCII(verifier)))</c>.
    /// </summary>
    public static bool VerifyS256(string? verifier, string challenge)
    {
        if (string.IsNullOrEmpty(verifier) || verifier.Length is < 43 or > 128) return false;
        var computed = OpaqueToken.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(computed),
            Encoding.ASCII.GetBytes(challenge));
    }
}
