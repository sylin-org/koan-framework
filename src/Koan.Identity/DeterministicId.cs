using System.Security.Cryptography;
using System.Text;

namespace Koan.Identity;

/// <summary>
/// Builds a deterministic entity id from delimited field parts: re-deriving the same parts yields the same id, so a
/// repeat write <b>upserts</b> the one row instead of appending a duplicate — collapsing read-then-write races
/// (TOCTOU) structurally rather than by discipline. The unit-separator (<c>U+001F</c>) delimiter cannot occur in
/// the inputs, so it removes the field-boundary ambiguity a bare concatenation would have
/// (<c>From("ab","cd") == From("a","bcd")</c> would otherwise hold).
/// </summary>
internal static class DeterministicId
{
    private const char UnitSeparator = (char)0x1f; // U+001F — cannot occur in ids / emails / provider keys

    public static string From(params string[] parts)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(UnitSeparator, parts))));
}
