using Koan.Data.Core.Model;
using Koan.Web.Auth.Roles.Contracts;

namespace Koan.Web.Auth.Roles.Model;

public sealed class Role : Entity<Role>, IKoanAuthRole
{
    public string? Display { get; set; }
    public string? Description { get; set; }
    public byte[]? RowVersion { get; set; }
}

public sealed class RoleAlias : Entity<RoleAlias>, IKoanAuthRoleAlias
{
    // Id = alias key
    public string TargetRole { get; set; } = string.Empty;
    public byte[]? RowVersion { get; set; }
}

public sealed class RolePolicyBinding : Entity<RolePolicyBinding>, IKoanAuthRolePolicyBinding
{
    // Id = policy name
    public string Requirement { get; set; } = string.Empty;
    public byte[]? RowVersion { get; set; }
}
