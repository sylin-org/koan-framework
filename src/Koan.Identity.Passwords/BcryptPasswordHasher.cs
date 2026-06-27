using System.Text;
using Koan.Identity.Credentials;
using BC = BCrypt.Net.BCrypt;

namespace Koan.Identity.Passwords;

/// <summary>
/// SEC-0007 P3-grp4 (D2) — the default <see cref="IPasswordHasher"/>: BCrypt.Net-Next emitting a portable bcrypt MCF
/// string (the <c>$2…$</c> cross-language de-facto standard). Plain mode — no <c>enhancedEntropy</c>/SHA pre-hash,
/// which would break portability. The cost is readable back from the stored hash, so <see cref="NeedsRehash"/> drives
/// upgrade-on-verify (and an app can swap in a memory-hard Argon2id hasher behind the same seam). bcrypt silently
/// ignores bytes past 72, so <see cref="Hash"/> rejects an over-length input rather than truncate it.
/// </summary>
public sealed class BcryptPasswordHasher : IPasswordHasher
{
    private const int MaxPasswordBytes = 72; // bcrypt's hard input limit — beyond this it truncates silently

    private readonly int _workFactor;

    /// <param name="workFactor">The bcrypt cost (log2 rounds). Default 12 — a sane 2025 floor.</param>
    public BcryptPasswordHasher(int workFactor = 12) => _workFactor = workFactor;

    public string Scheme => "bcrypt";

    public string Hash(string password)
    {
        // Reject rather than truncate — otherwise two passwords sharing the first 72 bytes would both verify.
        if (Encoding.UTF8.GetByteCount(password) > MaxPasswordBytes)
            throw new ArgumentException($"Password exceeds bcrypt's {MaxPasswordBytes}-byte limit; use a shorter passphrase.", nameof(password));
        return BC.HashPassword(password, _workFactor);
    }

    public bool Verify(string password, string hash)
    {
        try { return BC.Verify(password, hash); }
        catch (BCrypt.Net.SaltParseException) { return false; } // a malformed/foreign hash never authenticates
    }

    public bool NeedsRehash(string hash) => BC.PasswordNeedsRehash(hash, _workFactor);
}
