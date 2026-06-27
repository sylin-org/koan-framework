using Koan.Data.Core;
using Koan.Identity.Credentials;

namespace Koan.Identity.Passwords;

/// <summary>
/// SEC-0007 P3-grp4 — set / verify a person's local password. Verify is <b>upgrade-on-verify</b>: in the only moment
/// it legitimately holds the plaintext, if the stored hash's cost is below current policy it rehashes transparently,
/// future-proofing the work factor (and enabling algorithm migration by swapping the <see cref="IPasswordHasher"/>).
/// The plaintext is never stored, logged, or returned.
/// </summary>
public sealed class PasswordCredentialService
{
    private readonly IPasswordHasher _hasher;

    public PasswordCredentialService(IPasswordHasher hasher) => _hasher = hasher;

    /// <summary>Set (or replace) <paramref name="identityId"/>'s local password. <paramref name="loginIdentifier"/> (normalized) is the sign-in handle.</summary>
    public async Task<LocalCredential> SetPasswordAsync(string identityId, string loginIdentifier, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(identityId)) throw new ArgumentException("Identity id is required.", nameof(identityId));
        if (string.IsNullOrWhiteSpace(loginIdentifier)) throw new ArgumentException("A login identifier is required.", nameof(loginIdentifier));
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("A password is required.", nameof(password));

        var normalized = IdentityEmail.Normalize(loginIdentifier);

        // Collision guard: a login handle must resolve to ONE person — otherwise VerifyAsync fails closed on the
        // ambiguous match and BOTH accounts are silently, permanently locked out of password login. Reject a handle
        // already owned by a different person (a re-set by the SAME person is fine — deterministic id upserts one row).
        var sharing = await LocalCredential.Query(c => c.LoginIdentifier == normalized, ct).ConfigureAwait(false);
        if (sharing.Any(c => c.IdentityId != identityId))
            throw new InvalidOperationException($"The login identifier '{normalized}' is already in use by another account.");

        var credential = new LocalCredential
        {
            Id = LocalCredential.KeyFor(identityId),
            IdentityId = identityId,
            LoginIdentifier = normalized,
            PasswordHash = _hasher.Hash(password),
            Scheme = _hasher.Scheme,
        };
        return await credential.Save(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Verify a login. Returns the canonical person id when the password is correct (rehashing in place if policy
    /// moved), else null. Fails closed on a missing OR ambiguous login identifier.
    /// </summary>
    public async Task<string?> VerifyAsync(string loginIdentifier, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(loginIdentifier) || string.IsNullOrEmpty(password)) return null;
        var normalized = IdentityEmail.Normalize(loginIdentifier);

        var matches = await LocalCredential.Query(c => c.LoginIdentifier == normalized, ct).ConfigureAwait(false);
        if (matches.Count != 1) return null; // fail closed: no credential, or an ambiguous duplicate login handle
        var credential = matches[0];

        if (!_hasher.Verify(password, credential.PasswordHash)) return null;

        if (_hasher.NeedsRehash(credential.PasswordHash))
        {
            credential.PasswordHash = _hasher.Hash(password);
            credential.Scheme = _hasher.Scheme;
            await credential.Save(ct).ConfigureAwait(false);
        }
        return credential.IdentityId;
    }

    /// <summary>True when <paramref name="identityId"/> has a local password factor.</summary>
    public async Task<bool> HasPasswordAsync(string identityId, CancellationToken ct = default)
        => await LocalCredential.Get(LocalCredential.KeyFor(identityId), ct).ConfigureAwait(false) is not null;
}
