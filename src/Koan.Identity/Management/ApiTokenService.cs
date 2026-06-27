using System.Security.Cryptography;
using System.Text;
using Koan.Data.Core;

namespace Koan.Identity.Management;

/// <summary>
/// SEC-0007 Layer 1 — personal access tokens. The secret is shown once at issue and only its hash is stored.
/// Rotation creates a new row (same scopes, <see cref="ApiToken.RotatedFromId"/> set) and revokes the old one.
/// </summary>
public sealed class ApiTokenService
{
    /// <summary>The token entity plus the one-time secret (return this to the caller exactly once).</summary>
    public sealed record IssuedToken(ApiToken Token, string Secret);

    public async Task<IssuedToken> IssueAsync(string identityId, string name, IEnumerable<string> scopes, DateTimeOffset? expiresAt = null, CancellationToken ct = default)
    {
        var secret = GenerateSecret();
        var token = new ApiToken
        {
            IdentityId = identityId,
            Name = name,
            Scopes = scopes.ToList(),
            SecretHash = Hash(secret),
            ExpiresAt = expiresAt,
        };
        await token.Save(ct).ConfigureAwait(false);
        return new IssuedToken(token, secret);
    }

    /// <summary>Rotate a token: issue a new one with the same scopes, then revoke the old. Returns null if missing.</summary>
    public async Task<IssuedToken?> RotateAsync(string tokenId, CancellationToken ct = default)
    {
        var old = await ApiToken.Get(tokenId, ct).ConfigureAwait(false);
        if (old is null) return null;

        var issued = await IssueAsync(old.IdentityId, old.Name, old.Scopes, old.ExpiresAt, ct).ConfigureAwait(false);
        issued.Token.RotatedFromId = old.Id;
        await issued.Token.Save(ct).ConfigureAwait(false);

        old.Revoked = true;
        await old.Save(ct).ConfigureAwait(false);
        return issued;
    }

    public async Task<bool> RevokeAsync(string tokenId, CancellationToken ct = default)
    {
        var token = await ApiToken.Get(tokenId, ct).ConfigureAwait(false);
        if (token is null || token.Revoked) return false;
        token.Revoked = true;
        await token.Save(ct).ConfigureAwait(false);
        return true;
    }

    public Task<IReadOnlyList<ApiToken>> ListAsync(string identityId, CancellationToken ct = default)
        => ApiToken.Query(t => t.IdentityId == identityId, ct);

    private static string GenerateSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    /// <summary>SHA-256 of a presented secret, to compare against the stored <see cref="ApiToken.SecretHash"/>.</summary>
    public static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}
