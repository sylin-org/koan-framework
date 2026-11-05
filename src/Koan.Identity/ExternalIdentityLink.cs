using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;

namespace Koan.Identity;

/// <summary>
/// SEC-0007 D5, SEC-0040 — a login: the durable record binding an external provider identity (provider + key hash) to
/// the Koan <see cref="Identity"/> it signs in as, and the one way from a credential back to its person. The
/// Entity&lt;&gt;-backed twin of <c>Koan.Web.Auth.Domain.ExternalIdentity</c>. The entity <c>Id</c> is a deterministic hash
/// of (subject, provider, keyHash), so a login is one row wherever it is written, and a first write is made only if
/// absent. A person holds at most one login per provider.
/// </summary>
public sealed class ExternalIdentityLink : Entity<ExternalIdentityLink>, IAmbientExempt
{
    /// <summary>The person the login signs in as.</summary>
    [Parent(typeof(Identity))]
    [Index]
    public string IdentityId { get; set; } = "";

    /// <summary>The auth provider (e.g. <c>google</c>, <c>discord</c>).</summary>
    public string Provider { get; set; } = "";

    /// <summary>SHA-256 hash of the provider subject (never the raw sub).</summary>
    public string ProviderKeyHash { get; set; } = "";

    /// <summary>The raw provider userinfo JSON from the login's latest sign-in (best-effort).</summary>
    public string? ClaimsJson { get; set; }

    /// <summary>Set once, on creation.</summary>
    [Timestamp]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The deterministic id of the login (<paramref name="subject"/>, <paramref name="provider"/>, <paramref name="providerKeyHash"/>).</summary>
    public static string KeyFor(string subject, string provider, string providerKeyHash)
        => DeterministicId.From(subject, provider, providerKeyHash);
}
