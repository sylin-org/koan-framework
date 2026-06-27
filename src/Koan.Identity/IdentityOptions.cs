namespace Koan.Identity;

/// <summary>Options for the Identity module (bound to <c>Koan:Identity</c>).</summary>
public sealed class IdentityOptions
{
    /// <summary>Configuration section path.</summary>
    public const string SectionPath = "Koan:Identity";

    /// <summary>
    /// Explicit posture override (<c>Open</c> / <c>Closed</c>); <see langword="null"/> derives from the environment
    /// (Development → Open, otherwise Closed). A forced <c>Open</c> outside Development refuses to boot.
    /// </summary>
    public string? Posture { get; set; }

    /// <summary>Seed offline dev users at boot (Development + Open only). Default <see langword="true"/>.</summary>
    public bool SeedDevUsers { get; set; } = true;

    /// <summary>
    /// The primary dev person's id; defaults to <c>Koan:Data:Tenancy:DevUser</c> ?? the machine user, so the seeded
    /// person reconciles with the tenancy dev membership (<c>Membership.IdentityId</c>).
    /// </summary>
    public string? DevUser { get; set; }

    /// <summary>
    /// Opt into tamper-evident audit (Layer 3): each <c>AuditEvent</c> is hash-chained so altering a past event is
    /// detectable. Off by default — chaining serializes audit writes through the chain head (a deliberate cost).
    /// </summary>
    public bool HashChainAudit { get; set; }

    /// <summary>
    /// Opt into person ≠ email auto-merge (SEC-0007 D5): a new IdP sign-in carrying a VERIFIED email that already
    /// belongs to an Active person links onto that person instead of minting a duplicate.
    /// <para>
    /// <b>OFF by default — and unsafe to enable without a provider-trust policy.</b> The merge trusts the IdP's
    /// <c>email_verified</c> assertion; a low-assurance or attacker-operated IdP asserting a victim's verified email
    /// would take over the victim's person. The ADR marks the matching/collision rules an open question (provider /
    /// domain authority). Enable only once that policy exists (e.g. an authoritative-provider/domain allow-list, or
    /// require an explicit signed-in account-link rather than a fresh IdP claim).
    /// </para>
    /// </summary>
    public bool AutoMergeVerifiedEmail { get; set; }
}
