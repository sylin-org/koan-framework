using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;

namespace Koan.Identity;

/// <summary>
/// SEC-0007 D5 — the durable record relating an external provider identity (provider + key hash) to a Koan
/// <see cref="Identity"/>. The Entity&lt;&gt;-backed twin of <c>Koan.Web.Auth.Domain.ExternalIdentity</c> (a plain
/// value class): account-linking falls out of "many factors on one person". The entity <c>Id</c> is a
/// deterministic hash of (userId, provider, keyHash) so re-linking the same provider identity is idempotent
/// (one row, not a duplicate).
/// </summary>
public sealed class ExternalIdentityLink : Entity<ExternalIdentityLink>, IAmbientExempt
{
    /// <summary>The owning person (== the provider <c>sub</c>, which is the <see cref="Identity"/> id).</summary>
    [Parent(typeof(Identity))]
    public string IdentityId { get; set; } = "";

    /// <summary>The auth provider (e.g. <c>google</c>, <c>discord</c>).</summary>
    public string Provider { get; set; } = "";

    /// <summary>SHA-256 hash of the provider subject (never the raw sub).</summary>
    public string ProviderKeyHash { get; set; } = "";

    /// <summary>The raw provider userinfo JSON captured at link time (best-effort).</summary>
    public string? ClaimsJson { get; set; }

    /// <summary>Set once, on creation.</summary>
    [Timestamp]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The deterministic id for a (userId, provider, keyHash) triple — keeps <c>Link</c> idempotent.</summary>
    public static string KeyFor(string userId, string provider, string providerKeyHash)
        => DeterministicId.From(userId, provider, providerKeyHash);
}
