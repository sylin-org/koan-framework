using System.Security.Claims;

namespace Sora.Web.Auth.Roles.Infrastructure;

public static class RoleClaimConstants
{
    public const string SoraRole = "sora:role";
    public const string SoraRoles = "sora:roles";
    public const string SoraPermission = "sora:perm";
    public const string SoraRoleVersion = "sora:rolever";

    public static readonly string RoleType = ClaimTypes.Role; // canonical role claim type for ASP.NET Core
}
