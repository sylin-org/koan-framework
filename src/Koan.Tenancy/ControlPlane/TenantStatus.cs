namespace Koan.Tenancy;

/// <summary>The lifecycle status of a <see cref="TenantRecord"/> (ARCH-0099 §2). <see cref="Active"/> is the fail-safe default (value 0).</summary>
public enum TenantStatus
{
    /// <summary>The tenant is active and serving.</summary>
    Active = 0,

    /// <summary>The tenant is quiesced — reads may continue, but it is administratively suspended (P3 in the design).</summary>
    Suspended = 1,
}
