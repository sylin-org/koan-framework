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
}
