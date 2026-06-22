namespace Koan.Tenancy;

/// <summary>
/// The tenancy activation gradient (ARCH-0095 §4). Reference = Intent plus fail-closed would otherwise make
/// referencing the module throw on every un-scoped op the moment it is added; the gradient defuses that —
/// activation moves <see cref="Off"/> → <see cref="Warn"/> → <see cref="Enforce"/> by config. Default is
/// <see cref="Off"/> (single-tenant apps pay nothing even with the module referenced).
/// </summary>
public enum TenancyMode
{
    /// <summary>Tenancy disabled — the guard is a no-op; behavior is identical to a non-tenant app.</summary>
    Off = 0,

    /// <summary>Tenant-scoped ops with no tenant in scope are <b>logged</b> with the fix, not blocked.</summary>
    Warn = 1,

    /// <summary>Tenant-scoped ops with no tenant in scope <b>fail closed</b> (throw a fix-naming error).</summary>
    Enforce = 2,
}
