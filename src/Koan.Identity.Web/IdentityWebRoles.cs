namespace Koan.Identity.Web;

/// <summary>Well-known roles the consoles gate on. Bind these in the role catalog (Koan.Web.Auth.Roles).</summary>
public static class IdentityWebRoles
{
    /// <summary>The operator/admin role required by the operator console.</summary>
    public const string Operator = "koan:identity-operator";
}
