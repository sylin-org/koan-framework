namespace Koan.Identity.Roles;

public sealed record ScopedRoleChangeDecision(bool Allowed, string Code, string Message)
{
    public static ScopedRoleChangeDecision Continue() => new(true, "role.change.allowed", "The role change may continue.");
    public static ScopedRoleChangeDecision Veto(string code, string message) => new(false, code, message);
}
