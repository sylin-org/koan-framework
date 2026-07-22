using Koan.Identity.Audit;

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
    public IdentityPosture? Posture { get; set; }

    /// <summary>Seed offline dev users at boot (Development + Open only). Default <see langword="true"/>.</summary>
    public bool SeedDevUsers { get; set; } = true;

    /// <summary>
    /// The primary local person's id; defaults to the current machine user.
    /// </summary>
    public string? DevUser { get; set; }

    /// <summary>
    /// Opt into tamper-evident audit (Layer 3): each <c>AuditEvent</c> is hash-chained so altering a past event is
    /// detectable. Off by default — chaining serializes audit writes through the chain head (a deliberate cost).
    /// </summary>
    public bool HashChainAudit { get; set; }

    /// <summary>
    /// Audit before/after snapshot posture. Privacy-safe bounded metadata is the default; <c>Full</c> is an explicit
    /// forensic compatibility choice and is still sanitized by identity erasure.
    /// </summary>
    public IdentityAuditSnapshotMode AuditSnapshotMode { get; set; } = IdentityAuditSnapshotMode.PrivacySafe;
}
