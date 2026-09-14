namespace Koan.Identity;

/// <summary>Well-known host-level roles owned by the Identity domain.</summary>
public static class IdentityRoles
{
    /// <summary>
    /// Host operator authority over the global identity plane. Store it as a member collection in <see cref="Roles.Role"/>.
    /// </summary>
    public const string Operator = "koan:identity-operator";

    /// <summary>True when a role is reserved for the global Identity plane.</summary>
    public static bool IsReservedHostRole(string? role)
        => string.Equals(role?.Trim(), Operator, StringComparison.OrdinalIgnoreCase);
}
