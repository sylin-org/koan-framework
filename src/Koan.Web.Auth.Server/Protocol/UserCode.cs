using System.Security.Cryptography;

namespace Koan.Web.Auth.Server.Protocol;

/// <summary>
/// SEC-0006 D8 — the human-typed device <c>user_code</c> (RFC 8628). Short but high-entropy, drawn from an
/// unambiguous alphabet (no <c>0/O</c>, <c>1/I/L</c>, no vowels → no accidental words), formatted <c>XXXX-XXXX</c>.
/// 8 chars over a 31-symbol alphabet ≈ 40 bits — paired with rate-limited verification + a short lifetime, that
/// is well out of brute-force range.
/// </summary>
internal static class UserCode
{
    private const string Alphabet = "BCDFGHJKMNPQRSTVWXZ23456789"; // 27 unambiguous symbols

    /// <summary>A fresh code in its canonical (normalized, dash-less) lookup form, e.g. "BCDFGHJK".</summary>
    public static string New()
    {
        Span<char> chars = stackalloc char[8];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }

    /// <summary>The human-friendly display form, e.g. "BCDF-GHJK".</summary>
    public static string Format(string code)
        => code.Length == 8 ? code[..4] + "-" + code[4..] : code;

    /// <summary>Normalize user input: upper-case, strip spaces/dashes — so "bcdf-ghjk" matches "BCDFGHJK".</summary>
    public static string Normalize(string? input)
        => new((input ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
