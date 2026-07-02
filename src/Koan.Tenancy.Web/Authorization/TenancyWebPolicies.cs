namespace Koan.Tenancy.Web.Authorization;

/// <summary>
/// The authorization policy names for the tenancy control-plane console (ARCH-0104). The console is gated on a
/// single policy that resolves the tenancy posture: dev-open makes it just-work in dev; prod-closed requires the
/// explicit host role <see cref="Koan.Tenancy.TenancyRoles.Operator"/> and fails closed.
/// </summary>
public static class TenancyWebPolicies
{
    /// <summary>The host-face operator/service-owner gate. See <see cref="OperatorAuthorizationHandler"/>.</summary>
    public const string Operator = "koan:tenancy-operator";
}
