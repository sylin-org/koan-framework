using Koan.Data.Core.Model;

namespace Koan.Web.Auth.Server.Keys;

/// <summary>
/// SEC-0006 D1 — a persisted ES256 signing key for the embedded Authorization Server. The private key is stored
/// <b>encrypted-at-rest</b> (an <c>IDataProtector</c>-protected PKCS#8 blob); the row id is the key's <c>kid</c>.
/// One row is <see cref="IsActive"/> (signs new tokens); rotated-out rows linger (validate-only, published in the
/// JWKS) until <see cref="RetireAfterUtc"/>, then are purged. Entities are the Koan move — <c>Save()</c> to
/// persist, <c>Query(...)</c> to load, <c>Remove()</c> to purge.
/// </summary>
public sealed class IssuerSigningKeyRecord : Entity<IssuerSigningKeyRecord>
{
    /// <summary>The <c>IDataProtector</c>-protected PKCS#8 private key, base64. Never stored in the clear.</summary>
    public string ProtectedPkcs8 { get; set; } = "";

    /// <summary>When this key was generated (drives rotation timing).</summary>
    public DateTimeOffset CreatedUtc { get; set; }

    /// <summary>True for the single key that currently signs new tokens.</summary>
    public bool IsActive { get; set; }

    /// <summary>For a rotated-out key: when its last possible token has expired and the row may be purged.</summary>
    public DateTimeOffset? RetireAfterUtc { get; set; }
}
