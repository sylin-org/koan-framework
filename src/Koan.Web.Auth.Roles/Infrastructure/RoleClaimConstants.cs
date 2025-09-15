using System.Security.Claims;

namespace Koan.Web.Auth.Roles.Infrastructure;

public static class RoleClaimConstants
{
    public const string KoanRole = "Koan:role";
    public const string KoanRoles = "Koan:roles";
    public const string KoanPermission = "Koan:perm";
    public const string KoanRoleVersion = "Koan:rolever";

    public static readonly string RoleType = ClaimTypes.Role; // canonical role claim type for ASP.NET Core
}
