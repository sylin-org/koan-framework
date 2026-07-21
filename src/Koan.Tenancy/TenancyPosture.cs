namespace Koan.Tenancy;

/// <summary>
/// The tenancy posture (ARCH-0099 §1) — derived once at boot from the in-core <see cref="Koan.Core.KoanEnv"/>
/// snapshot, never a cross-environment flag. <see cref="Open"/> in Development (the developer lands in a working
/// control plane; a tenant-scoped op with no tenant in scope is warned, not blocked); <see cref="Closed"/>
/// otherwise (Production, Staging, unset, or ambiguous — the ASP.NET <c>IsDevelopment()</c> rule resolves the
/// ambiguous case to closed), where the same op fails closed.
///
/// <para>This replaces the retired <c>TenancyMode.Off</c> default: referencing <c>Koan.Tenancy</c> activates
/// tenancy (Reference = Intent), and the posture — not a flag — decides how strict it is. <see cref="Closed"/>
/// is value <c>0</c> on purpose, so an uninitialized posture is fail-safe (closed).</para>
/// </summary>
public enum TenancyPosture
{
    /// <summary>Production / Staging / ambiguous — a tenant-scoped op with no tenant in scope <b>fails closed</b>.</summary>
    Closed = 0,

    /// <summary>Development (dev-open) — a tenant-scoped op with no tenant in scope is <b>logged with the fix</b>, not blocked.</summary>
    Open = 1,
}
