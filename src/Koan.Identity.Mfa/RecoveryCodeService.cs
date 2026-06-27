using System.Security.Cryptography;
using System.Text;
using Koan.Data.Core;
using Koan.Identity.Credentials;

namespace Koan.Identity.Mfa;

/// <summary>
/// SEC-0007 P3-grp4 — pre-provisioned, single-use account-recovery codes. <see cref="GenerateAsync"/> replaces the
/// person's whole prior set and returns the plaintext codes to show <b>once</b> (only hashes are stored);
/// <see cref="RedeemAsync"/> burns a matching code. High entropy ⇒ a fast hash (SHA-256) is the right tool — the
/// entropy, not a slow KDF, is the defense.
/// </summary>
public sealed class RecoveryCodeService
{
    /// <summary>Replace the person's recovery-code set with <paramref name="count"/> fresh codes; returns the plaintext codes (shown once).</summary>
    public async Task<IReadOnlyList<string>> GenerateAsync(string identityId, int count = 10, CancellationToken ct = default)
    {
        // A regeneration REPLACES — burn the prior set so old printouts stop working.
        foreach (var prior in await RecoveryCode.Query(c => c.IdentityId == identityId, ct).ConfigureAwait(false))
            await prior.Remove(ct).ConfigureAwait(false);

        var setId = Guid.NewGuid().ToString("n");
        var codes = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var code = GenerateCode();
            codes.Add(code);
            await new RecoveryCode { IdentityId = identityId, SetId = setId, CodeHash = Hash(Normalize(code)) }.Save(ct).ConfigureAwait(false);
        }
        return codes;
    }

    /// <summary>
    /// Burn a matching, unused recovery code. Returns false if it does not match an available code, OR if a concurrent
    /// redemption already burned it — the compare-and-set guard makes single-use true under real concurrency, so a
    /// code cannot survive its own use even on a double-submit.
    /// </summary>
    public async Task<bool> RedeemAsync(string identityId, string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        var hash = Hash(Normalize(code));
        var matches = await RecoveryCode.Query(c => c.IdentityId == identityId && c.CodeHash == hash && c.UsedAt == null, ct).ConfigureAwait(false);
        var match = matches.FirstOrDefault();
        if (match is null) return false;

        match.UsedAt = DateTimeOffset.UtcNow;
        return await AtomicSingleUse.TryAsync<RecoveryCode, string>(match, c => c.UsedAt == null, ct).ConfigureAwait(false);
    }

    /// <summary>How many of the person's recovery codes remain unused.</summary>
    public async Task<int> RemainingAsync(string identityId, CancellationToken ct = default)
        => (await RecoveryCode.Query(c => c.IdentityId == identityId && c.UsedAt == null, ct).ConfigureAwait(false)).Count;

    private static string GenerateCode()
    {
        var hex = Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant(); // 40 bits, 10 hex chars
        return $"{hex[..5]}-{hex[5..]}";
    }

    private static string Normalize(string code) => code.Replace("-", "").Trim().ToLowerInvariant();

    private static string Hash(string normalized) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
}
