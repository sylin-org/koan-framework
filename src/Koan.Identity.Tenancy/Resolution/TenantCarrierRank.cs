namespace Koan.Identity.Tenancy;

/// <summary>
/// Where a carrier stands when a request's carriers disagree. The lowest rank that names a tenant decides, whatever the
/// registration order, and the ranks below it are not read. Two carriers of one rank that name different tenants have
/// no order between them, so the request is refused.
/// </summary>
public enum TenantCarrierRank
{
    /// <summary>The address the person sees: its host or its path. The page shows this tenant.</summary>
    Address = 0,

    /// <summary>A statement this one request makes: a header its client sends.</summary>
    Request = 1,

    /// <summary>A standing default of the session: a claim minted at sign-in, which lasts across pages.</summary>
    Session = 2,
}
