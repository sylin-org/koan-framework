namespace Koan.Identity.Credentials;

/// <summary>
/// SEC-0007 P3-grp4 (D2) — the pluggable password hasher. The default (<c>Koan.Identity.Passwords</c>) is
/// BCrypt.Net-Next emitting a <b>portable, self-describing PHC/MCF string</b> (algorithm + cost + salt + digest in
/// one column), so a hash is import/export-able across languages with no bespoke auth-storage adapter — and the
/// cost/params are readable back from the stored hash, enabling <see cref="NeedsRehash"/> (upgrade-on-verify) and
/// even algorithm migration by swapping the implementation. An app may register a memory-hard (Argon2id) hasher.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>A short, stable name for the boot report / diagnostics (e.g. <c>bcrypt</c>, <c>argon2id</c>).</summary>
    string Scheme { get; }

    /// <summary>Hash <paramref name="password"/> into a portable PHC/MCF string at the current policy cost.</summary>
    string Hash(string password);

    /// <summary>Verify <paramref name="password"/> against a stored <paramref name="hash"/> (the implementation compares the digest, not the raw input).</summary>
    bool Verify(string password, string hash);

    /// <summary>True when <paramref name="hash"/>'s embedded cost/params are below current policy — verify-then-rehash to upgrade it transparently.</summary>
    bool NeedsRehash(string hash);
}
