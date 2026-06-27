namespace Koan.Data.Access;

/// <summary>SEC-0008 — data-layer access-scoping options (bound from <c>Koan:Data:Access</c>).</summary>
public sealed class AccessOptions
{
    public const string SectionPath = "Koan:Data:Access";

    /// <summary>
    /// When <c>true</c> (the safe default), a read of an <c>[AccessScoped]</c> entity with <b>no subject in scope</b>
    /// returns nothing (deny-all) — fail closed. Set <c>false</c> (dev / a trusted batch context) to make an absent
    /// subject a no-op (full read). A <b>constrained</b> subject is always narrowed regardless; this only governs the
    /// absent-subject case. Legitimate full-access code declares <c>Subject.System()</c> / <c>Subject.Use(id)</c>.
    /// </summary>
    public bool FailClosedOnAbsentSubject { get; set; } = true;
}
