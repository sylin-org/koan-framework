namespace Koan.Data.Core.Tenancy;

/// <summary>
/// The tenancy activation gradient (ARCH-0095 §4). Reference = Intent plus fail-closed would otherwise
/// make referencing the package throw on every un-scoped op the moment it is added; the gradient defuses
/// that — first activation defaults to <see cref="Warn"/>, an explicit config flip moves to
/// <see cref="Enforce"/>. Default is <see cref="Off"/> (single-tenant apps pay nothing).
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
