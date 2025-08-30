using Sora.Data.Core.Model;
using Sora.Web.Auth.Roles.Contracts;

namespace Sora.Web.Auth.Roles.Model;

public sealed class Role : Entity<Role>, ISoraAuthRole
{
    public string? Display { get; set; }
    public string? Description { get; set; }
    public byte[]? RowVersion { get; set; }
}

public sealed class RoleAlias : Entity<RoleAlias>, ISoraAuthRoleAlias
{
    // Id = alias key
    public string TargetRole { get; set; } = string.Empty;
    public byte[]? RowVersion { get; set; }
}

public sealed class RolePolicyBinding : Entity<RolePolicyBinding>, ISoraAuthRolePolicyBinding
{
    // Id = policy name
    public string Requirement { get; set; } = string.Empty;
    public byte[]? RowVersion { get; set; }
}
