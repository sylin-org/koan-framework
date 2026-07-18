namespace Koan.Identity.Web;

/// <summary>Well-known roles the consoles gate on. Grant them globally through <c>IdentityRole</c> or per tenant through <c>Membership.Roles</c>.</summary>
public static class IdentityWebRoles
{
    /// <summary>The operator/admin role required by the operator console.</summary>
    public const string Operator = "koan:identity-operator";
}
